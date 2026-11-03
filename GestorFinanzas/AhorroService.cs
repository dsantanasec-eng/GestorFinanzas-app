using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Plugin.Firebase.Auth;

namespace GestorFinanzas
{
    public static class AhorroService
    {
        private const string ProjectId = "gestorfinanzas-bed0a";
        private static readonly HttpClient _http = new HttpClient();

        private static async Task<string> ObtenerTokenAsync()
        {
            var usuario = CrossFirebaseAuth.Current.CurrentUser;
            if (usuario == null) return "";
            var resultado = await usuario.GetIdTokenResultAsync(false);
            return resultado.Token;
        }

        public static async Task<PlanAhorro?> ObtenerPlanActivoAsync(string usuarioId)
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
                catch
                {
                    // documento inválido, se omite
                }
            }

            return planEncontrado;
        }

        public static async Task<bool> RegistrarAporteAsync(decimal monto, string moneda, string nombreMeta, string usuarioId)
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
                        Categoria = new { stringValue = nombreMeta },
                        Descripcion = new { stringValue = $"Aporte a la meta '{nombreMeta}' (recordatorio diario)" },
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
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<(bool exito, List<JsonElement> documentos, string error)> ConsultarColeccionPorUsuarioAsync(string coleccion, string usuarioId)
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