using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Plugin.Firebase.Auth;
using GestorFinanzas.Strings;

namespace GestorFinanzas
{
    public class PlanAhorro
    {
        public string Id { get; set; } = "";
        public string UsuarioId { get; set; } = "";
        public string Nombre { get; set; } = "";
        public decimal MontoMeta { get; set; }
        public string Moneda { get; set; } = "DOP";
        public DateTime FechaInicio { get; set; }
        public DateTime FechaMeta { get; set; }
        public string Frecuencia { get; set; } = "Mensual"; // Diario, Semanal, Quincenal, Mensual
    }

    public partial class PlanAhorroPage : ContentPage
    {
        // Mismo Project ID que usa MovimientosPage
        private const string ProjectId = "gestorfinanzas-bed0a";

        private static readonly string[] MonedasDisponibles = { "DOP", "USD", "EUR" };
        private static readonly string[] FrecuenciasDisponibles = { "Diario", "Semanal", "Quincenal", "Mensual" };

        private static readonly Dictionary<string, int> DiasPorFrecuencia = new()
        {
            { "Diario", 1 },
            { "Semanal", 7 },
            { "Quincenal", 15 },
            { "Mensual", 30 }
        };

        private static readonly HttpClient _http = new HttpClient();
        private PlanAhorro? _planActual;

        public PlanAhorroPage()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await CargarPlanYProgreso();
        }

        private async Task<string> ObtenerTokenAsync()
        {
            var usuario = CrossFirebaseAuth.Current.CurrentUser;
            if (usuario == null) return "";
            var resultado = await usuario.GetIdTokenResultAsync(false);
            return resultado.Token;
        }

        // ---------- Cargar plan + progreso real ----------

        private async Task CargarPlanYProgreso()
        {
            var usuario = CrossFirebaseAuth.Current.CurrentUser;
            if (usuario == null)
            {
                await Shell.Current.GoToAsync("//MainPage");
                return;
            }

            try
            {
                _planActual = await ObtenerPlanAsync(usuario.Uid);

                if (_planActual == null)
                {
                    PanelSinMeta.IsVisible = true;
                    PanelConMeta.IsVisible = false;
                    BtnCrearEditarMeta.Text = AppStrings.Get("PlanAhorro_CrearMeta");
                    return;
                }

                PanelSinMeta.IsVisible = false;
                PanelConMeta.IsVisible = true;
                BtnCrearEditarMeta.Text = AppStrings.Get("PlanAhorro_EditarMeta");

                decimal ahorroRealDOP = await ObtenerAhorroRealDesdeAsync(usuario.Uid, _planActual.FechaInicio);
                MostrarResumenPlan(_planActual, ahorroRealDOP);
            }
            catch (Exception ex)
            {
                await DisplayAlert(AppStrings.Get("Comun_Error"), AppStrings.Get("PlanAhorro_ErrorCargarPlan", ex.GetType().Name + ": " + ex.Message), "OK");
            }
        }

        private void MostrarResumenPlan(PlanAhorro plan, decimal ahorroRealDOP)
        {
            var hoy = DateTime.UtcNow.Date;
            double diasTotales = Math.Max(1, (plan.FechaMeta.Date - plan.FechaInicio.Date).TotalDays);
            int diasPorPeriodo = DiasPorFrecuencia.TryGetValue(plan.Frecuencia, out var d) ? d : 30;
            double periodosTotales = Math.Max(1, diasTotales / diasPorPeriodo);

            decimal metaDOP = plan.MontoMeta * (Movimiento.TasaADOP.TryGetValue(plan.Moneda, out var t) ? t : 1m);
            decimal montoPorPeriodo = metaDOP / (decimal)periodosTotales;

            double diasTranscurridos = Math.Clamp((hoy - plan.FechaInicio.Date).TotalDays, 0, diasTotales);
            double periodosTranscurridos = diasTranscurridos / diasPorPeriodo;
            decimal ahorroEsperadoAFecha = montoPorPeriodo * (decimal)periodosTranscurridos;

            decimal diferencia = ahorroRealDOP - ahorroEsperadoAFecha;
            decimal porcentajeMeta = metaDOP > 0 ? (ahorroRealDOP / metaDOP) * 100 : 0;

            string simboloMoneda = Movimiento.SimbolosMoneda.TryGetValue(plan.Moneda, out var s) ? s : plan.Moneda;

            LblNombreMeta.Text = plan.Nombre;
            LblMontoMeta.Text = AppStrings.Get("PlanAhorro_Meta", simboloMoneda, plan.MontoMeta.ToString("N2", CultureInfo.InvariantCulture), plan.FechaMeta.ToString("dd/MM/yyyy"));
            LblMontoPorPeriodo.Text = AppStrings.Get("PlanAhorro_DebesAhorrar", montoPorPeriodo.ToString("N2", CultureInfo.InvariantCulture), FrecuenciaSingular(plan.Frecuencia));
            LblAhorroReal.Text = AppStrings.Get("PlanAhorro_LlevasAhorrado", ahorroRealDOP.ToString("N2", CultureInfo.InvariantCulture), Math.Clamp(porcentajeMeta, 0, 999).ToString("N0", CultureInfo.InvariantCulture));

            if (diferencia >= 0)
                LblEstado.Text = AppStrings.Get("PlanAhorro_VasBien", diferencia.ToString("N2", CultureInfo.InvariantCulture));
            else
                LblEstado.Text = AppStrings.Get("PlanAhorro_VasAtrasado", Math.Abs(diferencia).ToString("N2", CultureInfo.InvariantCulture));

            ProgressMeta.Progress = (double)Math.Clamp(porcentajeMeta / 100m, 0, 1);
        }

        // "clave" siempre queda en español internamente (es lo que se guarda en Firestore
        // en el campo Frecuencia), solo el texto que se MUESTRA cambia de idioma.
        private static string FrecuenciaSingular(string clave) => clave switch
        {
            "Diario" => AppStrings.Get("Frecuencia_Dia"),
            "Semanal" => AppStrings.Get("Frecuencia_SemanaSing"),
            "Quincenal" => AppStrings.Get("Frecuencia_QuincenaSing"),
            "Mensual" => AppStrings.Get("Frecuencia_MesSing"),
            _ => AppStrings.Get("Frecuencia_PeriodoSing")
        };

        // Traduce una clave fija de frecuencia ("Diario", "Semanal", ...) al texto que
        // se muestra en el idioma activo, para los ActionSheet de selección.
        private static string FrecuenciaClaveATexto(string clave) => clave switch
        {
            "Diario" => AppStrings.Get("Frecuencia_Diario"),
            "Semanal" => AppStrings.Get("Frecuencia_Semanal"),
            "Quincenal" => AppStrings.Get("Frecuencia_Quincenal"),
            "Mensual" => AppStrings.Get("Frecuencia_Mensual"),
            _ => clave
        };

        // Hace el camino inverso: del texto que el usuario tocó en el ActionSheet
        // (en el idioma que sea) de vuelta a la clave fija en español que se guarda.
        private static string FrecuenciaTextoAClave(string texto)
        {
            foreach (var clave in FrecuenciasDisponibles)
            {
                if (FrecuenciaClaveATexto(clave) == texto) return clave;
            }
            return texto;
        }

        // ---------- Crear / editar meta ----------

        private async void OnCrearOEditarMetaClicked(object? sender, EventArgs e)
        {
            string nombre = await DisplayPromptAsync(AppStrings.Get("PlanAhorro_NombreMetaTitulo"), AppStrings.Get("PlanAhorro_NombreMetaEjemplo"),
                initialValue: _planActual?.Nombre ?? "");
            if (string.IsNullOrWhiteSpace(nombre)) nombre = AppStrings.Get("PlanAhorro_NombrePorDefecto");

            string montoTexto = await DisplayPromptAsync(AppStrings.Get("PlanAhorro_MontoTotalTitulo"), AppStrings.Get("PlanAhorro_MontoTotalEjemplo"),
                initialValue: _planActual != null ? _planActual.MontoMeta.ToString(CultureInfo.InvariantCulture) : "",
                keyboard: Keyboard.Numeric);
            if (!decimal.TryParse(montoTexto, out decimal monto) || monto <= 0)
            {
                await DisplayAlert(AppStrings.Get("Comun_DatoInvalido"), AppStrings.Get("PlanAhorro_MetaInvalida"), "OK");
                return;
            }

            string moneda = await DisplayActionSheet(AppStrings.Get("Comun_MonedaPregunta"), AppStrings.Get("Comun_Cancelar"), null, MonedasDisponibles);
            if (!MonedasDisponibles.Contains(moneda)) return;

            var hoy = DateTime.UtcNow.Date;
            string plazo3 = AppStrings.Get("PlanAhorro_Plazo3Meses");
            string plazo6 = AppStrings.Get("PlanAhorro_Plazo6Meses");
            string plazo1anio = AppStrings.Get("PlanAhorro_Plazo1Anio");
            string plazo2anios = AppStrings.Get("PlanAhorro_Plazo2Anios");
            string plazoOtro = AppStrings.Get("PlanAhorro_PlazoOtro");

            string plazoTexto = await DisplayActionSheet(AppStrings.Get("PlanAhorro_PlazoTitulo"), AppStrings.Get("Comun_Cancelar"), null,
                plazo3, plazo6, plazo1anio, plazo2anios, plazoOtro);

            DateTime fechaMeta;
            if (plazoTexto == plazo3) fechaMeta = hoy.AddMonths(3);
            else if (plazoTexto == plazo6) fechaMeta = hoy.AddMonths(6);
            else if (plazoTexto == plazo1anio) fechaMeta = hoy.AddYears(1);
            else if (plazoTexto == plazo2anios) fechaMeta = hoy.AddYears(2);
            else if (plazoTexto == plazoOtro)
            {
                string fechaTexto = await DisplayPromptAsync(AppStrings.Get("PlanAhorro_FechaMetaTitulo"), AppStrings.Get("PlanAhorro_FechaMetaFormato"),
                    initialValue: hoy.AddYears(1).ToString("yyyy-MM-dd")) ?? "";
                if (!DateTime.TryParse(fechaTexto, CultureInfo.InvariantCulture, DateTimeStyles.None, out fechaMeta) || fechaMeta.Date <= hoy)
                {
                    await DisplayAlert(AppStrings.Get("PlanAhorro_FechaInvalidaTitulo"), AppStrings.Get("PlanAhorro_FechaInvalidaDesc"), "OK");
                    return;
                }
            }
            else
            {
                return; // cancelado
            }

            string frecuenciaTexto = await DisplayActionSheet(AppStrings.Get("PlanAhorro_FrecuenciaTitulo"), AppStrings.Get("Comun_Cancelar"), null,
                FrecuenciasDisponibles.Select(FrecuenciaClaveATexto).ToArray());
            string frecuencia = FrecuenciaTextoAClave(frecuenciaTexto);
            if (!FrecuenciasDisponibles.Contains(frecuencia)) return;

            var usuario = CrossFirebaseAuth.Current.CurrentUser;
            if (usuario == null) return;

            bool exito = await GuardarPlanAsync(
                idExistente: _planActual?.Id,
                nombre: nombre,
                montoMeta: monto,
                moneda: moneda,
                fechaInicio: _planActual?.FechaInicio ?? hoy,
                fechaMeta: fechaMeta,
                frecuencia: frecuencia,
                usuarioId: usuario.Uid);

            if (exito)
            {
                await CargarPlanYProgreso();
            }
        }

        private async void OnBorrarMetaClicked(object? sender, EventArgs e)
        {
            if (_planActual == null) return;

            bool confirmar = await DisplayAlert(AppStrings.Get("PlanAhorro_BorrarMeta"), AppStrings.Get("PlanAhorro_BorrarMetaConfirmacion", _planActual.Nombre), AppStrings.Get("Comun_SiBorrar"), AppStrings.Get("Comun_Cancelar"));
            if (!confirmar) return;

            try
            {
                string token = await ObtenerTokenAsync();
                string url = $"https://firestore.googleapis.com/v1/projects/{ProjectId}/databases/(default)/documents/planesAhorro/{_planActual.Id}";

                var request = new HttpRequestMessage(HttpMethod.Delete, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var response = await _http.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    string body = await response.Content.ReadAsStringAsync();
                    await DisplayAlert(AppStrings.Get("Comun_ErrorAlBorrar"), "HTTP " + (int)response.StatusCode + ": " + body, "OK");
                    return;
                }

                await CargarPlanYProgreso();
            }
            catch (Exception ex)
            {
                await DisplayAlert(AppStrings.Get("Comun_ErrorAlBorrar"), ex.GetType().Name + ": " + ex.Message, "OK");
            }
        }

        // ---------- Registrar aporte de ahorro (un solo diálogo) ----------

        private async void OnRegistrarAporteClicked(object? sender, EventArgs e)
        {
            if (_planActual == null) return;

            string simboloMoneda = Movimiento.SimbolosMoneda.TryGetValue(_planActual.Moneda, out var s) ? s : _planActual.Moneda;

            string montoTexto = await DisplayPromptAsync(
                AppStrings.Get("PlanAhorro_CuantoAhorrasteTitulo"),
                AppStrings.Get("PlanAhorro_CuantoAhorrasteDesc", simboloMoneda, _planActual.Nombre),
                initialValue: "",
                keyboard: Keyboard.Numeric);

            if (!decimal.TryParse(montoTexto, out decimal monto) || monto <= 0)
            {
                if (!string.IsNullOrWhiteSpace(montoTexto))
                    await DisplayAlert(AppStrings.Get("Comun_DatoInvalido"), AppStrings.Get("Movimientos_MontoInvalido"), "OK");
                return;
            }

            var usuario = CrossFirebaseAuth.Current.CurrentUser;
            if (usuario == null) return;

            bool exito = await RegistrarAporteAsync(monto, _planActual.Moneda, usuario.Uid);

            if (exito)
            {
                await DisplayAlert(AppStrings.Get("PlanAhorro_ListoTitulo"), AppStrings.Get("PlanAhorro_ListoDesc", simboloMoneda, monto.ToString("N2", CultureInfo.InvariantCulture)), "OK");
                await CargarPlanYProgreso();

                // Registrar un aporte también cuenta como acción para el anuncio intersticial.
                AdsManager.RegistrarAccionYMostrarSiToca();
            }
        }

        private async Task<bool> RegistrarAporteAsync(decimal monto, string moneda, string usuarioId)
        {
            try
            {
                string token = await ObtenerTokenAsync();
                string url = $"https://firestore.googleapis.com/v1/projects/{ProjectId}/databases/(default)/documents/movimientos";

                var payload = new
                {
                    fields = new
                    {
                        Tipo = new { stringValue = "Ahorro" },
                        Categoria = new { stringValue = _planActual?.Nombre ?? "Ahorro" },
                        Descripcion = new { stringValue = $"Aporte a la meta '{_planActual?.Nombre}'" },
                        Monto = new { doubleValue = (double)monto },
                        Fecha = new { stringValue = DateTime.UtcNow.ToString("o") },
                        UsuarioId = new { stringValue = usuarioId },
                        Pais = new { stringValue = "" },
                        Moneda = new { stringValue = moneda }
                    }
                };

                string json = JsonSerializer.Serialize(payload);

                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _http.SendAsync(request);
                string body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    await DisplayAlert(AppStrings.Get("PlanAhorro_ErrorAlRegistrar"), "HTTP " + (int)response.StatusCode + ": " + body, "OK");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                await DisplayAlert(AppStrings.Get("PlanAhorro_ErrorAlRegistrar"), ex.GetType().Name + ": " + ex.Message, "OK");
                return false;
            }
        }

        // ---------- Firestore: leer plan ----------

        private async Task<PlanAhorro?> ObtenerPlanAsync(string usuarioId)
        {
            var (exito, documentos, _) = await ConsultarColeccionPorUsuarioAsync("planesAhorro", usuarioId);
            if (!exito) return null;

            PlanAhorro? planEncontrado = null;

            foreach (var d in documentos)
            {
                try
                {
                    if (!d.TryGetProperty("fields", out JsonElement fields)) continue;

                    string usuarioIdDoc = ObtenerString(fields, "UsuarioId");
                    if (usuarioIdDoc != usuarioId) continue;

                    string nombreCompleto = d.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                    string id = nombreCompleto.Contains('/')
                        ? nombreCompleto.Substring(nombreCompleto.LastIndexOf('/') + 1)
                        : nombreCompleto;

                    var plan = new PlanAhorro
                    {
                        Id = id,
                        UsuarioId = usuarioIdDoc,
                        Nombre = ObtenerString(fields, "Nombre"),
                        MontoMeta = ObtenerDecimal(fields, "MontoMeta"),
                        Moneda = string.IsNullOrWhiteSpace(ObtenerString(fields, "Moneda")) ? "DOP" : ObtenerString(fields, "Moneda"),
                        FechaInicio = ObtenerFecha(fields, "FechaInicio"),
                        FechaMeta = ObtenerFecha(fields, "FechaMeta"),
                        Frecuencia = string.IsNullOrWhiteSpace(ObtenerString(fields, "Frecuencia")) ? "Mensual" : ObtenerString(fields, "Frecuencia")
                    };

                    if (planEncontrado == null || plan.FechaInicio > planEncontrado.FechaInicio)
                        planEncontrado = plan;
                }
                catch (Exception exDoc)
                {
                    System.Diagnostics.Debug.WriteLine($"Plan inválido, se omite: {exDoc.Message}\n{d.GetRawText()}");
                }
            }

            return planEncontrado;
        }

        private async Task<(bool exito, List<JsonElement> documentos, string error)> ConsultarColeccionPorUsuarioAsync(string coleccion, string usuarioId)
        {
            string token = await ObtenerTokenAsync();
            string url = $"https://firestore.googleapis.com/v1/projects/{ProjectId}/databases/(default)/documents:runQuery";

            var consulta = new
            {
                structuredQuery = new
                {
                    from = new object[] { new { collectionId = coleccion } },
                    where = new
                    {
                        fieldFilter = new
                        {
                            field = new { fieldPath = "UsuarioId" },
                            op = "EQUAL",
                            value = new { stringValue = usuarioId }
                        }
                    }
                }
            };

            string json = JsonSerializer.Serialize(consulta);

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return (false, new List<JsonElement>(), "HTTP " + (int)response.StatusCode + ": " + body);

            var resultado = new List<JsonElement>();

            using JsonDocument doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.TryGetProperty("document", out var d))
                        resultado.Add(d.Clone());
                }
            }

            return (true, resultado, "");
        }

        // ---------- Firestore: guardar plan (crear o editar) ----------

        private async Task<bool> GuardarPlanAsync(
            string? idExistente, string nombre, decimal montoMeta, string moneda,
            DateTime fechaInicio, DateTime fechaMeta, string frecuencia, string usuarioId)
        {
            try
            {
                string token = await ObtenerTokenAsync();
                string urlBase = $"https://firestore.googleapis.com/v1/projects/{ProjectId}/databases/(default)/documents/planesAhorro";
                string url = idExistente == null ? urlBase : $"{urlBase}/{idExistente}";

                var payload = new
                {
                    fields = new
                    {
                        Nombre = new { stringValue = nombre.Trim() },
                        MontoMeta = new { doubleValue = (double)montoMeta },
                        Moneda = new { stringValue = moneda },
                        FechaInicio = new { stringValue = fechaInicio.ToUniversalTime().ToString("o") },
                        FechaMeta = new { stringValue = fechaMeta.ToUniversalTime().ToString("o") },
                        Frecuencia = new { stringValue = frecuencia },
                        UsuarioId = new { stringValue = usuarioId }
                    }
                };

                string json = JsonSerializer.Serialize(payload);

                var request = new HttpRequestMessage(idExistente == null ? HttpMethod.Post : HttpMethod.Patch, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _http.SendAsync(request);
                string body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    await DisplayAlert(AppStrings.Get("Comun_ErrorAlGuardar"), "HTTP " + (int)response.StatusCode + ": " + body, "OK");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                await DisplayAlert(AppStrings.Get("Comun_ErrorAlGuardar"), ex.GetType().Name + ": " + ex.Message, "OK");
                return false;
            }
        }

        // ---------- Firestore: ahorro real acumulado (solo aportes tipo "Ahorro") ----------

        private async Task<decimal> ObtenerAhorroRealDesdeAsync(string usuarioId, DateTime desde)
        {
            var (exito, documentos, _) = await ConsultarColeccionPorUsuarioAsync("movimientos", usuarioId);
            if (!exito) return 0;

            decimal total = 0;

            foreach (var d in documentos)
            {
                try
                {
                    if (!d.TryGetProperty("fields", out JsonElement fields)) continue;

                    string usuarioIdDoc = ObtenerString(fields, "UsuarioId");
                    if (usuarioIdDoc != usuarioId) continue;

                    string tipo = ObtenerString(fields, "Tipo");
                    if (tipo != "Ahorro") continue; // solo contamos aportes reales, no ingresos/gastos generales

                    DateTime fecha = ObtenerFecha(fields, "Fecha");
                    if (fecha < desde) continue;

                    decimal monto = ObtenerDecimal(fields, "Monto");
                    string moneda = ObtenerString(fields, "Moneda");
                    if (string.IsNullOrWhiteSpace(moneda)) moneda = "DOP";

                    decimal montoDOP = monto * (Movimiento.TasaADOP.TryGetValue(moneda, out var t) ? t : 1m);

                    total += montoDOP;
                }
                catch (Exception exDoc)
                {
                    System.Diagnostics.Debug.WriteLine($"Movimiento inválido, se omite: {exDoc.Message}\n{d.GetRawText()}");
                }
            }

            return total;
        }

        // ---------- Helpers para leer JSON de Firestore ----------

        private static string ObtenerString(JsonElement fields, string clave)
        {
            if (fields.TryGetProperty(clave, out var el) && el.TryGetProperty("stringValue", out var sv))
                return sv.GetString() ?? "";
            return "";
        }

        private static decimal ObtenerDecimal(JsonElement fields, string clave)
        {
            if (fields.TryGetProperty(clave, out var el))
            {
                if (el.TryGetProperty("doubleValue", out var dv))
                    return (decimal)dv.GetDouble();

                if (el.TryGetProperty("integerValue", out var iv))
                {
                    var texto = iv.GetString();
                    if (texto != null && decimal.TryParse(texto, NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
                        return num;
                }
            }
            return 0;
        }

        private static DateTime ObtenerFecha(JsonElement fields, string clave)
        {
            if (fields.TryGetProperty(clave, out var el) && el.TryGetProperty("stringValue", out var sv))
            {
                if (DateTime.TryParse(sv.GetString(), null, DateTimeStyles.RoundtripKind, out var fecha))
                    return fecha;
            }
            return DateTime.Now;
        }
    }
}