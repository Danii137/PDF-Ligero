using System.Drawing;
using System.Windows.Forms;

namespace FirmaAutomatica
{
    internal static class AppBranding
    {
        public const string ApplicationName = "PDF Ligero";

        public static void ApplyWindowIcon(Form form)
        {
            if (form == null)
            {
                return;
            }

            try
            {
                using (var executableIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                {
                    if (executableIcon != null)
                    {
                        form.Icon = (Icon)executableIcon.Clone();
                    }
                }
            }
            catch
            {
                // Windows seguirá usando el icono incrustado del ejecutable.
            }
        }
    }
}
