using GestorFinanzas.Strings;

namespace GestorFinanzas
{
    public partial class AjustesPage : ContentPage
    {
        private bool _actualizandoIdioma = false;

        public AjustesPage()
        {
            InitializeComponent();
            CargarOpcionesDeIdioma();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            SwitchPago.IsToggled = RecordatorioService.PagoActivo;
            DatePickerPago.Date = RecordatorioService.PagoFecha;
            PickerFrecuencia.SelectedItem = RecordatorioService.PagoFrecuencia;
            TimePickerPago.Time = RecordatorioService.PagoHora;

            SwitchAhorro.IsToggled = RecordatorioService.AhorroActivo;
            TimePickerAhorro.Time = RecordatorioService.AhorroHora;
        }

        private async void BotonGuardar_Clicked(object sender, EventArgs e)
        {
            var frecuencia = PickerFrecuencia.SelectedItem as string ?? "Mensual";

            var fechaPago = DatePickerPago.Date ?? DateTime.Today;
            var horaPago = TimePickerPago.Time ?? new TimeSpan(9, 0, 0);
            var horaAhorro = TimePickerAhorro.Time ?? new TimeSpan(20, 0, 0);

            await RecordatorioService.GuardarYProgramarPagoAsync(
                fechaPago,
                frecuencia,
                horaPago,
                SwitchPago.IsToggled);

            await RecordatorioService.GuardarYProgramarAhorroAsync(
                horaAhorro,
                SwitchAhorro.IsToggled);

            await DisplayAlert(AppStrings.Get("Ajustes_GuardadoTitulo"), AppStrings.Get("Ajustes_GuardadoDesc"), "OK");
        }

        // ---------- Idioma ----------

        private void CargarOpcionesDeIdioma()
        {
            _actualizandoIdioma = true;

            PickerIdioma.ItemsSource = new List<string>
            {
                AppStrings.Get("Ajustes_IdiomaAutomatico"),
                "Español",
                "English"
            };

            string guardado = LocalizationManager.IdiomaGuardado; // "", "es", "en"
            PickerIdioma.SelectedIndex = guardado switch
            {
                "es" => 1,
                "en" => 2,
                _ => 0
            };

            _actualizandoIdioma = false;
        }

        private async void PickerIdioma_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_actualizandoIdioma) return;

            string idioma = PickerIdioma.SelectedIndex switch
            {
                1 => "es",
                2 => "en",
                _ => ""
            };

            LocalizationManager.CambiarIdioma(idioma);

            await DisplayAlert("", AppStrings.Get("Ajustes_IdiomaCambiadoAviso"), "OK");
        }
    }
}