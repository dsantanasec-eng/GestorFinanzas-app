using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microcharts;
using Plugin.Firebase.Auth;
using SkiaSharp;
using GestorFinanzas.Strings;

namespace GestorFinanzas
{
    public class Movimiento
    {
        // Símbolo visual para cada moneda soportada
        public static readonly Dictionary<string, string> SimbolosMoneda = new()
        {
            { "DOP", "RD$" },
            { "USD", "US$" },
            { "EUR", "€" }
        };

        // Tasas aproximadas de conversión a DOP (Peso Dominicano), solo para poder
        // sumar/graficar montos en distintas monedas juntos. Ajusta según la tasa real.
        public static readonly Dictionary<string, decimal> TasaADOP = new()
        {
            { "DOP", 1m },
            { "USD", 60m },
            { "EUR", 65m }
        };

        public string Id { get; set; } = "";
        public string Tipo { get; set; } = "Gasto"; // "Ingreso" o "Gasto"
        public string Categoria { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public decimal Monto { get; set; }
        public DateTime Fecha { get; set; }
        public string UsuarioId { get; set; } = "";
        public string Pais { get; set; } = "";
        public string Moneda { get; set; } = "DOP";

        public string MontoFormateado
        {
            get
            {
                string simbolo = SimbolosMoneda.TryGetValue(Moneda, out var s) ? s : Moneda;
                return (Tipo == "Ingreso" ? "+ " : "- ") + simbolo + " " + Monto.ToString("N2", CultureInfo.InvariantCulture);
            }
        }

        public Color ColorMonto => Tipo == "Ingreso" ? Colors.Green : Colors.Red;

        // Monto convertido a DOP, usado internamente para sumar/graficar/proyectar
        // cuando hay movimientos en varias monedas mezclados.
        public decimal MontoEnDOP => Monto * (TasaADOP.TryGetValue(Moneda, out var t) ? t : 1m);
    }

    public partial class MovimientosPage : ContentPage
    {
        // Reemplaza esto por el Project ID real de tu proyecto Firebase
        // (lo ves en Firebase > Configuración del proyecto > "ID del proyecto")
        private const string ProjectId = "gestorfinanzas-bed0a";

        private static readonly string[] PaisesDisponibles = { "República Dominicana", "Estados Unidos", "España", "Otro" };
        private static readonly string[] MonedasDisponibles = { "DOP", "USD", "EUR" };

        private List<Movimiento> _todosLosMovimientos = new();
        private ObservableCollection<Movimiento> _movimientos = new();
        private static readonly HttpClient _http = new HttpClient();

        // Evita que el evento de los Pickers dispare filtros mientras los estamos rellenando
        private bool _actualizandoFiltros = false;

        public MovimientosPage()
        {
            InitializeComponent();
            ListaMovimientos.ItemsSource = _movimientos;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // Deja el anuncio intersticial listo desde ya, para que cuando toque
            // mostrarlo (al guardar un movimiento) no haya que esperar a que cargue.
            AdsManager.Precargar();

            await CargarMovimientos();
        }

        private async Task<string> ObtenerTokenAsync()
        {
            var usuario = CrossFirebaseAuth.Current.CurrentUser;
            if (usuario == null) return "";
            var resultado = await usuario.GetIdTokenResultAsync(false);
            return resultado.Token;
        }

        private async Task CargarMovimientos()
        {
            var usuario = CrossFirebaseAuth.Current.CurrentUser;
            if (usuario == null)
            {
                await Shell.Current.GoToAsync("//MainPage");
                return;
            }

            try
            {
                var (exito, documentos, error) = await ConsultarColeccionPorUsuarioAsync("movimientos", usuario.Uid);
                if (!exito)
                {
                    await DisplayAlert(AppStrings.Get("Comun_ErrorAlCargar"), error, "OK");
                    return;
                }

                List<Movimiento> listaTemporal = new List<Movimiento>();

                foreach (var d in documentos)
                {
                    try
                    {
                        if (!d.TryGetProperty("fields", out JsonElement fields))
                            continue; // documento sin campos: lo ignoramos

                        string nombreCompleto = d.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                        string id = nombreCompleto.Contains('/')
                            ? nombreCompleto.Substring(nombreCompleto.LastIndexOf('/') + 1)
                            : nombreCompleto;

                        string usuarioIdDoc = ObtenerString(fields, "UsuarioId");
                        if (usuarioIdDoc != usuario.Uid) continue;

                        string moneda = ObtenerString(fields, "Moneda");
                        if (string.IsNullOrWhiteSpace(moneda)) moneda = "DOP"; // movimientos viejos sin moneda

                        var mov = new Movimiento
                        {
                            Id = id,
                            Tipo = ObtenerString(fields, "Tipo"),
                            Categoria = ObtenerString(fields, "Categoria"),
                            Descripcion = ObtenerString(fields, "Descripcion"),
                            Monto = ObtenerDecimal(fields, "Monto"),
                            UsuarioId = usuarioIdDoc,
                            Fecha = ObtenerFecha(fields, "Fecha"),
                            Pais = ObtenerString(fields, "Pais"),
                            Moneda = moneda
                        };
                        listaTemporal.Add(mov);
                    }
                    catch (Exception exDoc)
                    {
                        // Un documento con forma inesperada no debe tumbar la lista completa
                        System.Diagnostics.Debug.WriteLine($"Movimiento inválido, se omite: {exDoc.Message}\n{d.GetRawText()}");
                    }
                }

                _todosLosMovimientos = listaTemporal.OrderByDescending(m => m.Fecha).ToList();

                ActualizarOpcionesDeFiltro();
                AplicarFiltros();
            }
            catch (Exception ex)
            {
                await DisplayAlert(AppStrings.Get("Comun_Error"), AppStrings.Get("Movimientos_ErrorCargar", ex.GetType().Name + ": " + ex.Message), "OK");
            }
        }

        // Las reglas de seguridad de Firestore exigen que un usuario solo pueda leer SUS
        // propios documentos (UsuarioId == su uid). Para que una consulta de "listar todo"
        // funcione bajo esas reglas, hay que pedirle a Firestore ese filtro en la propia
        // consulta (con :runQuery) — un simple GET a la colección completa ya no funciona
        // y devuelve 403 PERMISSION_DENIED.
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
                        resultado.Add(d.Clone()); // Clone: el JsonDocument se libera al salir del using
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

                // Firestore devuelve los enteros como integerValue, y viene como STRING
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

        // ---------- Filtros ----------

        private void ActualizarOpcionesDeFiltro()
        {
            _actualizandoFiltros = true;

            var categoriaAnterior = PickerCategoria.SelectedItem as string;
            var categorias = new List<string> { "Todas" };
            categorias.AddRange(_todosLosMovimientos
                .Select(m => m.Categoria)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct()
                .OrderBy(c => c));
            PickerCategoria.ItemsSource = categorias;
            PickerCategoria.SelectedIndex = (categoriaAnterior != null && categorias.Contains(categoriaAnterior))
                ? categorias.IndexOf(categoriaAnterior) : 0;

            var paisAnterior = PickerPais.SelectedItem as string;
            var paises = new List<string> { "Todos" };
            paises.AddRange(_todosLosMovimientos
                .Select(m => m.Pais)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct()
                .OrderBy(p => p));
            PickerPais.ItemsSource = paises;
            PickerPais.SelectedIndex = (paisAnterior != null && paises.Contains(paisAnterior))
                ? paises.IndexOf(paisAnterior) : 0;

            var monedaAnterior = PickerMoneda.SelectedItem as string;
            var monedas = new List<string> { "Todas" };
            monedas.AddRange(MonedasDisponibles);
            PickerMoneda.ItemsSource = monedas;
            PickerMoneda.SelectedIndex = (monedaAnterior != null && monedas.Contains(monedaAnterior))
                ? monedas.IndexOf(monedaAnterior) : 0;

            _actualizandoFiltros = false;
        }

        private void OnFiltroCambiado(object? sender, EventArgs e)
        {
            if (_actualizandoFiltros) return;
            AplicarFiltros();
        }

        private void AplicarFiltros()
        {
            string categoria = PickerCategoria.SelectedItem as string ?? "Todas";
            string pais = PickerPais.SelectedItem as string ?? "Todos";
            string moneda = PickerMoneda.SelectedItem as string ?? "Todas";

            var filtrados = _todosLosMovimientos.Where(m =>
                (categoria == "Todas" || m.Categoria == categoria) &&
                (pais == "Todos" || m.Pais == pais) &&
                (moneda == "Todas" || m.Moneda == moneda)
            ).OrderByDescending(m => m.Fecha).ToList();

            _movimientos.Clear();
            foreach (var mov in filtrados) _movimientos.Add(mov);

            ActualizarBalance(filtrados, moneda);
            ActualizarGrafico(filtrados);
            ActualizarProyeccion(filtrados);
        }

        // ---------- Balance ----------

        private void ActualizarBalance(List<Movimiento> lista, string filtroMoneda)
        {
            if (filtroMoneda != "Todas")
            {
                decimal balance = lista.Sum(m => m.Tipo == "Ingreso" ? m.Monto : -m.Monto);
                string simbolo = Movimiento.SimbolosMoneda.TryGetValue(filtroMoneda, out var s) ? s : filtroMoneda;
                LblBalance.Text = simbolo + " " + balance.ToString("N2", CultureInfo.InvariantCulture);
            }
            else
            {
                decimal balanceDOP = lista.Sum(m => m.Tipo == "Ingreso" ? m.MontoEnDOP : -m.MontoEnDOP);
                LblBalance.Text = AppStrings.Get("Movimientos_BalanceConvertido", "RD$ " + balanceDOP.ToString("N2", CultureInfo.InvariantCulture));
            }
        }

        // ---------- Gráfico ----------

        private void ActualizarGrafico(List<Movimiento> lista)
        {
            var porCategoria = lista
                .Where(m => m.Tipo == "Gasto")
                .GroupBy(m => string.IsNullOrWhiteSpace(m.Categoria) ? AppStrings.Get("Comun_SinCategoria") : m.Categoria)
                .Select(g => new { Categoria = g.Key, Total = g.Sum(m => m.MontoEnDOP) })
                .OrderByDescending(g => g.Total)
                .ToList();

            if (porCategoria.Count == 0)
            {
                ChartGastos.Chart = null;
                return;
            }

            var colores = new[]
            {
                SKColor.Parse("#6C5CE7"), SKColor.Parse("#00B894"), SKColor.Parse("#FDCB6E"),
                SKColor.Parse("#E17055"), SKColor.Parse("#0984E3"), SKColor.Parse("#D63031"),
                SKColor.Parse("#A29BFE"), SKColor.Parse("#00CEC9")
            };

            var entradas = porCategoria.Select((g, i) => new ChartEntry((float)g.Total)
            {
                Label = g.Categoria,
                ValueLabel = g.Total.ToString("N0", CultureInfo.InvariantCulture),
                Color = colores[i % colores.Length]
            }).ToArray();

            ChartGastos.Chart = new DonutChart
            {
                Entries = entradas,
                LabelTextSize = 28f
            };
        }

        // ---------- Proyección de ahorro ----------

        private void ActualizarProyeccion(List<Movimiento> lista)
        {
            if (lista.Count == 0)
            {
                LblPromedioMensual.Text = AppStrings.Get("Movimientos_SinMovimientosProyeccion");
                LblProyeccion3Meses.Text = "";
                LblProyeccion6Meses.Text = "";
                LblConsejo.Text = "";
                return;
            }

            // Agrupamos por mes calendario (convertido a DOP para poder sumar todo junto)
            var porMes = lista
                .GroupBy(m => new DateTime(m.Fecha.Year, m.Fecha.Month, 1))
                .Select(g => g.Sum(m => m.Tipo == "Ingreso" ? m.MontoEnDOP : -m.MontoEnDOP))
                .ToList();

            decimal promedioMensual = porMes.Count > 0 ? porMes.Average() : 0;
            decimal balanceActual = lista.Sum(m => m.Tipo == "Ingreso" ? m.MontoEnDOP : -m.MontoEnDOP);

            decimal proyeccion3 = balanceActual + (promedioMensual * 3);
            decimal proyeccion6 = balanceActual + (promedioMensual * 6);

            string mesesTexto = porMes.Count == 1 ? AppStrings.Get("Movimientos_BasadoEn1Mes") : AppStrings.Get("Movimientos_BasadoEnNMeses", porMes.Count);
            LblPromedioMensual.Text = AppStrings.Get("Movimientos_AhorroPromedioMensual", promedioMensual.ToString("N2", CultureInfo.InvariantCulture), mesesTexto);
            LblProyeccion3Meses.Text = AppStrings.Get("Movimientos_Proyeccion3Meses", proyeccion3.ToString("N2", CultureInfo.InvariantCulture));
            LblProyeccion6Meses.Text = AppStrings.Get("Movimientos_Proyeccion6Meses", proyeccion6.ToString("N2", CultureInfo.InvariantCulture));

            if (porMes.Count < 2)
            {
                LblConsejo.Text = AppStrings.Get("Movimientos_ConsejoMasHistorial");
                return;
            }

            var categoriaMasGastada = lista
                .Where(m => m.Tipo == "Gasto")
                .GroupBy(m => string.IsNullOrWhiteSpace(m.Categoria) ? AppStrings.Get("Comun_SinCategoria") : m.Categoria)
                .Select(g => new { Categoria = g.Key, Total = g.Sum(m => m.MontoEnDOP) })
                .OrderByDescending(g => g.Total)
                .FirstOrDefault();

            if (categoriaMasGastada != null && categoriaMasGastada.Total > 0)
            {
                decimal ahorroPosible = categoriaMasGastada.Total * 0.15m; // sugerencia: reducir 15%
                LblConsejo.Text = AppStrings.Get("Movimientos_Tip", categoriaMasGastada.Categoria, categoriaMasGastada.Total.ToString("N2", CultureInfo.InvariantCulture), ahorroPosible.ToString("N2", CultureInfo.InvariantCulture));
            }
            else
            {
                LblConsejo.Text = "";
            }
        }

        // ---------- Guardar (crear o editar) ----------

        // idExistente == null  -> crea un movimiento nuevo (POST)
        // idExistente != null  -> edita ese movimiento (PATCH)
        private async Task<bool> GuardarMovimientoAsync(
            string? idExistente, string tipo, string categoria, string descripcion,
            decimal monto, string moneda, string pais, string usuarioId, DateTime fecha)
        {
            try
            {
                string token = await ObtenerTokenAsync();
                string urlBase = $"https://firestore.googleapis.com/v1/projects/{ProjectId}/databases/(default)/documents/movimientos";
                string url = idExistente == null ? urlBase : $"{urlBase}/{idExistente}";

                var payload = new
                {
                    fields = new
                    {
                        Tipo = new { stringValue = tipo },
                        Categoria = new { stringValue = categoria.Trim() },
                        Descripcion = new { stringValue = descripcion.Trim() },
                        Monto = new { doubleValue = (double)monto },
                        Fecha = new { stringValue = fecha.ToUniversalTime().ToString("o") },
                        UsuarioId = new { stringValue = usuarioId },
                        Pais = new { stringValue = pais.Trim() },
                        Moneda = new { stringValue = moneda }
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

        // Pide los datos de un movimiento por medio de diálogos.
        // Si "existente" es null, arranca todo en blanco (para agregar).
        // Si "existente" tiene valor, precarga sus datos (para editar).
        private async Task<bool> PedirYGuardarMovimientoAsync(Movimiento? existente)
        {
            // "Ingreso"/"Gasto" se dejan en español porque es lo que se guarda en
            // Firestore (campo Tipo) y lo que usa el resto del código para comparar
            // (ColorMonto, filtros, etc.). Solo se traduce lo que el usuario VE.
            string textoIngreso = AppStrings.Get("Movimientos_Ingreso");
            string textoGasto = AppStrings.Get("Movimientos_Gasto");
            string tipoTexto = await DisplayActionSheet(AppStrings.Get("Movimientos_TipoPregunta"), AppStrings.Get("Comun_Cancelar"), null, textoIngreso, textoGasto);
            string tipo = tipoTexto == textoIngreso ? "Ingreso" : tipoTexto == textoGasto ? "Gasto" : "";
            if (tipo != "Ingreso" && tipo != "Gasto") return false;

            string categoria = await DisplayPromptAsync(AppStrings.Get("Movimientos_CategoriaTitulo"), AppStrings.Get("Movimientos_CategoriaEjemplo"),
                initialValue: existente?.Categoria ?? "");
            if (string.IsNullOrWhiteSpace(categoria)) return false;

            string descripcion = await DisplayPromptAsync(AppStrings.Get("Movimientos_DescripcionTitulo"), AppStrings.Get("Movimientos_DescripcionEjemplo"),
                initialValue: existente?.Descripcion ?? "") ?? "";

            string montoTexto = await DisplayPromptAsync(AppStrings.Get("Movimientos_MontoTitulo"), AppStrings.Get("Movimientos_MontoEjemplo"),
                initialValue: existente != null ? existente.Monto.ToString(CultureInfo.InvariantCulture) : "",
                keyboard: Keyboard.Numeric);
            if (!decimal.TryParse(montoTexto, out decimal monto) || monto <= 0)
            {
                await DisplayAlert(AppStrings.Get("Comun_DatoInvalido"), AppStrings.Get("Movimientos_MontoInvalido"), "OK");
                return false;
            }

            string moneda = await DisplayActionSheet(AppStrings.Get("Comun_MonedaPregunta"), AppStrings.Get("Comun_Cancelar"), null, MonedasDisponibles);
            if (!MonedasDisponibles.Contains(moneda)) return false;

            // Nota: los nombres de país se dejan en español a propósito (igual que la
            // categoría), porque quedan guardados tal cual en Firestore y el filtro de
            // país de la lista se arma leyendo esos mismos valores guardados. Traducir
            // solo la pantalla, sin tocar los datos ya guardados, generaría duplicados
            // (ej. "España" y "Spain" como si fueran países distintos).
            string pais = await DisplayActionSheet(AppStrings.Get("Movimientos_PaisPregunta"), AppStrings.Get("Comun_Cancelar"), null, PaisesDisponibles);
            if (string.IsNullOrWhiteSpace(pais) || pais == AppStrings.Get("Comun_Cancelar")) return false;
            if (pais == "Otro")
            {
                pais = await DisplayPromptAsync(AppStrings.Get("Movimientos_PaisTitulo"), AppStrings.Get("Movimientos_PaisEscribe"), initialValue: existente?.Pais ?? "") ?? "Otro";
            }

            var usuario = CrossFirebaseAuth.Current.CurrentUser;
            if (usuario == null) return false;

            bool exito = await GuardarMovimientoAsync(
                idExistente: existente?.Id,
                tipo: tipo,
                categoria: categoria,
                descripcion: descripcion,
                monto: monto,
                moneda: moneda,
                pais: pais,
                usuarioId: existente?.UsuarioId ?? usuario.Uid,
                fecha: existente?.Fecha ?? DateTime.UtcNow);

            if (exito)
            {
                await CargarMovimientos();

                // Cada movimiento guardado (agregado o editado) cuenta como una acción
                // para el anuncio intersticial — solo se ve 1 de cada varias veces.
                AdsManager.RegistrarAccionYMostrarSiToca();
            }

            return exito;
        }

        // ---------- Agregar movimiento ----------

        private async void OnAgregarClicked(object? sender, EventArgs e)
        {
            await PedirYGuardarMovimientoAsync(existente: null);
        }

        // ---------- Editar / Borrar movimiento (swipe en la lista) ----------

        private async void OnEditarSwipe(object? sender, EventArgs e)
        {
            if (sender is not SwipeItem item || item.BindingContext is not Movimiento mov) return;
            await PedirYGuardarMovimientoAsync(existente: mov);
        }

        private async void OnBorrarSwipe(object? sender, EventArgs e)
        {
            if (sender is not SwipeItem item || item.BindingContext is not Movimiento mov) return;

            bool confirmar = await DisplayAlert(
                AppStrings.Get("Movimientos_BorrarTitulo"),
                AppStrings.Get("Movimientos_BorrarConfirmacion", mov.Categoria, mov.MontoFormateado),
                AppStrings.Get("Comun_SiBorrar"), AppStrings.Get("Comun_Cancelar"));
            if (!confirmar) return;

            try
            {
                string token = await ObtenerTokenAsync();
                string url = $"https://firestore.googleapis.com/v1/projects/{ProjectId}/databases/(default)/documents/movimientos/{mov.Id}";

                var request = new HttpRequestMessage(HttpMethod.Delete, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var response = await _http.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    string body = await response.Content.ReadAsStringAsync();
                    await DisplayAlert(AppStrings.Get("Comun_ErrorAlBorrar"), "HTTP " + (int)response.StatusCode + ": " + body, "OK");
                    return;
                }

                await CargarMovimientos();
            }
            catch (Exception ex)
            {
                await DisplayAlert(AppStrings.Get("Comun_ErrorAlBorrar"), ex.GetType().Name + ": " + ex.Message, "OK");
            }
        }

        private async void OnPlanAhorroClicked(object? sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//PlanAhorroPage");
        }

        private async void OnCerrarSesionClicked(object? sender, EventArgs e)
        {
            await CrossFirebaseAuth.Current.SignOutAsync();
            await Shell.Current.GoToAsync("//MainPage");
        }
    }
}