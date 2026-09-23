using System;
using System.Drawing;
using System.Windows.Forms;
using PdfiumViewer;

namespace FirmaAutomatica
{
    /// <summary>
    /// La navegacion que se espera de un visor de PDF.
    ///
    /// Ctrl+rueda acerca y aleja dejando quieto el punto que hay bajo el
    /// raton, que es lo que hacen todos los visores y lo que permite acercarse
    /// a un detalle de un plano sin perderlo de vista. Mayus+rueda desplaza en
    /// horizontal, util cuando un A0 no cabe de ancho.
    ///
    /// El resto son los atajos de siempre, los mismos del navegador y de
    /// Acrobat, para no tener que aprenderse unos propios.
    /// </summary>
    internal sealed class PdfViewNavigationController
        : IMessageFilter, IDisposable
    {
        private const int WmMouseWheel = 0x020A;
        private const int WmKeyDown = 0x0100;

        private readonly PdfRenderer renderer;
        private readonly Func<bool> canNavigate;
        private readonly Action zoomChanged;
        private readonly Action fitPage;
        private readonly Action fitWidth;
        private readonly Action actualSize;

        private bool disposed;

        /// <param name="fitPage">
        /// Ajustar la pagina entera. Lo hace la ventana y no este controlador
        /// porque el modo de ajuste vive en el PdfViewer, no en el renderer:
        /// ponerlo directamente en el renderer dejaba el aumento a medias.
        /// </param>
        public PdfViewNavigationController(
            PdfRenderer renderer,
            Func<bool> canNavigate,
            Action zoomChanged,
            Action fitPage,
            Action fitWidth,
            Action actualSize)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException("renderer");
            }

            this.renderer = renderer;
            this.canNavigate = canNavigate;
            this.zoomChanged = zoomChanged;
            this.fitPage = fitPage;
            this.fitWidth = fitWidth;
            this.actualSize = actualSize;

            renderer.Disposed += Renderer_Disposed;
            Application.AddMessageFilter(this);
        }

        public bool PreFilterMessage(ref Message message)
        {
            if (disposed || !Allowed())
            {
                return false;
            }

            if (message.Msg == WmMouseWheel)
            {
                return HandleWheel(ref message);
            }

            if (message.Msg == WmKeyDown)
            {
                return HandleKey((Keys)(int)message.WParam);
            }

            return false;
        }

        private bool HandleWheel(ref Message message)
        {
            // La rueda va a la ventana que tiene el foco, no a la que esta
            // debajo del raton. El propio mensaje trae donde estaba el
            // puntero, en coordenadas de pantalla, en el momento de girar la
            // rueda: se usa eso y no Cursor.Position, que puede ir con retraso
            // si el raton sigue moviendose.
            try
            {
                var lParam = (long)message.LParam;
                var enPantalla = new Point(
                    (short)(lParam & 0xFFFF),
                    (short)((lParam >> 16) & 0xFFFF));
                var enVisor = renderer.PointToClient(enPantalla);
                if (!renderer.ClientRectangle.Contains(enVisor))
                {
                    return false;
                }

                // Ctrl viene tambien en el mensaje (MK_CONTROL). Se acepta
                // por cualquiera de las dos vias.
                var wParam = (long)message.WParam;
                var control = (wParam & 0x0008) != 0 ||
                    (Control.ModifierKeys & Keys.Control) == Keys.Control;
                if (!control)
                {
                    return false;
                }

                var delta = (short)((wParam >> 16) & 0xFFFF);
                if (delta == 0)
                {
                    return true;
                }

                PdfZoomAnchor.Step(renderer, enVisor, delta > 0);
                Notify();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool HandleKey(Keys tecla)
        {
            if (!renderer.Focused && !RendererHasFocusedChild())
            {
                return false;
            }

            var control = (Control.ModifierKeys & Keys.Control) == Keys.Control;

            if (control)
            {
                switch (tecla)
                {
                    case Keys.D0:
                    case Keys.NumPad0:
                        Run(fitPage);
                        return true;

                    case Keys.D1:
                    case Keys.NumPad1:
                        Run(actualSize);
                        return true;

                    case Keys.D2:
                    case Keys.NumPad2:
                        Run(fitWidth);
                        return true;

                    case Keys.Add:
                    case Keys.Oemplus:
                        PdfZoomAnchor.Step(
                            renderer,
                            PdfZoomAnchor.CenterOf(renderer),
                            true);
                        Notify();
                        return true;

                    case Keys.Subtract:
                    case Keys.OemMinus:
                        PdfZoomAnchor.Step(
                            renderer,
                            PdfZoomAnchor.CenterOf(renderer),
                            false);
                        Notify();
                        return true;

                    case Keys.Home:
                        renderer.Page = 0;
                        return true;

                    case Keys.End:
                        if (renderer.Document != null)
                        {
                            renderer.Page = renderer.Document.PageCount - 1;
                        }

                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// El foco puede estar en un hijo del visor —los botones flotantes de
        /// las herramientas—, y ahi los atajos tambien valen.
        /// </summary>
        private bool RendererHasFocusedChild()
        {
            try
            {
                var activo = Form.ActiveForm;
                if (activo == null)
                {
                    return false;
                }

                var control = activo.ActiveControl;
                while (control != null)
                {
                    if (ReferenceEquals(control, renderer))
                    {
                        return true;
                    }

                    control = control.Parent;
                }
            }
            catch (Exception)
            {
            }

            return false;
        }

        private bool Allowed()
        {
            if (renderer.IsDisposed ||
                !renderer.Visible ||
                renderer.Document == null)
            {
                return false;
            }

            try
            {
                return canNavigate == null || canNavigate();
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void Run(Action accion)
        {
            if (accion == null)
            {
                return;
            }

            try
            {
                accion();
            }
            catch (Exception ex)
            {
                AppLog.Write("No se pudo cambiar el ajuste de zoom: " + ex);
            }
        }

        private void Notify()
        {
            if (zoomChanged == null)
            {
                return;
            }

            try
            {
                zoomChanged();
            }
            catch (Exception ex)
            {
                AppLog.Write("No se pudo anotar el zoom: " + ex);
            }
        }

        private void Renderer_Disposed(object sender, EventArgs e)
        {
            Dispose();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Application.RemoveMessageFilter(this);
            try
            {
                renderer.Disposed -= Renderer_Disposed;
            }
            catch (Exception)
            {
            }
        }
    }
}
