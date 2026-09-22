using System;
using System.Collections;
using System.Reflection;
using PdfiumViewer;

namespace FirmaAutomatica
{
    /// <summary>
    /// Tira la imagen que PdfRenderer guarda de una pagina para que la vuelva
    /// a pedir.
    ///
    /// POR QUE HACE FALTA. El visor guarda la imagen de cada pagina y no la
    /// vuelve a pedir mientras no cambie el zoom. Eso es bueno —un repintado
    /// normal no cuesta nada— pero impide lo que hace cualquier visor rapido:
    /// pintar al instante una version reescalada y sustituirla por la buena
    /// cuando el hilo de atras la tiene lista. Sin poder tirar esa copia, la
    /// version blanda se quedaria en pantalla hasta el siguiente zoom.
    ///
    /// POR QUE SE HACE ASI. PdfiumViewer 2.13 esta congelado en este proyecto
    /// —actualizarlo exige bifurcar la libreria, y el analisis esta hecho en
    /// CONTEXTO_PDF_LIGERO.md—, y no expone nada publico para esto. Se toca
    /// un unico campo privado, `_pageCache`, y solo para poner a null la
    /// imagen de una pagina: no se llama a ningun metodo interno ni se
    /// cambia el estado del control.
    ///
    /// SI ALGUN DIA NO ESTA, no pasa nada: se detecta una vez, se anota, y el
    /// visor se comporta como antes de esto. Por eso todo el camino esta
    /// pensado para funcionar aunque esto devuelva false.
    /// </summary>
    internal static class PdfRendererCacheAccess
    {
        private static readonly object initLock = new object();
        private static bool inspected;
        private static FieldInfo pageCacheField;
        private static PropertyInfo imageProperty;

        /// <summary>
        /// Esta disponible el atajo. Falso significa que el visor seguira
        /// funcionando, pero sin refinado progresivo.
        /// </summary>
        public static bool IsAvailable
        {
            get
            {
                Inspect();
                return pageCacheField != null;
            }
        }

        /// <summary>
        /// Olvida la imagen guardada de una pagina. Devuelve true si se ha
        /// podido.
        /// </summary>
        public static bool ForgetPageImage(PdfRenderer renderer, int page)
        {
            if (renderer == null || page < 0)
            {
                return false;
            }

            Inspect();
            if (pageCacheField == null)
            {
                return false;
            }

            try
            {
                var lista = pageCacheField.GetValue(renderer) as IList;
                if (lista == null || page >= lista.Count)
                {
                    return false;
                }

                var entrada = lista[page];
                if (entrada == null)
                {
                    return false;
                }

                if (imageProperty == null)
                {
                    imageProperty = entrada.GetType().GetProperty(
                        "Image",
                        BindingFlags.Public |
                        BindingFlags.NonPublic |
                        BindingFlags.Instance);
                }

                if (imageProperty == null || !imageProperty.CanWrite)
                {
                    return false;
                }

                imageProperty.SetValue(entrada, null, null);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Write(
                    "No se pudo refrescar la página en el visor: " + ex);
                return false;
            }
        }

        private static void Inspect()
        {
            if (inspected)
            {
                return;
            }

            lock (initLock)
            {
                if (inspected)
                {
                    return;
                }

                inspected = true;
                try
                {
                    pageCacheField = typeof(PdfRenderer).GetField(
                        "_pageCache",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (pageCacheField == null)
                    {
                        AppLog.Write(
                            "El visor no expone su caché de páginas: se " +
                            "seguirá sin refinado progresivo.");
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Write(
                        "No se pudo inspeccionar el visor: " + ex);
                    pageCacheField = null;
                }
            }
        }
    }
}
