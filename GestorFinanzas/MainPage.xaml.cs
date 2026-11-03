using Plugin.Firebase.Auth;
using GestorFinanzas.Strings;

namespace GestorFinanzas
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // Si ya hay una sesión activa (guardada por Firebase en el dispositivo),
            // saltamos directo a la pantalla principal sin pedir login de nuevo.
            var usuario = CrossFirebaseAuth.Current.CurrentUser;
            if (usuario != null)
            {
                await Shell.Current.GoToAsync("//MovimientosPage");
            }
        }

        private async void OnIniciarSesionClicked(object? sender, EventArgs e)
        {
            LblMensaje.IsVisible = false;

            var correo = EntryCorreo.Text?.Trim();
            var contrasena = EntryContrasena.Text;

            if (string.IsNullOrWhiteSpace(correo) || string.IsNullOrWhiteSpace(contrasena))
            {
                MostrarError(AppStrings.Get("Login_CompletaCorreoContrasena"));
                return;
            }

            try
            {
                BtnIniciarSesion.IsEnabled = false;
                await CrossFirebaseAuth.Current.SignInWithEmailAndPasswordAsync(correo, contrasena);

                // Login exitoso: navegamos a la pantalla principal
                await Shell.Current.GoToAsync("//MovimientosPage");
            }
            catch (Exception ex)
            {
                MostrarError(AppStrings.Get("Login_ErrorIniciarSesion", ex.Message));
            }
            finally
            {
                BtnIniciarSesion.IsEnabled = true;
            }
        }

        private async void OnRegistrarseClicked(object? sender, EventArgs e)
        {
            LblMensaje.IsVisible = false;

            var correo = EntryCorreo.Text?.Trim();
            var contrasena = EntryContrasena.Text;

            if (string.IsNullOrWhiteSpace(correo) || string.IsNullOrWhiteSpace(contrasena))
            {
                MostrarError(AppStrings.Get("Login_CompletaParaRegistrarte"));
                return;
            }

            if (contrasena.Length < 6)
            {
                MostrarError(AppStrings.Get("Login_ContrasenaCorta"));
                return;
            }

            try
            {
                BtnRegistrarse.IsEnabled = false;
                await CrossFirebaseAuth.Current.CreateUserAsync(correo, contrasena);

                await Shell.Current.GoToAsync("//MovimientosPage");
            }
            catch (Exception ex)
            {
                MostrarError(AppStrings.Get("Login_ErrorCrearCuenta", ex.Message));
            }
            finally
            {
                BtnRegistrarse.IsEnabled = true;
            }
        }

        private async void OnOlvideContrasenaClicked(object? sender, EventArgs e)
        {
            LblMensaje.IsVisible = false;

            // Usamos el correo que ya haya escrito, o se lo pedimos si está vacío.
            var correo = EntryCorreo.Text?.Trim();

            if (string.IsNullOrWhiteSpace(correo))
            {
                correo = await DisplayPromptAsync(
                    AppStrings.Get("Login_RecuperarTitulo"),
                    AppStrings.Get("Login_RecuperarDesc"),
                    accept: AppStrings.Get("Login_Enviar"),
                    cancel: AppStrings.Get("Comun_Cancelar"),
                    keyboard: Keyboard.Email);
            }

            if (string.IsNullOrWhiteSpace(correo))
            {
                // El usuario canceló el diálogo o no escribió nada.
                return;
            }

            try
            {
                BtnOlvideContrasena.IsEnabled = false;
                await CrossFirebaseAuth.Current.SendPasswordResetEmailAsync(correo);

                await DisplayAlert(
                    AppStrings.Get("Login_RevisaCorreoTitulo"),
                    AppStrings.Get("Login_RevisaCorreoDesc", correo),
                    "OK");
            }
            catch (Exception ex)
            {
                MostrarError(AppStrings.Get("Login_ErrorEnviarRecuperacion", ex.Message));
            }
            finally
            {
                BtnOlvideContrasena.IsEnabled = true;
            }
        }

        private void MostrarError(string mensaje)
        {
            LblMensaje.Text = mensaje;
            LblMensaje.IsVisible = true;
        }
    }
}