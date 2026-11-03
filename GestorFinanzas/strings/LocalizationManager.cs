using System.Globalization;
using System.Linq;
using Microsoft.Maui.Storage;

namespace GestorFinanzas.Strings
{
    // Controla qué idioma usa la app: "es", "en", o "" (automático = sigue el idioma del teléfono).
    public static class LocalizationManager
    {
        private const string IdiomaPrefKey = "IdiomaSeleccionado";

        // Códigos de idioma que la app realmente soporta.
        private static readonly string[] IdiomasSoportados = { "es", "en" };

        // Se dispara cada vez que el usuario cambia el idioma manualmente,
        // para que las pantallas que usan Tr (bindings) se refresquen solas.
        public static event Action? IdiomaCambiado;

        // Cultura que está usando la app ahora mismo (ya resuelta: nunca está vacía).
        public static CultureInfo ActualCulture { get; private set; } = new CultureInfo("es");

        // Lo que el usuario eligió guardar: "es", "en", o "" si eligió Automático.
        public static string IdiomaGuardado => Preferences.Default.Get(IdiomaPrefKey, "");

        // Llamar UNA sola vez, al arrancar la app (en MauiProgram.cs, justo después
        // de "var builder = MauiApp.CreateBuilder();").
        public static void Inicializar()
        {
            AplicarIdioma(IdiomaGuardado);
        }

        // idioma: "es", "en", o "" para volver a Automático (idioma del teléfono).
        public static void CambiarIdioma(string idioma)
        {
            Preferences.Default.Set(IdiomaPrefKey, idioma);
            AplicarIdioma(idioma);
            IdiomaCambiado?.Invoke();
        }

        private static void AplicarIdioma(string idiomaGuardado)
        {
            string codigo;

            if (!string.IsNullOrEmpty(idiomaGuardado) && IdiomasSoportados.Contains(idiomaGuardado))
            {
                codigo = idiomaGuardado;
            }
            else
            {
                // Automático: usa el idioma del teléfono si lo soportamos; si no, español.
                string codigoTelefono = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                codigo = IdiomasSoportados.Contains(codigoTelefono) ? codigoTelefono : "es";
            }

            var cultura = new CultureInfo(codigo);
            ActualCulture = cultura;

            CultureInfo.CurrentUICulture = cultura;
            CultureInfo.CurrentCulture = cultura;
            CultureInfo.DefaultThreadCurrentUICulture = cultura;
            CultureInfo.DefaultThreadCurrentCulture = cultura;
        }
    }
}