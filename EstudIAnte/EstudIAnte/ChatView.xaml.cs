using EstudIAnte.Models;
using EstudIAnte.Services;

namespace EstudIAnte;

public partial class ChatView : ContentView
{
    Tema? _temaActual;
    Chat? _chatActual;
    bool _ocupado;

    public ChatView()
    {
        InitializeComponent();
        Loaded += (_, _) => _ = RefrescarTemasAsync();
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
            var boton = new Button
            {
                Text = tema.Nombre,
                Style = ChipStyle(),
                CommandParameter = tema
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
        if ((sender as Button)?.CommandParameter is Tema tema)
            await SeleccionarTemaAsync(tema);
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
            var boton = new Button { Text = tema.Nombre, Style = ChipStyle(), CommandParameter = tema };
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

        var texto = MensajeEntry.Text?.Trim();
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

        await GuardarMensajeAsync(new Mensaje { ChatId = _chatActual.Id, Rol = "user", Contenido = texto });
        MensajeEntry.Text = string.Empty;
        await CargarMensajesAsync();

        // Respuesta de la IA
        _ocupado = true;
        BtnEnviar.IsEnabled = false;
        SpinnerIA.IsRunning = SpinnerIA.IsVisible = true;

        try
        {
            var historial = await ObtenerHistorialAsync();
            var prompt = ConstruirPromptChat(_temaActual.Nombre, historial);
            var (respuesta, proveedor) = await AIProviderManager.GenerarAsync(prompt);

            await GuardarMensajeAsync(new Mensaje
            {
                ChatId = _chatActual.Id,
                Rol = "assistant",
                Contenido = $"{respuesta}\n\n— {proveedor}"
            });
        }
        catch (Exception ex)
        {
            await GuardarMensajeAsync(new Mensaje
            {
                ChatId = _chatActual.Id,
                Rol = "assistant",
                Contenido = "⚠ " + ex.Message
            });
        }
        finally
        {
            _ocupado = false;
            BtnEnviar.IsEnabled = true;
            SpinnerIA.IsRunning = SpinnerIA.IsVisible = false;
            await CargarMensajesAsync();
        }
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

    static string ConstruirPromptChat(string tema, List<Mensaje> historial)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Eres un tutor educativo amable y claro. Estás ayudando a un estudiante con el tema: \"{tema}\".");
        sb.AppendLine("Responde en español, de forma concisa y didáctica.");
        sb.AppendLine();
        sb.AppendLine("Conversación hasta ahora:");

        // Solo los últimos 12 mensajes para no exceder el contexto.
        foreach (var m in historial.TakeLast(12))
        {
            var etiqueta = m.Rol == "user" ? "Estudiante" : "Tutor";
            sb.AppendLine($"{etiqueta}: {m.Contenido}");
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

        ListaMensajes.ItemsSource = mensajes.Select(m => new MensajeItem(m)).ToList();
    }

    // ---------------- UTILIDADES ----------------

    static Style? ChipStyle() =>
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
            Alineacion = LayoutOptions.Start;
            ColorFondo = Color.FromArgb("#0F1733");
            ColorTexto = Color.FromArgb("#2A3A6A");
            ColorTextoTexto = Color.FromArgb("#E2E8F0");
            ColorHora = Color.FromArgb("#94A3B8");
        }
    }
}
