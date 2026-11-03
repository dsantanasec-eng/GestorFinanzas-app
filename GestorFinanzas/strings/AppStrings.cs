using System.Reflection;
using System.Resources;

namespace GestorFinanzas.Strings
{
    // Lee los textos desde AppStrings.resx (español) / AppStrings.en.resx (inglés)
    // según el idioma activo en LocalizationManager.
    public static class AppStrings
    {
        // OJO: si tu proyecto NO se llama "GestorFinanzas" en el .csproj (RootNamespace),
        // cambia el texto "GestorFinanzas.Strings.AppStrings" de la línea de abajo por
        // "<TuNamespace>.Strings.AppStrings".
        private static readonly ResourceManager _rm =
            new ResourceManager("GestorFinanzas.Strings.AppStrings", typeof(AppStrings).GetTypeInfo().Assembly);

        // Devuelve el texto tal cual, en el idioma activo.
        public static string Get(string clave)
        {
            try
            {
                return _rm.GetString(clave, LocalizationManager.ActualCulture) ?? clave;
            }
            catch
            {
                return clave;
            }
        }

        // Devuelve el texto con los {0}, {1}, etc. ya reemplazados.
        public static string Get(string clave, params object[] argumentos)
        {
            string plantilla = Get(clave);
            try
            {
                return string.Format(LocalizationManager.ActualCulture, plantilla, argumentos);
            }
            catch
            {
                return plantilla;
            }
        }
    }
}