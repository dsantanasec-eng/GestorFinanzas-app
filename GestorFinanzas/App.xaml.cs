using Microsoft.Extensions.DependencyInjection;
using Plugin.LocalNotification;
using Plugin.LocalNotification.EventArgs;
using Plugin.Firebase.Auth;

namespace GestorFinanzas
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            LocalNotificationCenter.Current.NotificationActionTapped += OnNotificationActionTapped;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }

        protected override async void OnStart()
        {
            base.OnStart();

            try
            {
                await RecordatorioService.ReprogramarTodoAsync();
            }
            catch
            {
                // Si falla la reprogramación al iniciar, no debe tumbar la app.
            }
        }

        private void OnNotificationActionTapped(NotificationActionEventArgs e)
        {
            if (e?.Request?.NotificationId != 2001) return; // solo nos interesa el recordatorio de ahorro

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await ManejarTapRecordatorioAhorroAsync();
            });
        }

        private async Task ManejarTapRecordatorioAhorroAsync()
        {
            try
            {
                var usuario = CrossFirebaseAuth.Current.CurrentUser;
                if (usuario == null) return; // no hay sesión activa

                var plan = await AhorroService.ObtenerPlanActivoAsync(usuario.Uid);
                if (plan == null) return; // no tiene meta activa, nada que registrar

                var paginaActual = Shell.Current?.CurrentPage;
                if (paginaActual == null) return;

                string simboloMoneda = Movimiento.SimbolosMoneda.TryGetValue(plan.Moneda, out var s) ? s : plan.Moneda;

                string montoTexto = await paginaActual.DisplayPromptAsync(
                    "¿Cuánto ahorraste hoy?",
                    $"Monto en {simboloMoneda} que apartaste para '{plan.Nombre}'",
                    initialValue: "",
                    keyboard: Keyboard.Numeric);

                if (!decimal.TryParse(montoTexto, out decimal monto) || monto <= 0)
                    return;

                bool exito = await AhorroService.RegistrarAporteAsync(monto, plan.Moneda, plan.Nombre, usuario.Uid);

                if (exito)
                {
                    await paginaActual.DisplayAlert("¡Listo!", $"Se registró tu aporte de {simboloMoneda} {monto:N2}.", "OK");
                }
            }
            catch
            {
                // Un fallo aquí no debe tumbar la app.
            }
        }
    }
}