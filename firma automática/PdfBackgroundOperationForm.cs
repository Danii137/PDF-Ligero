using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace FirmaAutomatica
{
    internal sealed class PdfBackgroundOperationForm : Form
    {
        private readonly Action operation;
        private readonly ManualResetEvent operationCompleted =
            new ManualResetEvent(false);
        private Exception operationError;
        private int operationStarted;
        private volatile bool operationFinished;

        public PdfBackgroundOperationForm(
            string title,
            string message,
            Action operation)
        {
            if (operation == null)
            {
                throw new ArgumentNullException("operation");
            }

            this.operation = operation;
            Text = title;
            AppBranding.ApplyWindowIcon(this);
            Width = 440;
            Height = 142;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(250, 249, 247);
            Font = SystemFonts.MessageBoxFont;

            var accentLine = new Panel
            {
                Left = 18,
                Top = 18,
                Width = 34,
                Height = 2,
                BackColor = Color.FromArgb(238, 91, 61)
            };
            var messageLabel = new Label
            {
                Left = 18,
                Top = 28,
                Width = 390,
                Height = 30,
                Text = message,
                ForeColor = Color.FromArgb(31, 31, 29),
                AutoEllipsis = true
            };
            var progressBar = new ProgressBar
            {
                Left = 18,
                Top = 68,
                Width = 390,
                Height = 12,
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 24
            };

            Controls.Add(accentLine);
            Controls.Add(messageLabel);
            Controls.Add(progressBar);
            Shown += PdfBackgroundOperationForm_Shown;
        }

        public void Run(IWin32Window owner)
        {
            ShowDialog(owner);
            if (Interlocked.CompareExchange(
                    ref operationStarted,
                    0,
                    0) == 0)
            {
                throw new InvalidOperationException(
                    "La operación en segundo plano no llegó a iniciarse.");
            }

            // Normally the worker closes the dialog. If an owner or Windows
            // closes it first, never report a half-finished copy as successful.
            operationCompleted.WaitOne();
            if (operationError != null)
            {
                throw new InvalidOperationException(
                    operationError.GetBaseException().Message,
                    operationError);
            }
        }

        private void PdfBackgroundOperationForm_Shown(
            object sender,
            EventArgs e)
        {
            if (Interlocked.Exchange(ref operationStarted, 1) != 0)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(
                delegate
                {
                    try
                    {
                        operation();
                    }
                    catch (Exception ex)
                    {
                        operationError = ex;
                    }
                    finally
                    {
                        operationFinished = true;
                        operationCompleted.Set();
                        try
                        {
                            BeginInvoke(new Action(Close));
                        }
                        catch
                        {
                        }
                    }
                });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!operationFinished &&
                e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                return;
            }

            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                operationCompleted.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
