using System;
using System.Windows.Forms;

namespace FirmaAutomatica
{
    /// <summary>
    /// Impone el cursor de una herramienta sobre la pagina del visor.
    ///
    /// Hace falta porque PdfiumViewer decide su propio cursor —la mano de
    /// desplazar la pagina— al atender WM_SETCURSOR, y ese mensaje se ENVIA
    /// directamente a la ventana en vez de encolarse. Los filtros de mensajes de
    /// la aplicacion (IMessageFilter) solo ven los que pasan por la cola, asi que
    /// desde alli no hay forma de adelantarse: el cursor cambiaba sobre las
    /// barras de herramientas propias, pero volvia a ser la mano en cuanto el
    /// raton entraba en la pagina.
    ///
    /// Enganchandose al procedimiento de ventana del visor si se ve el mensaje y
    /// se puede responder antes que el.
    /// </summary>
    internal sealed class PdfRendererCursorOverride : NativeWindow, IDisposable
    {
        private const int WmSetCursor = 0x0020;

        private readonly Control target;
        private readonly Func<Cursor> resolveCursor;
        private bool disposed;

        /// <param name="resolveCursor">
        /// Devuelve el cursor que toca, o null para dejar que el visor ponga el
        /// suyo de siempre.
        /// </param>
        public PdfRendererCursorOverride(
            Control target,
            Func<Cursor> resolveCursor)
        {
            if (target == null)
            {
                throw new ArgumentNullException("target");
            }
            if (resolveCursor == null)
            {
                throw new ArgumentNullException("resolveCursor");
            }

            this.target = target;
            this.resolveCursor = resolveCursor;

            if (target.IsHandleCreated)
            {
                AssignHandle(target.Handle);
            }

            target.HandleCreated += Target_HandleCreated;
            target.HandleDestroyed += Target_HandleDestroyed;
        }

        protected override void WndProc(ref Message m)
        {
            if (!disposed && m.Msg == WmSetCursor)
            {
                Cursor cursor = null;
                try
                {
                    cursor = resolveCursor();
                }
                catch (Exception)
                {
                    cursor = null;
                }

                if (cursor != null)
                {
                    Cursor.Current = cursor;

                    // Devolver 1 le dice a Windows que el cursor ya esta puesto,
                    // de modo que el visor no lo cambia despues.
                    m.Result = new IntPtr(1);
                    return;
                }
            }

            base.WndProc(ref m);
        }

        private void Target_HandleCreated(object sender, EventArgs e)
        {
            if (!disposed)
            {
                AssignHandle(target.Handle);
            }
        }

        private void Target_HandleDestroyed(object sender, EventArgs e)
        {
            ReleaseHandle();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            target.HandleCreated -= Target_HandleCreated;
            target.HandleDestroyed -= Target_HandleDestroyed;

            try
            {
                ReleaseHandle();
            }
            catch (Exception)
            {
            }
        }
    }
}
