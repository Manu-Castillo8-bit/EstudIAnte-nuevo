using EstudIAnte.Controls;
using EstudIAnte.Models;
using EstudIAnte.Services;

namespace EstudIAnte;

public partial class ChatView : ContentView
{
    // Tiempo máximo que se espera a un proveedor antes de pasar al siguiente.
    static readonly TimeSpan TimeoutIntento = TimeSpan.FromSeconds(40);

    Tema? _temaActual;
    Chat? _chatActual;
    bool _ocupado;
    bool? _compactLayout;
    CancellationTokenSource? _envioCts;      // cancela TODA la petición
    Task? _operacionActual;
    string? _ultimoMensajeUsuarioId;
    List<MensajeItem> _items = new();

    public ChatView()
    {
        InitializeComponent();
        Loaded += (_, _) => _ = RefrescarTemasAsync();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0)
            return;

        var compact = width < 760;
        if (_compactLayout == compact)
            return;

        _compactLayout = compact;
        ChatLayout.Padding = compact
            ? new Thickness(14, 8, 14, 18)
            : new Thickness(28, 8, 28, 24);

        // Al cambiar el tamaño, mantener el último mensaje visible.
        _ = ScrollAlFinalAsync();
    }

    /// <summary>Abre el chat de un tema por su nombre (lo usa el dashboard del Inicio).</summary>
    public async Task AbrirTemaPorNombreAsync(string nombre)
    {
        var temas = await TemasService.ListarAsync(AuthService.UsuarioActual?.Id ?? "");
        var tema = temas.FirstOrDefault(t => t.Nombre == nombre) ?? temas.FirstOrDefault();
        if (tema is not null)
            await SeleccionarTemaAsync(tema);
    }

    // ---------------- TEMAS ----------------

    async Task RefrescarTemasAsync()
    {
        var usuario = AuthService.UsuarioActual;
        if (usuario is null)
            return;

        var temas = await TemasService.ListarAsync(usuario.Id);

        TemasPanel.Clear();
        if (temas.Count == 0)
        {
            TemasPanel.Add(new Label { Text = "Aún no tienes temas. Usa «Crear tema».", VerticalOptions = LayoutOptions.Center });
            AplicarEstiloMuted(TemasPanel);
            _temaActual = null;
            _chatActual = null;
            BtnEliminarTema.IsVisible = false;
            Subtitulo.Text = "Crea un tema para empezar una conversación";
            ListaMensajes.ItemsSource = null;
            return;
        }

        foreach (var tema in temas)
        {
            var boton = new PulsarButton
            {
                Text = tema.Nombre,
                Style = ChipStyle(),
                CommandParameter = tema,
                Margin = new Thickness(0, 0, 10, 8)
            };

            if (_temaActual is not null && _temaActual.Id == tema.Id)
            {
                boton.Background = Gradient();
                boton.TextColor = Color.FromArgb("#06121F");
                boton.FontAttributes = FontAttributes.Bold;
            }

            boton.Clicked += TemaChip_Click;
            TemasPanel.Add(boton);
        }

        // Si no hay tema seleccionado o el seleccionado se borró, tomar el primero.
        if (_temaActual is null || temas.All(t => t.Id != _temaActual.Id))
        {
            await SeleccionarTemaAsync(temas[0]);
        }
    }

    void AplicarEstiloMuted(Layout panel)
    {
        foreach (var hijo in panel.Children.OfType<Label>())
            hijo.TextColor = Color.FromArgb("#94A3B8");
    }

    async void TemaChip_Click(object? sender, EventArgs e)
    {
        try
        {
            if ((sender as PulsarButton)?.CommandParameter is Tema tema)
                await SeleccionarTemaAsync(tema);
        }
        catch (Exception ex)
        {
            await MostrarAsync("Error", "No se pudo abrir el tema: " + ex.Message);
        }
    }

    async Task SeleccionarTemaAsync(Tema tema)
    {
        _temaActual = tema;
        Subtitulo.Text = $"Tema: {tema.Nombre}";
        BtnEliminarTema.IsVisible = true;

        var usuario = AuthService.UsuarioActual!;
        var conn = await LocalDatabase.GetAsync();
        _chatActual = await conn.Table<Chat>()
            .Where(c => c.UsuarioId == usuario.Id && c.Tema == tema.Nombre)
            .OrderByDescending(c => c.CreadoEn)
            .FirstOrDefaultAsync();

        if (_chatActual is null)
        {
            _chatActual = new Chat { UsuarioId = usuario.Id, Tema = tema.Nombre, Titulo = tema.Nombre };
            await conn.InsertAsync(_chatActual);
        }

        await RefrescarResaltadoAsync(tema);
        await CargarMensajesAsync();
    }

    async Task RefrescarResaltadoAsync(Tema activo)
    {
        if (AuthService.UsuarioActual is null)
            return;

        var temas = await TemasService.ListarAsync(AuthService.UsuarioActual.Id);
        TemasPanel.Clear();
        foreach (var tema in temas)
        {
            var boton = new PulsarButton
            {
                Text = tema.Nombre,
                Style = ChipStyle(),
                CommandParameter = tema,
                Margin = new Thickness(0, 0, 10, 8)
            };
            if (tema.Id == activo.Id)
            {
                boton.Background = Gradient();
                boton.TextColor = Color.FromArgb("#06121F");
                boton.FontAttributes = FontAttributes.Bold;
            }
            boton.Clicked += TemaChip_Click;
            TemasPanel.Add(boton);
        }
    }

    async void CrearTema_Click(object? sender, EventArgs e)
    {
        var usuario = AuthService.UsuarioActual;
        if (usuario is null)
        {
            await MostrarAsync("Aviso", "Debes iniciar sesión para crear temas.");
            return;
        }

        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null)
            return;

        var nombre = await page.DisplayPromptAsync("Nuevo tema", "¿Sobre qué quieres conversar?", "Crear", "Cancelar");
        if (string.IsNullOrWhiteSpace(nombre))
            return;

        var tema = await TemasService.CrearAsync(usuario.Id, nombre.Trim());

        // Marca el tema como actual ANTES de refrescar, para que el flujo de
        // refresco no elija el primero y el nuevo tema quede abierto al instante.
        _temaActual = tema;
        await RefrescarTemasAsync();
        await SeleccionarTemaAsync(tema);
    }

    async void EliminarTema_Click(object? sender, EventArgs e)
    {
        if (_temaActual is null)
            return;

        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null)
            return;

        var ok = await page.DisplayAlertAsync("Eliminar tema",
            $"Se borrará el tema «{_temaActual.Nombre}» y toda su conversación. ¿Continuar?", "Sí", "No");
        if (!ok)
            return;

        await TemasService.EliminarAsync(_temaActual);
        _temaActual = null;
        _chatActual = null;
        await RefrescarTemasAsync();
    }

    // ---------------- MENSAJES ----------------

    async void Enviar_Click(object? sender, EventArgs e)
    {
        if (_ocupado)
            return;

        _operacionActual = EnviarMensajeAsync(null, null);
        await _operacionActual;
    }

    async Task EnviarMensajeAsync(string? editarId, string? textoEditado)
    {
        if (_ocupado)
            return;

        var texto = (textoEditado ?? MensajeEntry.Text)?.Trim();
        if (string.IsNullOrEmpty(texto))
            return;

        var usuario = AuthService.UsuarioActual;
        if (usuario is null)
        {
            await MostrarAsync("Aviso", "Debes iniciar sesión para usar el chat.");
            return;
        }

        if (_chatActual is null || _temaActual is null)
        {
            await MostrarAsync("Aviso", "Primero crea o elige un tema.");
            return;
        }

        if (editarId is null)
        {
            var mensajeUsuario = new Mensaje { ChatId = _chatActual.Id, Rol = "user", Contenido = texto };
            await GuardarMensajeAsync(mensajeUsuario);
            _ultimoMensajeUsuarioId = mensajeUsuario.Id;
        }
        else
        {
            var conn = await LocalDatabase.GetAsync();
            var mensajeUsuario = await conn.Table<Mensaje>().Where(m => m.Id == editarId).FirstOrDefaultAsync();
            if (mensajeUsuario is null)
                return;

            var respuestasAnteriores = await conn.Table<Mensaje>()
                .Where(m => m.ChatId == mensajeUsuario.ChatId && m.Rol == "assistant")
                .ToListAsync();
            respuestasAnteriores = respuestasAnteriores
                .Where(m => string.CompareOrdinal(m.CreadoEn, mensajeUsuario.CreadoEn) > 0)
                .ToList();
            foreach (var respuestaAnterior in respuestasAnteriores)
                await conn.DeleteAsync(respuestaAnterior);

            mensajeUsuario.Contenido = texto;
            mensajeUsuario.CreadoEn = DateTime.UtcNow.ToString("o");
            await conn.UpdateAsync(mensajeUsuario);
            var client = await SupabaseService.GetClientAsync();
            if (client is not null)
            {
                try
                {
                    foreach (var respuestaAnterior in respuestasAnteriores)
                        await client.From<Mensaje>().Where(m => m.Id == respuestaAnterior.Id).Delete();
                    await client.From<Mensaje>().Update(mensajeUsuario);
                }
                catch { /* se sincroniza después */ }
            }
            _ultimoMensajeUsuarioId = mensajeUsuario.Id;
        }

        MensajeEntry.Text = string.Empty;
        await CargarMensajesAsync();

        // Respuesta de la IA
        _ocupado = true;
        BtnEnviar.IsBusy = true;
        BtnCancelar.IsVisible = true;
        BtnEditarPregunta.IsVisible = true;
        SpinnerIA.IsRunning = SpinnerIA.IsVisible = true;
        _envioCts = new CancellationTokenSource();
        var subtituloOriginal = Subtitulo.Text;

        try
        {
            var historial = await ObtenerHistorialAsync();

            // Búsqueda web: la casilla, o detección automática de preguntas que piden datos recientes.
            var buscarWeb = BuscarActualizado.IsChecked || PideInformacionReciente(texto);
            var prompt = ConstruirPromptChat(_temaActual.Nombre, historial, buscarWeb);
            var promptSinWeb = buscarWeb
                ? ConstruirPromptChat(_temaActual.Nombre, historial, false)
                    + "\nNo tienes acceso a búsqueda web para esta respuesta; indica explícitamente que no puedes verificar información actual."
                : prompt;

            var (respuesta, proveedor) = await GenerarConConmutacionAsync(
                prompt, promptSinWeb, texto, buscarWeb, _envioCts.Token);
            _envioCts.Token.ThrowIfCancellationRequested();
            if (buscarWeb && !PuedeBuscarEnWeb(proveedor))
            {
                respuesta = "⚠ No se pudo consultar la web. Esta respuesta alternativa no está verificada en tiempo real.\n\n"
                    + respuesta;
            }

            await GuardarMensajeAsync(new Mensaje
            {
                ChatId = _chatActual.Id,
                Rol = "assistant",
                Contenido = $"{respuesta}\n\n— {proveedor}"
            });
            Subtitulo.Text = subtituloOriginal;
        }
        catch (OperationCanceledException) when (_envioCts?.IsCancellationRequested == true)
        {
            Subtitulo.Text = "Solicitud cancelada. Puedes editar la pregunta o enviarla de nuevo.";
        }
        catch (Exception ex)
        {
            await GuardarMensajeAsync(new Mensaje
            {
                ChatId = _chatActual.Id,
                Rol = "assistant",
                Contenido = "⚠ " + ex.Message
            });
            Subtitulo.Text = subtituloOriginal;
        }
        finally
        {
            _ocupado = false;
            BtnEnviar.IsBusy = false;
            BtnCancelar.IsVisible = false;
            BtnEditarPregunta.IsVisible = _ultimoMensajeUsuarioId is not null;
            SpinnerIA.IsRunning = SpinnerIA.IsVisible = false;
            _envioCts?.Dispose();
            _envioCts = null;
            await CargarMensajesAsync();
        }
    }

    /// <summary>
    /// Prueba todos los proveedores disponibles antes de ofrecer cambiar a uno manualmente.
    /// Cancelar aborta toda la operación.
    /// </summary>
    async Task<(string, string)> GenerarConConmutacionAsync(
        string prompt, string promptSinWeb, string preguntaUsuario, bool buscarWeb, CancellationToken ct)
    {
        var proveedores = AIProviderManager.Proveedores().Where(p => p.Configurado).ToList();
        if (proveedores.Count == 0)
            throw new IAException("No hay ninguna IA configurada.");

        if (buscarWeb)
            proveedores = proveedores.OrderBy(p => p is SupabaseExaProvider ? 0 : 1).ToList();

        var errores = new List<string>();
        var pendientes = proveedores;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            foreach (var proveedor in pendientes)
            {
                ct.ThrowIfCancellationRequested();
                var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeoutIntento);

                try
                {
                    var esExa = proveedor is SupabaseExaProvider;
                    var promptProveedor = esExa
                        ? preguntaUsuario
                        : buscarWeb ? promptSinWeb : prompt;
                    var tarea = AIProviderManager.GenerarConProveedorAsync(
                        promptProveedor, proveedor, cts.Token);
                    return await EsperarAsync(tarea, cts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    errores.Add($"{proveedor.Nombre}: no respondió a tiempo.");
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    errores.Add($"{proveedor.Nombre}: {ex.Message}");
                }
                finally
                {
                    cts.Dispose();
                }

                Subtitulo.Text = $"No respondió {proveedor.Nombre}. Probando otra IA…";
            }

            ct.ThrowIfCancellationRequested();
            var cambiar = await PreguntarCambiarProveedorAsync(proveedores, errores);
            if (cambiar is null)
                throw new IAException("Ninguna IA pudo responder:\n" + string.Join("\n", errores));

            pendientes = new List<IAIProvider> { cambiar };
            Subtitulo.Text = $"Reintentando con {cambiar.Nombre}…";
        }
    }

    async Task<IAIProvider?> PreguntarCambiarProveedorAsync(
        List<IAIProvider> proveedores, List<string> errores)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null)
            return null;

        var continuar = await page.DisplayAlertAsync(
            "No se pudo responder",
            "Se probaron todas las IAs disponibles. ¿Quieres elegir otra y continuar la búsqueda?\n\n"
                + string.Join("\n", errores),
            "Cambiar IA",
            "Detener búsqueda");
        if (!continuar)
            return null;

        var opciones = proveedores.Select(p => p.Nombre).ToArray();
        var elegida = await page.DisplayActionSheetAsync(
            "Elige una IA para continuar", "Cancelar", null, opciones);
        return proveedores.FirstOrDefault(p => p.Nombre == elegida);
    }

    static bool PuedeBuscarEnWeb(string nombreProveedor) =>
        nombreProveedor.StartsWith("Exa", StringComparison.Ordinal);

    static bool PuedeBuscarEnWeb(IAIProvider proveedor) => PuedeBuscarEnWeb(proveedor.Nombre);

    /// <summary>Devuelve en cuanto se cancela el token aunque el proveedor ignore la cancelación.</summary>
    static async Task<T> EsperarAsync<T>(Task<T> tarea, CancellationToken ct)
    {
        var cancelada = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registro = ct.Register(() => cancelada.TrySetResult(true));

        if (await Task.WhenAny(tarea, cancelada.Task) != tarea)
        {
            // Evita excepciones no observadas si la tarea abandonada falla después.
            _ = tarea.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            throw new OperationCanceledException(ct);
        }

        return await tarea;
    }

    void Cancelar_Click(object? sender, EventArgs e) => _envioCts?.Cancel();

    async void EditarPregunta_Click(object? sender, EventArgs e)
    {
        if (_ultimoMensajeUsuarioId is null || _chatActual is null)
            return;

        var conn = await LocalDatabase.GetAsync();
        var mensaje = await conn.Table<Mensaje>()
            .Where(m => m.Id == _ultimoMensajeUsuarioId).FirstOrDefaultAsync();
        if (mensaje is null)
            return;

        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null)
            return;

        var texto = await page.DisplayPromptAsync(
            "Editar pregunta", "Modifica tu mensaje y se volverá a generar la respuesta.",
            "Actualizar", "Cancelar", initialValue: mensaje.Contenido);
        if (string.IsNullOrWhiteSpace(texto) || texto.Trim() == mensaje.Contenido)
            return;

        _envioCts?.Cancel();
        if (_operacionActual is not null)
            await _operacionActual;

        _operacionActual = EnviarMensajeAsync(mensaje.Id, texto);
        await _operacionActual;
    }

    async void CopiarMensaje_Click(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is string texto)
            await Clipboard.Default.SetTextAsync(texto);
    }

    async Task GuardarMensajeAsync(Mensaje mensaje)
    {
        var conn = await LocalDatabase.GetAsync();
        await conn.InsertAsync(mensaje);

        var client = await SupabaseService.GetClientAsync();
        if (client is not null)
        {
            try { await client.From<Mensaje>().Insert(mensaje); }
            catch { /* se sincroniza después */ }
        }
    }

    async Task<List<Mensaje>> ObtenerHistorialAsync()
    {
        var conn = await LocalDatabase.GetAsync();
        return await conn.Table<Mensaje>()
            .Where(m => m.ChatId == _chatActual!.Id)
            .OrderBy(m => m.CreadoEn)
            .ToListAsync();
    }

    static readonly string[] PistasActualidad =
    {
        "hoy", "actual", "actualmente", "reciente", "recientes", "último", "ultimo", "última", "ultima",
        "últimas", "ultimas", "noticia", "noticias", "ahora", "este año", "esta semana", "este mes",
        "precio", "cotización", "cotizacion", "versión", "version", "2025", "2026", "vigente", "nuevo", "nueva"
    };

    static bool PideInformacionReciente(string texto)
    {
        var t = texto.ToLowerInvariant();
        return PistasActualidad.Any(p => t.Contains(p));
    }

    static string QuitarFirmaProveedor(string contenido)
    {
        // Las respuestas se guardan como "texto\n\n— Proveedor"; no se reenvía la firma a la IA.
        var i = contenido.LastIndexOf("\n\n— ", StringComparison.Ordinal);
        return i >= 0 ? contenido[..i] : contenido;
    }

    static string ConstruirPromptChat(string tema, List<Mensaje> historial, bool buscarWeb)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Eres un tutor educativo amable y claro. Estás ayudando a un estudiante con el tema: \"{tema}\".");
        sb.AppendLine("Responde en español, de forma concisa y didáctica.");
        sb.AppendLine($"La fecha actual es {DateTime.Today:yyyy-MM-dd}.");
        if (buscarWeb)
        {
            sb.AppendLine("Usa la herramienta de búsqueda web y basa tu respuesta en las fuentes más recientes disponibles. " +
                          "Indica la fecha o el año de los datos y menciona las fuentes consultadas.");
            sb.AppendLine("No des por vigentes datos de tu entrenamiento si pueden haber cambiado. " +
                          "Si la búsqueda web no está disponible, aclara que no puedes verificar la vigencia en vez de presentar datos inciertos como actuales.");
        }
        else
        {
            sb.AppendLine("Si la pregunta depende de datos que cambian con el tiempo, avisa que podrían estar desactualizados.");
        }
        sb.AppendLine();
        sb.AppendLine("Conversación hasta ahora:");

        // Solo los últimos 12 mensajes para no exceder el contexto.
        foreach (var m in historial.TakeLast(12))
        {
            var etiqueta = m.Rol == "user" ? "Estudiante" : "Tutor";
            var contenido = m.Rol == "user" ? m.Contenido : QuitarFirmaProveedor(m.Contenido);
            sb.AppendLine($"{etiqueta}: {contenido}");
        }

        sb.AppendLine("Tutor:");
        return sb.ToString();
    }

    async Task CargarMensajesAsync()
    {
        if (_chatActual is null)
            return;

        var conn = await LocalDatabase.GetAsync();
        var mensajes = await conn.Table<Mensaje>()
            .Where(m => m.ChatId == _chatActual.Id)
            .OrderBy(m => m.CreadoEn)
            .ToListAsync();

        _items = mensajes.Select(m => new MensajeItem(m)).ToList();
        ListaMensajes.ItemsSource = _items;
        var ultimoUsuario = mensajes.LastOrDefault(m => m.Rol == "user");
        _ultimoMensajeUsuarioId = ultimoUsuario?.Id;
        BtnEditarPregunta.IsVisible = !_ocupado && ultimoUsuario is not null;

        await ScrollAlFinalAsync();
    }

    /// <summary>Ancla la conversación al último mensaje (se reintenta porque la lista maqueta de forma asíncrona).</summary>
    async Task ScrollAlFinalAsync()
    {
        foreach (var espera in new[] { 60, 200, 450 })
        {
            await Task.Delay(espera);
            try
            {
                var ultimo = _items.LastOrDefault();
                if (ultimo is null)
                    return;

                ListaMensajes.ScrollTo(ultimo, position: ScrollToPosition.End, animate: false);
            }
            catch { /* la lista pudo vaciarse o cambiar */ }
        }
    }

    // ---------------- UTILIDADES ----------------

    internal static Style? ChipStyle() =>
        Application.Current?.Resources.TryGetValue("ChipButton", out var s) == true ? s as Style : null;

    static Brush Gradient() =>
        Application.Current?.Resources.TryGetValue("BrandGradient", out var b) == true && b is Brush br
            ? br
            : new SolidColorBrush(Color.FromArgb("#22D3EE"));

    Task MostrarAsync(string titulo, string mensaje)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        return page is null ? Task.CompletedTask : page.DisplayAlertAsync(titulo, mensaje, "OK");
    }
}

public class MensajeItem
{
    public string Texto { get; set; } = string.Empty;
    public string Hora { get; set; } = string.Empty;
    public LayoutOptions Alineacion { get; set; } = LayoutOptions.Start;
    public Color ColorFondo { get; set; } = Colors.Transparent;
    public Color ColorTexto { get; set; } = Colors.White;
    public Color ColorTextoTexto { get; set; } = Colors.White;
    public Color ColorHora { get; set; } = Colors.Gray;
    public bool EsIA { get; set; }

    public MensajeItem() { }

    public MensajeItem(Mensaje m)
    {
        Texto = m.Contenido;
        Hora = DateTime.TryParse(m.CreadoEn, out var f) ? f.ToLocalTime().ToString("HH:mm") : "";

        if (m.Rol == "user")
        {
            Alineacion = LayoutOptions.End;
            ColorFondo = Color.FromArgb("#2563EB");
            ColorTexto = Color.FromArgb("#2563EB");
            ColorTextoTexto = Colors.White;
            ColorHora = Color.FromArgb("#BFDBFE");
        }
        else
        {
            EsIA = true;
            Alineacion = LayoutOptions.Start;
            ColorFondo = Color.FromArgb("#0F1733");
            ColorTexto = Color.FromArgb("#2A3A6A");
            ColorTextoTexto = Color.FromArgb("#E2E8F0");
            ColorHora = Color.FromArgb("#94A3B8");
        }
    }
}