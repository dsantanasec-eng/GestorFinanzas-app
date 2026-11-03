using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;

namespace GestorFinanzas
{
    public static class RecordatorioService
    {
        private const int NOTIF_ID_PAGO = 1001;
        private const int NOTIF_ID_AHORRO = 2001;

        private const string KEY_PAGO_ACTIVO = "recordatorio_pago_activo";
        private const string KEY_PAGO_FECHA = "recordatorio_pago_fecha";
        private const string KEY_PAGO_FRECUENCIA = "recordatorio_pago_frecuencia";
        private const string KEY_PAGO_HORA = "recordatorio_pago_hora";

        private const string KEY_AHORRO_ACTIVO = "recordatorio_ahorro_activo";
        private const string KEY_AHORRO_HORA = "recordatorio_ahorro_hora";

        public static async Task<bool> SolicitarPermisoAsync()
        {
            var habilitado = await LocalNotificationCenter.Current.AreNotificationsEnabled();
            if (!habilitado)
            {
                habilitado = await LocalNotificationCenter.Current.RequestNotificationPermission();
            }
            return habilitado;
        }

        // ---------- RECORDATORIO DE PAGO ----------

        public static async Task GuardarYProgramarPagoAsync(DateTime fecha, string frecuencia, TimeSpan hora, bool activo)
        {
            Preferences.Set(KEY_PAGO_ACTIVO, activo);
            Preferences.Set(KEY_PAGO_FECHA, fecha.ToString("yyyy-MM-dd"));
            Preferences.Set(KEY_PAGO_FRECUENCIA, frecuencia);
            Preferences.Set(KEY_PAGO_HORA, hora.ToString());

            if (activo)
            {
                var permisoOk = await SolicitarPermisoAsync();
                if (!permisoOk) return;

                await ProgramarPagoInternoAsync(fecha, frecuencia, hora);
            }
            else
            {
                await CancelarPagoAsync();
            }
        }

        private static async Task ProgramarPagoInternoAsync(DateTime fechaBase, string frecuencia, TimeSpan hora)
        {
            var proximaFecha = CalcularProximaFecha(fechaBase, frecuencia, hora);

            var notification = new NotificationRequest
            {
                NotificationId = NOTIF_ID_PAGO,
                Title = "💰 Día de pago",
                Description = "Hoy es tu día de pago. No olvides registrar tu ingreso en GestorFinanzas.",
                Schedule = new NotificationRequestSchedule
                {
                    NotifyTime = proximaFecha
                }
            };

            await LocalNotificationCenter.Current.Show(notification);
        }

        public static Task CancelarPagoAsync()
        {
            LocalNotificationCenter.Current.Cancel(NOTIF_ID_PAGO);
            return Task.CompletedTask;
        }

        // ---------- RECORDATORIO DIARIO DE AHORRO ----------

        public static async Task GuardarYProgramarAhorroAsync(TimeSpan hora, bool activo)
        {
            Preferences.Set(KEY_AHORRO_ACTIVO, activo);
            Preferences.Set(KEY_AHORRO_HORA, hora.ToString());

            if (activo)
            {
                var permisoOk = await SolicitarPermisoAsync();
                if (!permisoOk) return;

                var proxima = DateTime.Today.Add(hora);
                if (proxima <= DateTime.Now)
                {
                    proxima = proxima.AddDays(1);
                }

                var notification = new NotificationRequest
                {
                    NotificationId = NOTIF_ID_AHORRO,
                    Title = "🎯 ¿Ahorraste hoy?",
                    Description = "Confirma en tu Plan de Ahorro si guardaste algo hoy y sigue tu progreso.",
                    Schedule = new NotificationRequestSchedule
                    {
                        NotifyTime = proxima,
                        RepeatType = NotificationRepeat.Daily
                    }
                };

                await LocalNotificationCenter.Current.Show(notification);
            }
            else
            {
                await CancelarAhorroAsync();
            }
        }

        public static Task CancelarAhorroAsync()
        {
            LocalNotificationCenter.Current.Cancel(NOTIF_ID_AHORRO);
            return Task.CompletedTask;
        }

        // ---------- CÁLCULO DE PRÓXIMA FECHA ----------

        private static DateTime CalcularProximaFecha(DateTime fechaBase, string frecuencia, TimeSpan hora)
        {
            var fecha = fechaBase.Date.Add(hora);
            var ahora = DateTime.Now;

            while (fecha <= ahora)
            {
                fecha = frecuencia switch
                {
                    "Semanal" => fecha.AddDays(7),
                    "Quincenal" => fecha.AddDays(14),
                    "Mensual" => fecha.AddMonths(1),
                    _ => fecha.AddDays(7)
                };
            }

            return fecha;
        }

        // ---------- REPROGRAMAR AL ABRIR LA APP ----------

        public static async Task ReprogramarTodoAsync()
        {
            if (PagoActivo)
            {
                await ProgramarPagoInternoAsync(PagoFecha, PagoFrecuencia, PagoHora);
            }

            if (AhorroActivo)
            {
                await GuardarYProgramarAhorroAsync(AhorroHora, true);
            }
        }

        // ---------- LECTURA PARA LA PANTALLA DE AJUSTES ----------

        public static bool PagoActivo => Preferences.Get(KEY_PAGO_ACTIVO, false);
        public static DateTime PagoFecha => DateTime.TryParse(Preferences.Get(KEY_PAGO_FECHA, string.Empty), out var f) ? f : DateTime.Today;
        public static string PagoFrecuencia => Preferences.Get(KEY_PAGO_FRECUENCIA, "Mensual");
        public static TimeSpan PagoHora => TimeSpan.TryParse(Preferences.Get(KEY_PAGO_HORA, "09:00:00"), out var h) ? h : new TimeSpan(9, 0, 0);

        public static bool AhorroActivo => Preferences.Get(KEY_AHORRO_ACTIVO, false);
        public static TimeSpan AhorroHora => TimeSpan.TryParse(Preferences.Get(KEY_AHORRO_HORA, "20:00:00"), out var h) ? h : new TimeSpan(20, 0, 0);
    }
}