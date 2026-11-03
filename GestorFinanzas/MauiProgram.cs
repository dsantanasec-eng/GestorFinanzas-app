using Microcharts.Maui;
using Microsoft.Extensions.Logging;
using Plugin.AdMob;
using Plugin.AdMob.Configuration;
using Plugin.LocalNotification;

namespace GestorFinanzas
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();

            GestorFinanzas.Strings.LocalizationManager.Inicializar();

            builder
                .UseMauiApp<App>()
                .UseMicrocharts()
                .UseLocalNotification()
                .UseAdMob()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
            builder.Logging.AddDebug();
            AdConfig.UseTestAdUnitIds = true;
#endif

            return builder.Build();
        }
    }
}