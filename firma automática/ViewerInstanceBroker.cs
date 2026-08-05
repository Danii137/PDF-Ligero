using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Security;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace FirmaAutomatica
{
    /// <summary>
    /// Keeps one viewer instance alive and forwards later PDF invocations to it.
    /// The primary instance owns this object for the lifetime of the viewer.
    /// </summary>
    internal sealed class ViewerInstanceBroker : IDisposable
    {
        private const int ProtocolMagic = 0x5044464C; // "PDFL"
        private const int ProtocolVersion = 1;
        private const int MaximumPathsPerInvocation = 64;
        private const int MaximumEncodedPathBytes = 65536;
        private const int ForwardingWindowMilliseconds = 900;
        private const int ConnectAttemptMilliseconds = 225;
        private const int PendingInvocationLimit = 16;

        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private readonly object syncRoot = new object();
        private readonly string pipeName;
        private readonly Queue<IReadOnlyList<string>> pendingInvocations =
            new Queue<IReadOnlyList<string>>();
        private readonly Thread listenerThread;

        private Mutex instanceMarker;
        private NamedPipeServerStream waitingServer;
        private Action<IReadOnlyList<string>> pdfPathsReceived;
        private bool disposed;

        private ViewerInstanceBroker(Mutex marker, string primaryPipeName)
        {
            if (marker == null)
            {
                throw new ArgumentNullException("marker");
            }

            instanceMarker = marker;
            pipeName = primaryPipeName;
            listenerThread = new Thread(Listen)
            {
                IsBackground = true,
                Name = "PDF Ligero viewer instance listener"
            };
            listenerThread.Start();
        }

        /// <summary>
        /// Raised on a ThreadPool thread when a later invocation is received.
        /// An empty list means that PDF Ligero was launched without a file and
        /// can be used by the form as a request to activate the existing window.
        /// </summary>
        public event Action<IReadOnlyList<string>> PdfPathsReceived
        {
            add
            {
                if (value == null)
                {
                    return;
                }

                List<IReadOnlyList<string>> backlog = null;
                Action<IReadOnlyList<string>> handlers;
                lock (syncRoot)
                {
                    ThrowIfDisposed();
                    pdfPathsReceived += value;
                    handlers = pdfPathsReceived;

                    if (pendingInvocations.Count > 0)
                    {
                        backlog = new List<IReadOnlyList<string>>(pendingInvocations);
                        pendingInvocations.Clear();
                    }
                }

                if (backlog == null)
                {
                    return;
                }

                foreach (var paths in backlog)
                {
                    QueueDelivery(handlers, paths);
                }
            }
            remove
            {
                if (value == null)
                {
                    return;
                }

                lock (syncRoot)
                {
                    pdfPathsReceived -= value;
                }
            }
        }

        /// <summary>
        /// Attempts to become the persistent viewer instance.
        /// Returns true and a broker for the primary process. When it returns
        /// false, the paths (including an empty invocation) have been offered
        /// to the existing primary and the caller should exit.
        /// </summary>
        public static bool TryStart(
            IEnumerable<string> invocationPaths,
            out ViewerInstanceBroker primaryBroker)
        {
            primaryBroker = null;
            var paths = NormalizePdfPaths(invocationPaths);
            var names = BuildInstanceNames();

            Mutex marker;
            bool createdNew;
            marker = new Mutex(false, names.MarkerName, out createdNew);
            if (createdNew)
            {
                primaryBroker = CreatePrimary(marker, names.PipeName);
                return true;
            }

            marker.Dispose();
            if (TryForward(paths, names.PipeName, ForwardingWindowMilliseconds))
            {
                return false;
            }

            // The former primary may have exited between the marker check and
            // the pipe connection. Make one lock-free takeover attempt.
            marker = new Mutex(false, names.MarkerName, out createdNew);
            if (createdNew)
            {
                primaryBroker = CreatePrimary(marker, names.PipeName);
                return true;
            }

            marker.Dispose();

            // A live primary may briefly have been between two accept calls.
            // This final short attempt keeps that uncommon race transparent.
            TryForward(paths, names.PipeName, ConnectAttemptMilliseconds);
            return false;
        }

        public void Dispose()
        {
            NamedPipeServerStream server;
            Mutex marker;
            lock (syncRoot)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                server = waitingServer;
                waitingServer = null;
                marker = instanceMarker;
                instanceMarker = null;
                pdfPathsReceived = null;
                pendingInvocations.Clear();
            }

            if (server != null)
            {
                try
                {
                    server.Dispose();
                }
                catch
                {
                }
            }

            if (listenerThread != null &&
                listenerThread != Thread.CurrentThread &&
                listenerThread.IsAlive)
            {
                listenerThread.Join(500);
            }

            if (marker != null)
            {
                marker.Dispose();
            }

            GC.SuppressFinalize(this);
        }

        private static ViewerInstanceBroker CreatePrimary(Mutex marker, string primaryPipeName)
        {
            try
            {
                return new ViewerInstanceBroker(marker, primaryPipeName);
            }
            catch
            {
                marker.Dispose();
                throw;
            }
        }

        private void Listen()
        {
            while (!IsDisposed())
            {
                NamedPipeServerStream server = null;
                try
                {
                    server = CreateServer(pipeName);
                    lock (syncRoot)
                    {
                        if (disposed)
                        {
                            server.Dispose();
                            return;
                        }

                        waitingServer = server;
                    }

                    server.WaitForConnection();
                    var paths = TryReadInvocation(server);
                    if (paths != null)
                    {
                        Publish(paths);
                    }
                }
                catch (ObjectDisposedException)
                {
                    if (IsDisposed())
                    {
                        return;
                    }
                }
                catch (IOException)
                {
                    if (IsDisposed())
                    {
                        return;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    if (IsDisposed())
                    {
                        return;
                    }

                    Thread.Sleep(100);
                }
                catch (Exception)
                {
                    if (IsDisposed())
                    {
                        return;
                    }

                    // An IPC failure must never terminate the WinForms process.
                    Thread.Sleep(100);
                }
                finally
                {
                    lock (syncRoot)
                    {
                        if (ReferenceEquals(waitingServer, server))
                        {
                            waitingServer = null;
                        }
                    }

                    if (server != null)
                    {
                        try
                        {
                            server.Dispose();
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        private static NamedPipeServerStream CreateServer(string primaryPipeName)
        {
            SecurityIdentifier userSid;
            using (var identity = WindowsIdentity.GetCurrent())
            {
                userSid = identity == null ? null : identity.User;
            }

            if (userSid == null)
            {
                return new NamedPipeServerStream(
                    primaryPipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
            }

            try
            {
                var security = new PipeSecurity();
                security.SetAccessRuleProtection(true, false);
                security.SetOwner(userSid);
                security.AddAccessRule(
                    new PipeAccessRule(
                        userSid,
                        PipeAccessRights.FullControl,
                        AccessControlType.Allow));

                return CreateSecuredServer(primaryPipeName, security);
            }
            catch (MissingMethodException)
            {
                // Keeps isolated tests usable on runtimes that omit the
                // .NET Framework PipeSecurity constructor.
            }
            catch (PlatformNotSupportedException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return new NamedPipeServerStream(
                primaryPipeName,
                PipeDirection.In,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
        }

        private static NamedPipeServerStream CreateSecuredServer(
            string primaryPipeName,
            PipeSecurity security)
        {
            return new NamedPipeServerStream(
                primaryPipeName,
                PipeDirection.In,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                4096,
                4096,
                security);
        }

        private static bool TryForward(
            IReadOnlyList<string> paths,
            string primaryPipeName,
            int forwardingWindowMilliseconds)
        {
            var timer = Stopwatch.StartNew();
            do
            {
                try
                {
                    using (var client = new NamedPipeClientStream(
                        ".",
                        primaryPipeName,
                        PipeDirection.Out,
                        PipeOptions.Asynchronous))
                    {
                        var remaining = forwardingWindowMilliseconds - (int)timer.ElapsedMilliseconds;
                        var timeout = Math.Max(
                            1,
                            Math.Min(ConnectAttemptMilliseconds, remaining));
                        client.Connect(timeout);
                        WriteInvocation(client, paths);
                        client.Flush();
                        return true;
                    }
                }
                catch (TimeoutException)
                {
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                    return false;
                }

                if (timer.ElapsedMilliseconds < forwardingWindowMilliseconds)
                {
                    Thread.Sleep(20);
                }
            }
            while (timer.ElapsedMilliseconds < forwardingWindowMilliseconds);

            return false;
        }

        private static void WriteInvocation(Stream stream, IReadOnlyList<string> paths)
        {
            WriteInt32(stream, ProtocolMagic);
            WriteInt32(stream, ProtocolVersion);
            WriteInt32(stream, paths.Count);

            for (var index = 0; index < paths.Count; index++)
            {
                var encodedPath = StrictUtf8.GetBytes(paths[index]);
                if (encodedPath.Length > MaximumEncodedPathBytes)
                {
                    throw new IOException("La ruta PDF supera el limite admitido.");
                }

                WriteInt32(stream, encodedPath.Length);
                stream.Write(encodedPath, 0, encodedPath.Length);
            }
        }

        private static IReadOnlyList<string> TryReadInvocation(Stream stream)
        {
            int magic;
            int version;
            int pathCount;
            if (!TryReadInt32(stream, out magic) ||
                !TryReadInt32(stream, out version) ||
                !TryReadInt32(stream, out pathCount) ||
                magic != ProtocolMagic ||
                version != ProtocolVersion ||
                pathCount < 0 ||
                pathCount > MaximumPathsPerInvocation)
            {
                return null;
            }

            var received = new List<string>(pathCount);
            for (var index = 0; index < pathCount; index++)
            {
                int byteCount;
                if (!TryReadInt32(stream, out byteCount) ||
                    byteCount < 0 ||
                    byteCount > MaximumEncodedPathBytes)
                {
                    return null;
                }

                var encodedPath = new byte[byteCount];
                if (!TryReadExactly(stream, encodedPath, byteCount))
                {
                    return null;
                }

                try
                {
                    received.Add(StrictUtf8.GetString(encodedPath));
                }
                catch (DecoderFallbackException)
                {
                    return null;
                }
            }

            return NormalizePdfPaths(received);
        }

        private static IReadOnlyList<string> NormalizePdfPaths(IEnumerable<string> paths)
        {
            var normalized = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (paths == null)
            {
                return new ReadOnlyCollection<string>(normalized);
            }

            foreach (var rawPath in paths)
            {
                if (normalized.Count >= MaximumPathsPerInvocation ||
                    string.IsNullOrWhiteSpace(rawPath))
                {
                    continue;
                }

                try
                {
                    var candidate = rawPath.Trim().Trim('"');
                    if (!string.Equals(
                        Path.GetExtension(candidate),
                        ".pdf",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var fullPath = Path.GetFullPath(candidate);
                    if (File.Exists(fullPath) && seen.Add(fullPath))
                    {
                        normalized.Add(fullPath);
                    }
                }
                catch (ArgumentException)
                {
                }
                catch (NotSupportedException)
                {
                }
                catch (PathTooLongException)
                {
                }
                catch (SecurityException)
                {
                }
            }

            return new ReadOnlyCollection<string>(normalized);
        }

        private void Publish(IReadOnlyList<string> paths)
        {
            Action<IReadOnlyList<string>> handlers;
            lock (syncRoot)
            {
                if (disposed)
                {
                    return;
                }

                handlers = pdfPathsReceived;
                if (handlers == null)
                {
                    if (pendingInvocations.Count >= PendingInvocationLimit)
                    {
                        pendingInvocations.Dequeue();
                    }

                    pendingInvocations.Enqueue(paths);
                    return;
                }
            }

            QueueDelivery(handlers, paths);
        }

        private void QueueDelivery(
            Action<IReadOnlyList<string>> handlers,
            IReadOnlyList<string> paths)
        {
            ThreadPool.QueueUserWorkItem(
                delegate
                {
                    if (IsDisposed())
                    {
                        return;
                    }

                    foreach (Action<IReadOnlyList<string>> handler in handlers.GetInvocationList())
                    {
                        try
                        {
                            handler(paths);
                        }
                        catch
                        {
                            // A UI handler must not terminate the persistent listener.
                        }
                    }
                });
        }

        private bool IsDisposed()
        {
            lock (syncRoot)
            {
                return disposed;
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException("ViewerInstanceBroker");
            }
        }

        private static void WriteInt32(Stream stream, int value)
        {
            var buffer = BitConverter.GetBytes(value);
            stream.Write(buffer, 0, buffer.Length);
        }

        private static bool TryReadInt32(Stream stream, out int value)
        {
            var buffer = new byte[sizeof(int)];
            if (!TryReadExactly(stream, buffer, buffer.Length))
            {
                value = 0;
                return false;
            }

            value = BitConverter.ToInt32(buffer, 0);
            return true;
        }

        private static bool TryReadExactly(Stream stream, byte[] buffer, int count)
        {
            var offset = 0;
            while (offset < count)
            {
                var read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                {
                    return false;
                }

                offset += read;
            }

            return true;
        }

        private static InstanceNames BuildInstanceNames()
        {
            string userIdentity;
            using (var identity = WindowsIdentity.GetCurrent())
            {
                userIdentity = identity != null && identity.User != null
                    ? identity.User.Value
                    : Environment.UserName;
            }

            var discriminator = string.Format(
                CultureInfo.InvariantCulture,
                "{0}|{1}",
                userIdentity,
                Process.GetCurrentProcess().SessionId);
            var suffix = ComputeStableSuffix(discriminator);

            return new InstanceNames(
                @"Local\PDFLigero.Viewer.Persistent.v1." + suffix + ".Mutex",
                "PDFLigero.Viewer.Persistent.v1." + suffix + ".Pipe");
        }

        private static string ComputeStableSuffix(string value)
        {
            using (var algorithm = SHA256.Create())
            {
                var hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(value));
                var result = new StringBuilder(24);
                for (var index = 0; index < 12; index++)
                {
                    result.Append(hash[index].ToString("x2", CultureInfo.InvariantCulture));
                }

                return result.ToString();
            }
        }

        private sealed class InstanceNames
        {
            public InstanceNames(string markerName, string primaryPipeName)
            {
                MarkerName = markerName;
                PipeName = primaryPipeName;
            }

            public string MarkerName { get; private set; }

            public string PipeName { get; private set; }
        }
    }
}
