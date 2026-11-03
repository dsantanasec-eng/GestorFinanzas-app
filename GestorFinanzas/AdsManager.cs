using Microsoft.Extensions.DependencyInjection;
using Plugin.AdMob;
using Plugin.AdMob.Services;

namespace GestorFinanzas
{
    // Centraliza el anuncio intersticial (pantalla completa) para que no se repita
    // la misma lógica en cada pantalla. Se encarga de:
    //  - precargarlo con tiempo (cargar un anuncio tarda unos segundos)
    //  - contar las acciones del usuario y mostrarlo solo cada tantas veces,
    //    para que no sea molesto
    //  - volver a precargar el siguiente en cuanto el usuario cierra uno
    public static class AdsManager
    {
        // Ad Unit ID real del anuncio INTERSTICIAL (distinto al del banner).
        //
        // En modo DEBUG, AdConfig.UseTestAdUnitIds ya está activado en
        // MauiProgram.cs, así que el plugin usa automáticamente un anuncio
        // de PRUEBA de Google mientras pruebas, aunque este ID ya sea el real.
        // Este ID real es el que se usará cuando publiques la app (modo Release).
        private const string InterstitialAdUnitId = "ca-app-pub-4227427045418783/3300503567";

        private const string ContadorPrefKey = "ContadorParaIntersticial";

        // Cada cuántas acciones del usuario (agregar/editar movimiento, registrar
        // aporte de ahorro) se muestra el anuncio. 4 = se ve 1 de cada 4 veces.
        private const int AccionesEntreAnuncios = 4;

        private static IInterstitialAdService? _servicio;
        private static IInterstitialAd? _anuncioActual;

        private static IInterstitialAdService Servicio =>
            _servicio ??= IPlatformApplication.Current!.Services.GetRequiredService<IInterstitialAdService>();

        private static string? AdUnitIdOCero =>
            InterstitialAdUnitId == "TU_AD_UNIT_ID_INTERSTICIAL" ? null : InterstitialAdUnitId;

        // Llama esto al entrar a la pantalla principal, para que el anuncio ya
        // esté listo cuando de verdad toque mostrarlo (cargarlo toma unos segundos).
        // Es seguro llamarlo varias veces: si ya hay uno cargado o cargándose, no hace nada.
        public static void Precargar()
        {
            if (_anuncioActual != null) return;

            try
            {
                _anuncioActual = Servicio.CreateAd(AdUnitIdOCero);

                _anuncioActual.OnAdDismissed += (s, e) =>
                {
                    // en cuanto el usuario cierra el anuncio, se prepara el siguiente
                    _anuncioActual = null;
                    Precargar();
                };
                _anuncioActual.OnAdFailedToLoad += (s, e) =>
                {
                    _anuncioActual = null; // se reintentará la próxima vez que se llame a Precargar()
                };
                _anuncioActual.OnAdFailedToShow += (s, e) =>
                {
                    _anuncioActual = null;
                };

                _anuncioActual.Load();
            }
            catch
            {
                // si falla precargar (sin internet, etc.), simplemente no se muestra el anuncio esta vez
                _anuncioActual = null;
            }
        }

        // Llama esto cada vez que el usuario complete una acción importante
        // (guardar un movimiento, registrar un aporte de ahorro). Solo muestra
        // el anuncio 1 de cada "AccionesEntreAnuncios" veces.
        public static void RegistrarAccionYMostrarSiToca()
        {
            try
            {
                int contador = Preferences.Default.Get(ContadorPrefKey, 0) + 1;

                if (contador >= AccionesEntreAnuncios)
                {
                    contador = 0;

                    if (_anuncioActual != null && _anuncioActual.IsLoaded)
                    {
                        _anuncioActual.Show();
                    }
                    else
                    {
                        // no estaba listo todavía: no se muestra esta vez, pero se prepara
                        // para que sí esté listo más adelante
                        Precargar();
                    }
                }

                Preferences.Default.Set(ContadorPrefKey, contador);
            }
            catch
            {
                // un anuncio que falla nunca debe tumbar la acción real del usuario (guardar su movimiento)
            }
        }
    }
}