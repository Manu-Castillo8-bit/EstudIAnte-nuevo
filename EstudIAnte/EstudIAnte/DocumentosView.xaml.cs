using EstudIAnte.Models;
using EstudIAnte.Services;

namespace EstudIAnte;

public partial class DocumentosView : ContentView
{
    const long MaxBytes = 20 * 1024 * 1024;
    bool _trabajando;
    CancellationTokenSource? _analisisCts;
    string? _detalleId;
    bool? _compactLayout;
    readonly Dictionary<string, byte[]> _archivosExternos = new();
    public event EventHandler<bool>? LecturaCompletaChanged;

    public DocumentosView()
    {
        InitializeComponent();
        Loaded += (_, _) => _ = CargarDocumentosAsync();
        AgregarPinchZoom();
    }

    double _pinchBaseZoom = 1;
    void AgregarPinchZoom()
    {
        void adjuntarPinch(View vista)
        {
            var pinch = new PinchGestureRecognizer();
            pinch.PinchUpdated += (_, e) =>
            {
                if (e.Status == GestureStatus.Started)
                    _pinchBaseZoom = _zoomLectura;
                else if (e.Status == GestureStatus.Running)
                {
                    var nuevo = Math.Clamp(Math.Round(_pinchBaseZoom * e.Scale, 1), 0.6, 2.5);
                    if (Math.Abs(nuevo - _zoomLectura) > 0.0001)
                    {
                        _zoomLectura = nuevo;
                        ZoomLectura.Text = $"{_zoomLectura:P0}";
                        TextoLectura.FontSize = 15 * _zoomLectura;
                        _ = AplicarZoomWebAsync(LecturaWeb);
                    }
                }
            };
            vista.GestureRecognizers.Add(pinch);
        }

        // Se aplica al lector de pantalla completa y al visor.
        foreach (var vista in new View[] { PanelLectura, PanelVisor })
            adjuntarPinch(vista);
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0)
            return;

        var compact = width < 760;
        BtnSubir.Text = width < 420 ? "＋ Subir" : "＋ Subir documento";
        BtnSubir.HorizontalOptions = compact ? LayoutOptions.Fill : LayoutOptions.End;
        if (_compactLayout == compact)
            return;

        _compactLayout = compact;
        DocumentsLayout.Padding = compact
            ? new Thickness(14, 8, 14, 14)
            : new Thickness(28, 8, 28, 28);
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

    async void SincronizarDocs_Click(object? sender, EventArgs e)
    {
        try
        {
            BtnSincronizarDocs.IsBusy = true;
            BtnSincronizarDocs.Text = "🔄 Sincronizando...";
            var (ok, msg) = await SyncService.SincronizarTodoAsync();
            BtnSincronizarDocs.Text = (ok ? "✅" : "⚠") + " Sincronizar";
            await CargarDocumentosAsync();
            await MostrarAsync("Sincronización", msg);
        }
        finally
        {
            BtnSincronizarDocs.IsBusy = false;
        }
    }

    /// <summary>Abre el lector de pantalla completa de un documento por Id.</summary>
    public async Task AbrirLecturaPorIdAsync(string? id) => await AbrirLecturaAsync(id);

    public async Task AbrirArchivoExternoAsync(string ruta, string nombre)
    {
        if (AuthService.UsuarioActual is not { } usuario)
        {
            await MostrarAsync("Inicia sesión", "Inicia sesión para abrir este documento en EstudIAnte.");
            return;
        }

        string? rutaPersistente = null;
        var guardadoLocal = false;
        try
        {
            var bytes = await File.ReadAllBytesAsync(ruta);
            if (bytes.Length > MaxBytes)
            {
                await MostrarAsync("Archivo demasiado grande", "El archivo supera el límite de 20 MB.");
                return;
            }

            var doc = new Documento
            {
                UsuarioId = usuario.Id,
                Nombre = nombre,
                Tipo = "pdf",
                TamanoBytes = bytes.Length,
                RutaStorage = null,
                Texto = await DocumentExtractor.ExtraerAsync(bytes, "pdf"),
                Estado = "subido"
            };

            var conn = await LocalDatabase.GetAsync();
            rutaPersistente = RutaDocumentoExterno(doc.Id);
            Directory.CreateDirectory(Path.GetDirectoryName(rutaPersistente)!);
            await File.WriteAllBytesAsync(rutaPersistente, bytes);
            await conn.InsertAsync(doc);
            _archivosExternos[doc.Id] = bytes;
            guardadoLocal = true;

            // El documento externo se lee localmente sin subirse a la nube:
            // no se consume almacenamiento remoto ni se sincroniza.
            // (Solo se guarda la copia local temporal para el lector.)

            await CargarDocumentosAsync();
            await AbrirLecturaAsync(doc.Id, bytes);
        }
        catch (Exception ex)
        {
            if (!guardadoLocal && rutaPersistente is not null && File.Exists(rutaPersistente))
            {
                try
                {
                    File.Delete(rutaPersistente);
                }
                catch (Exception cleanupError)
                {
                    Diag("No se pudo limpiar un archivo externo incompleto: " + cleanupError.Message);
                }
            }
            await MostrarAsync("No se pudo abrir el documento", ex.Message);
        }
        finally
        {
            try
            {
                File.Delete(ruta);
            }
            catch (Exception ex)
            {
                Diag("No se pudo limpiar el archivo temporal externo: " + ex.Message);
            }
        }
    }

    async void Detalle_Click(object? sender, TappedEventArgs e)
    {
        var id = (sender as TapGestureRecognizer)?.CommandParameter as string;
        if (id is null)
            return;

        var conn = await LocalDatabase.GetAsync();
        var doc = await conn.Table<Documento>().Where(d => d.Id == id).FirstOrDefaultAsync();
        if (doc is null)
            return;

        var item = new DocumentoItem(doc);
        _detalleId = id;
        DetalleIcono.Text = item.TipoEtiqueta;
        DetalleIcono.TextColor = item.ColorTexto;
        DetalleIconoBorder.BackgroundColor = item.ColorFondo;
        DetalleNombre.Text = doc.Nombre;
        DetalleMeta.Text = $"{item.TipoEtiqueta} · {item.TamanoTexto}";
        DetalleEstado.Text = item.Estado;
        DetalleResumen.Text = string.IsNullOrWhiteSpace(doc.Resumen)
            ? "Aún no hay un resumen. Analiza el documento para generarlo."
            : doc.Resumen;
        DetalleTexto.Text = doc.Texto;
        DetalleTextoScroll.IsVisible = !string.IsNullOrWhiteSpace(doc.Texto);
        DetalleWeb.IsVisible = false;
        BtnDetalleAnalizar.Text = item.TextoBotonAnalizar;
        BtnDetalleAnalizar.IsEnabled = item.PuedeAnalizar;
        BtnDetalleCuestionario.IsEnabled = !string.IsNullOrWhiteSpace(doc.Texto);
        BtnDetalleDiagrama.IsEnabled = !string.IsNullOrWhiteSpace(doc.Texto);
        PanelDetalle.IsVisible = true;
    }

    void CerrarDetalle_Click(object? sender, EventArgs e)
    {
        PanelDetalle.IsVisible = false;
        _detalleId = null;
    }

    async void DetalleVer_Click(object? sender, EventArgs e)
    {
        var id = _detalleId;
        if (id is null)
            return;

        PanelDetalle.IsVisible = false;
        await AbrirVisorAsync(id);
    }

    async void DetalleSoloLeer_Click(object? sender, EventArgs e) =>
        await AbrirLecturaAsync(_detalleId);

    async void SoloLeer_Click(object? sender, EventArgs e) =>
        await AbrirLecturaAsync((sender as Button)?.CommandParameter as string);

    async void DetalleAnalizar_Click(object? sender, EventArgs e) =>
        await AnalizarConTamanoAsync(_detalleId);

    async void DetalleCuestionario_Click(object? sender, EventArgs e) =>
        await CrearCuestionarioAsync(_detalleId);

    async void DetalleDiagrama_Click(object? sender, EventArgs e) =>
        await CrearDiagramaDesdeAsync(_detalleId);

    async void DetalleEliminar_Click(object? sender, EventArgs e)
    {
        var id = _detalleId;
        if (id is null)
            return;

        if (await EliminarDocumentoAsync(id))
            CerrarDetalle_Click(sender, e);
    }

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
        ActualizarDetalle();

        _analisisCts = new CancellationTokenSource();
        PanelCargando.IsVisible = true;
        SpinnerAnalisis.IsRunning = true;
        try
        {
            var (resumen, ia) = await DocumentoAnalysisService.AnalizarAsync(
                doc, claveTamano, _analisisCts.Token);
            await CargarDocumentosAsync();
            ActualizarDetalle();
            await MostrarAsync("Análisis completo", $"Resumen ({claveTamano}) generado con {ia}:\n\n{resumen}");
        }
        catch (OperationCanceledException) when (_analisisCts?.IsCancellationRequested == true)
        {
            doc.Estado = "subido";
            await conn.UpdateAsync(doc);
            await CargarDocumentosAsync();
            ActualizarDetalle();
        }
        catch (Exception ex)
        {
            doc.Estado = "error";
            await conn.UpdateAsync(doc);
            await CargarDocumentosAsync();
            ActualizarDetalle();
            await MostrarAsync("No se pudo analizar", ex.Message);
        }
        finally
        {
            PanelCargando.IsVisible = false;
            SpinnerAnalisis.IsRunning = false;
            _analisisCts?.Dispose();
            _analisisCts = null;
        }
    }

    void CancelarAnalisis_Click(object? sender, EventArgs e) => _analisisCts?.Cancel();

    void ActualizarDetalle()
    {
        if (_detalleId is null)
            return;

        var doc = _docs.FirstOrDefault(d => d.Id == _detalleId);
        if (doc is null)
            return;

        var item = new DocumentoItem(doc);
        DetalleEstado.Text = item.Estado;
        DetalleResumen.Text = string.IsNullOrWhiteSpace(doc.Resumen)
            ? "Aún no hay un resumen. Analiza el documento para generarlo."
            : doc.Resumen;
        BtnDetalleAnalizar.Text = item.TextoBotonAnalizar;
        BtnDetalleAnalizar.IsEnabled = item.PuedeAnalizar;
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
    double _zoomLectura = 1;

    async void Ver_Click(object? sender, EventArgs e) =>
        await AbrirVisorAsync((sender as Button)?.CommandParameter as string);

    async Task AbrirVisorAsync(string? id)
    {
        if (id is null)
            return;

        var conn = await LocalDatabase.GetAsync();
        var doc = await conn.Table<Documento>().Where(d => d.Id == id).FirstOrDefaultAsync();
        if (doc is null)
        {
            Diag($"Ver: doc id={id} NO ENCONTRADO");
            return;
        }

        Diag($"Ver: doc={doc.Nombre} tipo={doc.Tipo} texto={doc.Texto?.Length} ruta={(doc.RutaStorage ?? "(vacía)")}");

        // Solo se bloquea de entrada si NO hay texto y el tipo tampoco es visualizable.
        if (string.IsNullOrWhiteSpace(doc.Texto) && !DocumentoHtmlService.EsVisualizable(doc.Tipo))
        {
            await MostrarAsync("Sin contenido",
                "Este archivo no tiene texto extraíble (por ejemplo, un PDF escaneado o una hoja de cálculo con fórmulas sin valores).");
            return;
        }
        if (string.IsNullOrWhiteSpace(doc.Texto))
            Diag("texto vacío pero tipo visualizable → intentando render fiel");

        _visorId = id;
        TituloVisor.Text = doc.Nombre;
        MetaVisor.Text = $"{doc.Tipo.ToUpperInvariant()} · {DocumentoItem.FormatearTamano(doc.TamanoBytes)} · {(doc.Texto?.Length ?? 0)} caracteres";

        byte[]? bytes = null;
        if (_archivosExternos.TryGetValue(id, out var b))
            bytes = b;
        else
        {
            var rutaLocal = RutaDocumentoExterno(id);
            if (File.Exists(rutaLocal))
                bytes = await File.ReadAllBytesAsync(rutaLocal);
        }

        await CargarContenidoAsync(doc, VisorWeb, VisorTextoScroll, TextoVisor, bytes);

        PanelVisor.IsVisible = true;
    }

    async Task AbrirLecturaAsync(string? id, byte[]? bytesOriginales = null)
    {
        if (id is null)
            return;

        var conn = await LocalDatabase.GetAsync();
        var doc = await conn.Table<Documento>().Where(d => d.Id == id).FirstOrDefaultAsync();
        if (doc is null)
            return;

        if (string.IsNullOrWhiteSpace(doc.Texto) && !DocumentoHtmlService.EsVisualizable(doc.Tipo))
        {
            await MostrarAsync("Sin contenido",
                "Este archivo no tiene texto extraíble ni puede mostrarse en el lector.");
            return;
        }

        _visorId = id;
        _zoomLectura = 1;
        ZoomLectura.Text = "100%";
        TextoLectura.FontSize = 15;
        OpcionesLectura.IsVisible = true;
        TituloLectura.Text = doc.Nombre;
        MetaLectura.Text = $"{doc.Tipo.ToUpperInvariant()} · {DocumentoItem.FormatearTamano(doc.TamanoBytes)}";
        if (bytesOriginales is null && !_archivosExternos.TryGetValue(id, out bytesOriginales))
        {
            var rutaLocal = RutaDocumentoExterno(id);
            if (File.Exists(rutaLocal))
                bytesOriginales = await File.ReadAllBytesAsync(rutaLocal);
        }
        await CargarContenidoAsync(doc, LecturaWeb, LecturaTextoScroll, TextoLectura,
            bytesOriginales);
        PanelLectura.IsVisible = true;
        LecturaCompletaChanged?.Invoke(this, true);
    }

    async Task CargarContenidoAsync(Documento doc, WebView web, ScrollView textoScroll, Label texto,
        byte[]? bytesOriginales = null)
    {
        var mostradoFiel = false;
        if (bytesOriginales is { Length: > 0 } && DocumentoHtmlService.EsVisualizable(doc.Tipo))
        {
            var htmlOriginal = DocumentoHtmlService.GenerarHtml(doc, bytesOriginales);
            if (!string.IsNullOrWhiteSpace(htmlOriginal))
            {
                await MostrarHtmlAsync(web, htmlOriginal);
                web.IsVisible = true;
                textoScroll.IsVisible = false;
                mostradoFiel = true;
            }
        }

        if (DocumentoHtmlService.EsVisualizable(doc.Tipo) &&
            !mostradoFiel && !string.IsNullOrWhiteSpace(doc.RutaStorage))
        {
            try
            {
                var client = await SupabaseService.GetClientAsync();
                if (client is null)
                    Diag("visor: sin conexión a Supabase (client null)");
                else
                {
                    var bytes = await client.Storage.From("documentos")
                        .Download(doc.RutaStorage, (Supabase.Storage.TransformOptions?)null, null);
                    var html = DocumentoHtmlService.GenerarHtml(doc, bytes);
                    if (!string.IsNullOrWhiteSpace(html))
                    {
                        await MostrarHtmlAsync(web, html);
                        web.IsVisible = true;
                        textoScroll.IsVisible = false;
                        mostradoFiel = true;
                    }
                }
            }
            catch (Exception exVisor)
            {
                Diag("ERROR visor: " + exVisor.Message);
            }
        }

        static async Task MostrarHtmlAsync(WebView web, string html)
        {
            if (html.Length > 1_500_000)
            {
                var carpeta = Path.Combine(FileSystem.CacheDirectory, "visores");
                Directory.CreateDirectory(carpeta);
                var ruta = Path.Combine(carpeta, $"{Guid.NewGuid()}.html");
                await File.WriteAllTextAsync(ruta, html);
                web.Source = new UrlWebViewSource
                {
                    Url = "file:///" + ruta.Replace('\\', '/').TrimStart('/'),
                };
            }
            else
            {
                web.Source = new HtmlWebViewSource { Html = html };
            }
        }

        if (!mostradoFiel)
        {
            texto.Text = doc.Texto;
            web.IsVisible = false;
            textoScroll.IsVisible = true;
        }
    }

    /// <summary>Log de diagnóstico del visor en %LOCALAPPDATA%\EstudIAnte\visor.log.</summary>
    static void Diag(string mensaje)
    {
        try
        {
            var carpeta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EstudIAnte");
            Directory.CreateDirectory(carpeta);
            File.AppendAllText(Path.Combine(carpeta, "visor.log"),
                $"[{DateTime.Now:HH:mm:ss}] {mensaje}\n");
        }
        catch { }
    }

    void CerrarVisor_Click(object? sender, EventArgs e)
    {
        PanelVisor.IsVisible = false;
        _visorId = null;
    }

    void CerrarLectura_Click(object? sender, EventArgs e)
    {
        PanelLectura.IsVisible = false;
        if (_visorId is not null)
            _archivosExternos.Remove(_visorId);
        _visorId = null;
        LecturaCompletaChanged?.Invoke(this, false);
    }

    void AlternarOpcionesLectura_Click(object? sender, EventArgs e) =>
        OpcionesLectura.IsVisible = !OpcionesLectura.IsVisible;

    void ReducirZoom_Click(object? sender, EventArgs e) => CambiarZoom(-0.1);

    void AumentarZoom_Click(object? sender, EventArgs e) => CambiarZoom(0.1);

    void CambiarZoom(double cambio)
    {
        _zoomLectura = Math.Clamp(Math.Round(_zoomLectura + cambio, 1), 0.6, 2.5);
        ZoomLectura.Text = $"{_zoomLectura:P0}";
        TextoLectura.FontSize = 15 * _zoomLectura;
        _ = AplicarZoomWebAsync(LecturaWeb);
    }

    async void LecturaWeb_Navigated(object? sender, WebNavigatedEventArgs e) =>
        await AplicarZoomWebAsync(LecturaWeb);

    async Task AplicarZoomWebAsync(WebView web)
    {
        if (!web.IsVisible)
            return;

        try
        {
            await web.EvaluateJavaScriptAsync($"document.body.style.zoom='{_zoomLectura.ToString(System.Globalization.CultureInfo.InvariantCulture)}'");
        }
        catch (Exception ex)
        {
            Diag("No se pudo aplicar el zoom del lector: " + ex.Message);
        }
    }

    async void VisorResumen_Click(object? sender, EventArgs e) => await AnalizarConTamanoAsync(_visorId);

    async void LecturaResumen_Click(object? sender, EventArgs e)
    {
        if (_visorId is null)
            return;

        var conn = await LocalDatabase.GetAsync();
        var doc = await conn.Table<Documento>().Where(d => d.Id == _visorId).FirstOrDefaultAsync();
        if (doc is not null)
            await MostrarAsync("Resumen", string.IsNullOrWhiteSpace(doc.Resumen)
                ? "Todavía no hay un resumen. Usa «Analizar documento» para generarlo."
                : doc.Resumen);
    }

    async void VisorCuestionario_Click(object? sender, EventArgs e)
    {
        await CrearCuestionarioAsync(_visorId);
    }

    async void VisorDiagrama_Click(object? sender, EventArgs e)
    {
        var id = _visorId;
        if (id is null)
            return;

        PanelVisor.IsVisible = false;
        PanelLectura.IsVisible = false;
        _archivosExternos.Remove(id);
        _visorId = null;
        LecturaCompletaChanged?.Invoke(this, false);
        await CrearDiagramaDesdeAsync(id);
    }

    async void Eliminar_Click(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not string id)
            return;

        if (_trabajando)
            return;

        await EliminarDocumentoAsync(id);
    }

    async Task<bool> EliminarDocumentoAsync(string id)
    {
        var conn = await LocalDatabase.GetAsync();
        var doc = await conn.Table<Documento>().Where(d => d.Id == id).FirstOrDefaultAsync();
        if (doc is null)
            return false;

        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null)
            return false;

        var confirmar = await page.DisplayAlertAsync("Eliminar documento",
            $"¿Borrar «{doc.Nombre}»? Esta acción no se puede deshacer.", "Sí", "No");
        if (!confirmar)
            return false;

        _trabajando = true;
        try
        {
            // 1) Registra el borrado para aplicarlo en la nube al sincronizar
            await SyncService.MarcarBorradoAsync("documentos", doc.Id);

            // 2) Borra local
            await conn.DeleteAsync(doc);
            _archivosExternos.Remove(id);
            var rutaExterna = RutaDocumentoExterno(id);
            if (File.Exists(rutaExterna))
            {
                try
                {
                    File.Delete(rutaExterna);
                }
                catch (Exception ex)
                {
                    Diag("No se pudo eliminar la copia local del PDF: " + ex.Message);
                }
            }

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
            return true;
        }
        catch (Exception ex)
        {
            await MostrarAsync("Error", "No se pudo eliminar el documento: " + ex.Message);
            return false;
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

    static string RutaDocumentoExterno(string id) =>
        Path.Combine(FileSystem.AppDataDirectory, "DocumentosExternos", $"{id}.pdf");
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
    public bool PuedeCrearCuestionario { get; set; }
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
        PuedeAnalizar = d.Estado != "analizando";
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
        PuedeCrearCuestionario = !string.IsNullOrWhiteSpace(d.Texto);
        // El botón "Ver contenido" aparece si hay texto extraído O si el tipo
        // se puede renderizar fielmente (los Word/PowerPoint de solo imágenes
        // tienen Texto vacío pero el visor los muestra igual con imágenes).
        PuedeVer = !string.IsNullOrWhiteSpace(d.Texto) ||
                   (DocumentoHtmlService.EsVisualizable(d.Tipo) && d.TamanoBytes > 0);

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
