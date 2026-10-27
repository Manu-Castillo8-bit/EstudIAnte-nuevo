using EstudIAnte.Models;
using EstudIAnte.Services;

namespace EstudIAnte;

public partial class DocumentosView : ContentView
{
    const long MaxBytes = 20 * 1024 * 1024;
    bool _trabajando;

    public DocumentosView()
    {
        InitializeComponent();
        Loaded += (_, _) => _ = CargarDocumentosAsync();
    }

    async Task CargarDocumentosAsync()
    {
        var usuario = AuthService.UsuarioActual;
        if (usuario is null)
            return;

        var conn = await LocalDatabase.GetAsync();
        _docs = await conn.Table<Documento>()
            .Where(d => d.UsuarioId == usuario.Id)
            .OrderByDescending(d => d.CreadoEn)
            .ToListAsync();

        AplicarFiltro();
    }

    List<Documento> _docs = new();

    void AplicarFiltro()
    {
        var texto = Buscador.Text?.Trim();
        var resultados = string.IsNullOrWhiteSpace(texto)
            ? _docs
            : _docs.Where(d =>
                d.Nombre.Contains(texto, StringComparison.OrdinalIgnoreCase) ||
                d.Resumen.Contains(texto, StringComparison.OrdinalIgnoreCase) ||
                d.Texto.Contains(texto, StringComparison.OrdinalIgnoreCase));

        // Lista ordenada por fecha (más reciente primero) y ya filtrada.
        ListaDocumentos.ItemsSource = resultados.Select(d => new DocumentoItem(d)).ToList();
    }

    void Buscador_TextChanged(object? sender, TextChangedEventArgs e) => AplicarFiltro();

    /// <summary>Recarga los documentos (la llama MainPage al volver a la sección).</summary>
    public async Task RefrescarAsync() => await CargarDocumentosAsync();

    async void Subir_Click(object? sender, EventArgs e) => await SubirAsync();

    async void ZonaCarga_Tapped(object? sender, TappedEventArgs e) => await SubirAsync();

    async Task SubirAsync()
    {
        if (_trabajando)
            return;

        var usuario = AuthService.UsuarioActual;
        if (usuario is null)
        {
            await MostrarAsync("Aviso", "Debes iniciar sesión para subir documentos.");
            return;
        }

        FileResult? archivo;
        try
        {
            archivo = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Selecciona un documento",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".pdf", ".docx", ".txt", ".csv", ".xlsx", ".pptx", ".md", ".log", ".json" } },
                    { DevicePlatform.Android, new[] { "application/pdf", "text/plain", "text/csv", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/vnd.openxmlformats-officedocument.presentationml.presentation" } },
                    { DevicePlatform.iOS, new[] { "com.adobe.pdf", "public.plain-text", "public.comma-separated-values-text", "com.microsoft.excel.xlsx", "org.openxmlformats.presentationml.presentation" } },
                    { DevicePlatform.MacCatalyst, new[] { "com.adobe.pdf", "public.plain-text", "public.comma-separated-values-text" } },
                })
            });
        }
        catch (Exception ex)
        {
            await MostrarAsync("Error", ex.Message);
            return;
        }

        if (archivo is null)
            return;

        _trabajando = true;
        BtnSubir.IsBusy = true;
        Spinner.IsRunning = Spinner.IsVisible = true;
        IconoNube.IsVisible = false;

        try
        {
            var extension = Path.GetExtension(archivo.FileName).TrimStart('.').ToLowerInvariant();
            var tipo = extension switch
            {
                "doc" or "docx" or "docm" => "docx",
                "xlsx" or "xlsm" or "xls" => "xlsx",
                "pptx" or "ppt" => "pptx",
                "csv" => "csv",
                "md" => "md",
                "log" => "log",
                "json" => "json",
                "pdf" => "pdf",
                _ => "txt",
            };

            long tamano;
            byte[] bytes;
            using (var stream = await archivo.OpenReadAsync())
            {
                tamano = stream.Length;
                if (tamano > MaxBytes)
                {
                    await MostrarAsync("Aviso", "El archivo supera el límite de 20 MB.");
                    return;
                }

                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                bytes = ms.ToArray();
            }

            // Extrae el texto (necesario para que la IA pueda generar cuestionarios).
            var texto = await DocumentExtractor.ExtraerAsync(bytes, tipo);

            var ruta = $"{usuario.Id}/{Guid.NewGuid()}{Path.GetExtension(archivo.FileName)}";
            var doc = new Documento
            {
                UsuarioId = usuario.Id,
                Nombre = archivo.FileName,
                Tipo = tipo,
                TamanoBytes = tamano,
                RutaStorage = ruta,
                Texto = texto,
                Estado = "subido"
            };

            // 1) Guarda siempre en SQLite (funciona sin internet)
            var conn = await LocalDatabase.GetAsync();
            await conn.InsertAsync(doc);

            // 2) Sube archivo + registro a Supabase (si hay conexión)
            var client = await SupabaseService.GetClientAsync();
            if (client is not null)
            {
                try
                {
                    await client.Storage.From("documentos").Upload(
                        bytes,
                        ruta,
                        new Supabase.Storage.FileOptions { ContentType = archivo.ContentType, Upsert = true });

                    await client.From<Documento>().Insert(doc);
                }
                catch
                {
                    // Sin conexión: el documento queda local y se sincroniza después
                }
            }

            await CargarDocumentosAsync();

            if (string.IsNullOrWhiteSpace(texto) && tipo == "pdf")
                await MostrarAsync("Aviso", "El PDF no tiene texto seleccionable (posible escaneo). La IA podría no poder analizarlo.");
        }
        catch (Exception ex)
        {
            await MostrarAsync("Error", "No se pudo subir el documento: " + ex.Message);
        }
        finally
        {
            _trabajando = false;
            BtnSubir.IsBusy = false;
            Spinner.IsRunning = Spinner.IsVisible = false;
            IconoNube.IsVisible = true;
        }
    }

    async void Analizar_Click(object? sender, EventArgs e) => await AnalizarConTamanoAsync((sender as Button)?.CommandParameter as string);

    async Task AnalizarConTamanoAsync(string? id)
    {
        if (id is null)
            return;

        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null)
            return;

        // El usuario elige el tamaño del resumen.
        var tamano = await page.DisplayActionSheetAsync(
            "¿De qué tamaño quieres el resumen?", "Cancelar", null,
            "Corto (≈60 palabras)", "Medio (≈120)", "Detallado (≈350)", "Extenso (≈600)");
        if (tamano is null || tamano == "Cancelar")
            return;
        var claveTamano = tamano.Split(' ')[0];

        var conn = await LocalDatabase.GetAsync();
        var doc = await conn.Table<Documento>().Where(d => d.Id == id).FirstOrDefaultAsync();
        if (doc is null)
            return;

        doc.Estado = "analizando";
        await conn.UpdateAsync(doc);
        await CargarDocumentosAsync();

        try
        {
            var (resumen, ia) = await DocumentoAnalysisService.AnalizarAsync(doc, claveTamano);
            await CargarDocumentosAsync();
            await MostrarAsync("Análisis completo", $"Resumen ({claveTamano}) generado con {ia}:\n\n{resumen}");
        }
        catch (Exception ex)
        {
            doc.Estado = "error";
            await conn.UpdateAsync(doc);
            await CargarDocumentosAsync();
            await MostrarAsync("No se pudo analizar", ex.Message);
        }
    }

    async void CrearCuestionario_Click(object? sender, EventArgs e) =>
        await CrearCuestionarioAsync((sender as Button)?.CommandParameter as string);

    async Task CrearCuestionarioAsync(string? id)
    {
        if (id is null)
            return;

        var conn = await LocalDatabase.GetAsync();
        var doc = await conn.Table<Documento>().Where(d => d.Id == id).FirstOrDefaultAsync();
        if (doc is null || string.IsNullOrWhiteSpace(doc.Texto))
        {
            await MostrarAsync("Aviso", doc is null
                ? "Documento no encontrado."
                : "El documento no tiene texto extraíble para crear un cuestionario.");
            return;
        }

        var usuario = AuthService.UsuarioActual;
        if (usuario is null)
            return;

        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null)
            return;

        var confirmar = await page.DisplayAlertAsync("Crear cuestionario",
            $"¿Generar un cuestionario con IA a partir de «{doc.Nombre}»?", "Sí", "No");
        if (!confirmar)
            return;

        // El usuario elige cuántas preguntas quiere.
        var eleccion = await page.DisplayActionSheetAsync("¿Cuántas preguntas?", "Cancelar", null, "5", "10", "15", "20");
        if (eleccion is null || eleccion == "Cancelar" || !int.TryParse(eleccion, out var cantidad))
            return;

        try
        {
            var (quiz, ia) = await QuizGenerator.GenerarAsync(
                usuario.Id, doc.Nombre, $"Documento: {doc.Nombre}", doc.Texto, doc.Id, cantidad);

            await page.DisplayAlertAsync("Cuestionario listo",
                $"Se creó «{quiz.Titulo}» con {quiz.TotalPreguntas} preguntas usando {ia}.\n\nVe a la sección Cuestionarios para responderlo.", "OK");
        }
        catch (Exception ex)
        {
            await page.DisplayAlertAsync("No se pudo crear", ex.Message, "OK");
        }
    }

    async void CrearDiagrama_Click(object? sender, EventArgs e) =>
        await CrearDiagramaDesdeAsync((sender as Button)?.CommandParameter as string);

    async Task CrearDiagramaDesdeAsync(string? id)
    {
        if (id is null)
            return;

        // Navega a la sección Diagramas con este documento ya seleccionado.
        if (MainPage.Actual is not null)
            await MainPage.Actual.IrADiagramasAsync(id);
    }

    // ---------------- VISOR DE CONTENIDO ----------------

    string? _visorId;

    async void Ver_Click(object? sender, EventArgs e) =>
        await AbrirVisorAsync((sender as Button)?.CommandParameter as string);

    async Task AbrirVisorAsync(string? id)
    {
        if (id is null)
            return;

        var conn = await LocalDatabase.GetAsync();
        var doc = await conn.Table<Documento>().Where(d => d.Id == id).FirstOrDefaultAsync();
        if (doc is null)
            return;

        if (string.IsNullOrWhiteSpace(doc.Texto))
        {
            await MostrarAsync("Sin contenido",
                "Este archivo no tiene texto extraíble (por ejemplo, un PDF escaneado o una hoja de cálculo con fórmulas sin valores).");
            return;
        }

        _visorId = id;
        TituloVisor.Text = doc.Nombre;
        MetaVisor.Text = $"{doc.Tipo.ToUpperInvariant()} · {DocumentoItem.FormatearTamano(doc.TamanoBytes)} · {doc.Texto.Length} caracteres";

        // Si el tipo se puede mostrar "tal como es", descarga el archivo original
        // y lo renderiza a HTML con estructura e imágenes incrustadas.
        var mostradoFiel = false;
        if (DocumentoHtmlService.EsVisualizable(doc.Tipo))
        {
            try
            {
                var client = await SupabaseService.GetClientAsync();
                if (client is not null && !string.IsNullOrWhiteSpace(doc.RutaStorage))
                {
                    var bytes = await client.Storage.From("documentos")
                        .Download(doc.RutaStorage, (Supabase.Storage.TransformOptions?)null, null);
                    var html = DocumentoHtmlService.GenerarHtml(doc, bytes);
                    if (!string.IsNullOrWhiteSpace(html))
                    {
                        VisorWeb.Source = new HtmlWebViewSource { Html = html };
                        VisorWeb.IsVisible = true;
                        VisorTextoScroll.IsVisible = false;
                        mostradoFiel = true;
                    }
                }
            }
            catch
            {
                // sin conexión o error: usa el texto extraído
            }
        }

        if (!mostradoFiel)
        {
            TextoVisor.Text = doc.Texto;
            VisorWeb.IsVisible = false;
            VisorTextoScroll.IsVisible = true;
        }

        PanelVisor.IsVisible = true;
    }

    void CerrarVisor_Click(object? sender, EventArgs e)
    {
        PanelVisor.IsVisible = false;
        _visorId = null;
    }

    async void VisorResumen_Click(object? sender, EventArgs e) => await AnalizarConTamanoAsync(_visorId);

    async void VisorCuestionario_Click(object? sender, EventArgs e)
    {
        await CrearCuestionarioAsync(_visorId);
    }

    async void VisorDiagrama_Click(object? sender, EventArgs e)
    {
        var id = _visorId;
        if (id is null)
            return;

        CerrarVisor_Click(sender, e);
        await CrearDiagramaDesdeAsync(id);
    }

    async void Eliminar_Click(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not string id)
            return;

        if (_trabajando)
            return;

        var conn = await LocalDatabase.GetAsync();
        var doc = await conn.Table<Documento>().Where(d => d.Id == id).FirstOrDefaultAsync();
        if (doc is null)
            return;

        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null)
            return;

        var confirmar = await page.DisplayAlertAsync("Eliminar documento",
            $"¿Borrar «{doc.Nombre}»? Esta acción no se puede deshacer.", "Sí", "No");
        if (!confirmar)
            return;

        _trabajando = true;
        try
        {
            // 1) Registra el borrado para aplicarlo en la nube al sincronizar
            await SyncService.MarcarBorradoAsync("documentos", doc.Id);

            // 2) Borra local
            await conn.DeleteAsync(doc);

            // 2) Borra en Supabase (archivo + registro), si hay conexión
            var client = await SupabaseService.GetClientAsync();
            if (client is not null)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(doc.RutaStorage))
                        await client.Storage.From("documentos").Remove(doc.RutaStorage);

                    await client.From<Documento>().Where(d => d.Id == id).Delete();
                }
                catch
                {
                    // Sin conexión: queda borrado localmente
                }
            }

            await CargarDocumentosAsync();
        }
        catch (Exception ex)
        {
            await MostrarAsync("Error", "No se pudo eliminar el documento: " + ex.Message);
        }
        finally
        {
            _trabajando = false;
        }
    }

    Task MostrarAsync(string titulo, string mensaje)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        return page is null ? Task.CompletedTask : page.DisplayAlertAsync(titulo, mensaje, "OK");
    }
}

public class DocumentoItem
{
    public string Id { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string TamanoTexto { get; set; } = string.Empty;
    public string TipoEtiqueta { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public string Resumen { get; set; } = string.Empty;
    public bool TieneResumen { get; set; }
    public bool Listo { get; set; }
    public bool PuedeAnalizar { get; set; }
    public bool PuedeVer { get; set; }
    public string TextoBotonAnalizar { get; set; } = "Analizar con IA";
    public Color ColorTexto { get; set; } = Colors.White;
    public Color ColorFondo { get; set; } = Colors.Transparent;

    public DocumentoItem() { }

    public DocumentoItem(Documento d)
    {
        Id = d.Id;
        Nombre = d.Nombre;
        TamanoTexto = FormatearTamano(d.TamanoBytes);
        TipoEtiqueta = d.Tipo.ToUpperInvariant();

        Listo = d.Estado == "listo";
        PuedeAnalizar = !Listo && d.Estado != "analizando";
        TextoBotonAnalizar = d.Estado switch
        {
            "listo" => "Re-analizar con IA",
            "analizando" => "Analizando...",
            _ => "Analizar con IA"
        };

        Estado = d.Estado switch
        {
            "listo" => "Analizado ✓",
            "analizando" => "Analizando...",
            "error" => "Error al analizar",
            _ => "Pendiente de análisis"
        };

        Resumen = d.Resumen;
        TieneResumen = !string.IsNullOrWhiteSpace(d.Resumen);
        PuedeVer = !string.IsNullOrWhiteSpace(d.Texto);

        (ColorTexto, ColorFondo) = d.Tipo switch
        {
            "pdf" => (Color.FromArgb("#FB7185"), Color.FromArgb("#3F1D2B")),
            "docx" => (Color.FromArgb("#60A5FA"), Color.FromArgb("#14284F")),
            "xlsx" => (Color.FromArgb("#34D399"), Color.FromArgb("#0F3D2E")),
            "pptx" => (Color.FromArgb("#FBBF24"), Color.FromArgb("#3D2E0F")),
            _ => (Color.FromArgb("#34D399"), Color.FromArgb("#0F3D2E")), // txt, csv, md, log, json
        };
    }

    internal static string FormatearTamano(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):0.#} MB",
        >= 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} B"
    };
}
