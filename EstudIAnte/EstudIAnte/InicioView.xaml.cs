using Microsoft.Maui.Layouts;
using EstudIAnte.Models;
using EstudIAnte.Services;

namespace EstudIAnte;

public partial class InicioView : ContentView
{
    bool? _compactLayout;

    public InicioView()
    {
        InitializeComponent();
        Loaded += (_, _) => _ = CargarDashboardAsync();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0)
            return;

        var compact = width < 760;
        if (_compactLayout != compact)
            HomeLayout.Padding = compact
                ? new Thickness(14, 8, 14, 20)
                : new Thickness(28, 8, 28, 28);
        _compactLayout = compact;
        HeroRobot.IsVisible = width >= 760;
        foreach (var card in QuickAccess.Children.OfType<Border>())
        {
            FlexLayout.SetBasis(card, new FlexBasis(compact ? 0.47f : 0.22f, true));
            card.MinimumWidthRequest = compact ? Math.Max(112, (width - 72) / 2) : 180;
            card.Margin = compact ? new Thickness(0, 0, 8, 8) : new Thickness(0, 0, 16, 16);
        }

        foreach (var card in DashboardStats.Children.OfType<Border>())
        {
            FlexLayout.SetBasis(card, new FlexBasis(compact ? 0.46f : 0.19f, true));
            card.MinimumWidthRequest = compact ? 105 : 130;
            card.Margin = new Thickness(0, 0, compact ? 6 : 10, 8);
        }
    }

    /// <summary>Recarga el dashboard (MainPage lo llama al volver a Inicio).</summary>
    public async Task RefrescarAsync()
    {
        ActualizarSaludo();
        await CargarDashboardAsync();
    }

    void ActualizarSaludo()
    {
        var u = AuthService.UsuarioActual;
        if (u is null)
            return;

        var nombre = !string.IsNullOrWhiteSpace(u.Nombre) ? u.Nombre : u.NombreUsuario;
        SaludoNombre.Text = $"¡Hola, {nombre}!";
    }

    // ---------------- DASHBOARD ----------------

    async Task CargarDashboardAsync()
    {
        var usuario = AuthService.UsuarioActual;
        if (usuario is null)
            return;

        var conn = await LocalDatabase.GetAsync();
        var temas = (await conn.Table<Tema>().Where(t => t.UsuarioId == usuario.Id).ToListAsync())
            .OrderBy(t => t.Nombre).ToList();
        var chats = (await conn.Table<Chat>().Where(c => c.UsuarioId == usuario.Id).ToListAsync())
            .OrderByDescending(c => c.CreadoEn).ToList();
        var mensajes = (await conn.Table<Mensaje>().ToListAsync())
            .Where(m => chats.Any(c => c.Id == m.ChatId)).OrderBy(m => m.CreadoEn).ToList();
        var quizzes = await conn.Table<Cuestionario>().Where(q => q.UsuarioId == usuario.Id).ToListAsync();
        var resultados = (await conn.Table<Resultado>().Where(r => r.UsuarioId == usuario.Id).ToListAsync())
            .OrderByDescending(r => r.Fecha).ToList();
        var documentos = await conn.Table<Documento>().Where(d => d.UsuarioId == usuario.Id).ToListAsync();
        var diagramas = await conn.Table<Diagrama>().Where(dg => dg.UsuarioId == usuario.Id).ToListAsync();

        // Tarjetas de estadísticas
        var promedio = resultados.Count > 0
            ? (int)Math.Round(resultados.Average(r => r.Total > 0 ? 100.0 * r.Puntaje / r.Total : 0))
            : -1;
        var stats = new List<(string Emoji, string Valor, string Titulo, string Detalle)>
        {
            ("💬", chats.Count.ToString(), "Chats", $"{mensajes.Count} mensajes"),
            ("📋", quizzes.Count.ToString(), "Cuestionarios",
                resultados.Count > 0 ? $"{promedio}% promedio" : quizzes.Count > 0 ? "sin responder aún" : "crea el primero"),
            ("📁", documentos.Count.ToString(), "Documentos", $"{documentos.Count(d => d.Estado == "listo")} analizados"),
            ("🔀", diagramas.Count.ToString(), "Diagramas", "creados con IA"),
            ("🏁", resultados.Count.ToString(), "Resultados",
                resultados.Count > 0 ? $"mejor: {resultados.Max(r => r.Puntaje)}/{MaxTotal(resultados)}" : "—"),
        };

        DashboardStats.Clear();
        foreach (var (emoji, valor, titulo, detalle) in stats)
        {
            var card = CrearStatCard(emoji, valor, titulo, detalle);
            FlexLayout.SetBasis(card, new FlexBasis(_compactLayout == true ? 0.48f : 0.19f, true));
            card.MinimumWidthRequest = _compactLayout == true ? 105 : 130;
            card.Margin = new Thickness(0, 0, _compactLayout == true ? 6 : 10, 8);
            DashboardStats.Add(card);
        }

        // Contenido interactivo
        PanelDashboard.Children.Clear();

        // 1) Temas de investigación
        PanelDashboard.Children.Add(Encabezado("🔎", "Temas de investigación"));
        if (temas.Count == 0)
        {
            PanelDashboard.Children.Add(Muted("Aún no has creado temas. Ve a «Chat Temático» y crea el primero."));
        }
        else
        {
            var filaTemas = new FlexLayout { Wrap = FlexWrap.Wrap, JustifyContent = FlexJustify.Start, AlignContent = FlexAlignContent.Start };
            foreach (var tema in temas.Take(8))
            {
                var nChats = chats.Count(c => c.Tema == tema.Nombre);
                var nMens = mensajes.Count(m => chats.Any(c => c.Id == m.ChatId && c.Tema == tema.Nombre));
                var boton = new Controls.PulsarButton
                {
                    Text = $"{tema.Nombre}  ({nChats} chat{(nChats == 1 ? "" : "s")} · {nMens} msj)",
                    Style = ChatView.ChipStyle(),
                    Margin = new Thickness(0, 0, 8, 8),
                    CommandParameter = tema.Nombre,
                };
                boton.Clicked += (s, e) => _ = IrATemaAsync((s as Controls.PulsarButton)?.CommandParameter as string);
                filaTemas.Add(boton);
            }
            PanelDashboard.Children.Add(filaTemas);
        }

        // 2) Últimos chats
        PanelDashboard.Children.Add(Encabezado("🕘", "Tus últimos chats"));
        var ultimos = chats.Take(5).ToList();
        if (ultimos.Count == 0)
            PanelDashboard.Children.Add(Muted("Todavía no tienes conversaciones."));
        else
        {
            foreach (var chat in ultimos)
            {
                var nMens = mensajes.Count(m => m.ChatId == chat.Id);
                var (fecha, _) = ParsearFecha(chat.CreadoEn);
                var fila = FilaClicable(
                    "💬",
                    chat.Titulo,
                    $"Tema: {chat.Tema} · {nMens} mensajes · {fecha}",
                    () => _ = IrATemaAsync(chat.Tema));
                PanelDashboard.Children.Add(fila);
            }
        }

        // 3) Cuestionarios: últimos resultados y promedio
        PanelDashboard.Children.Add(Encabezado("🧠", "Tus cuestionarios"));
        if (resultados.Count == 0)
            PanelDashboard.Children.Add(Muted(
                quizzes.Count == 0
                    ? "Aún no creas cuestionarios. Genera uno desde tus documentos o temas."
                    : $"Tienes {quizzes.Count} cuestionario(s) sin responder. ¡Anímate!"));
        else
        {
            var (ultimoResult, _) = ParsearFecha(resultados[0].Fecha);
            PanelDashboard.Children.Add(Muted($"{promedio}% de acierto total · último resultado: {ultimoResult}"));

            foreach (var r in resultados.Take(5))
            {
                var (fechaRes, _) = ParsearFecha(r.Fecha);
                var minutos = TimeSpan.FromSeconds(r.TiempoSegundos).ToString(@"mm\:ss");
                var icon = r.Total > 0 && r.Puntaje * 100 / r.Total >= 60 ? "🟢" : "🔴";
                var fila = FilaClicable(
                    icon,
                    $"{r.Puntaje} / {r.Total} correctas",
                    $"{fechaRes} · tiempo {minutos}",
                    () => MainPage.Actual?.IrA("quiz"));
                PanelDashboard.Children.Add(fila);
            }
        }

        // 4) Documentos y diagramas (lo más importante del material)
        PanelDashboard.Children.Add(Encabezado("📚", "Tu material de estudio"));
        var analizados = documentos.Where(d => d.Estado == "listo").ToList();
        if (documentos.Count == 0 && diagramas.Count == 0)
            PanelDashboard.Children.Add(Muted("Sube documentos para que la IA los analice y les cree resúmenes."));
        else
        {
            foreach (var doc in analizados.Take(4))
            {
                var (fechaDoc, _) = ParsearFecha(doc.CreadoEn);
                var franja = string.IsNullOrWhiteSpace(doc.Resumen) ? "sin resumen aún" : Recortar(doc.Resumen, 70);
                var fila = FilaClicable(
                    "📄",
                    doc.Nombre,
                    $"{doc.Tipo.ToUpperInvariant()} · {fechaDoc} · {franja}",
                    () => MainPage.Actual?.IrA("documentos"));
                PanelDashboard.Children.Add(fila);
            }
            if (diagramas.Count > 0)
                PanelDashboard.Children.Add(Muted($"Total: {documentos.Count} documento(s), {diagramas.Count} diagrama(s)."));
        }
    }

    static string Recortar(string texto, int max) =>
        string.IsNullOrEmpty(texto) ? string.Empty :
        texto.Length <= max ? texto : texto[..max] + "…";

    static int MaxTotal(List<Resultado> resultados) => resultados.Any() ? resultados.Max(r => r.Total) : 0;

    static Label Encabezado(string emoji, string texto) => new()
    {
        Text = $"{emoji}  {texto}",
        FontSize = 15,
        FontAttributes = FontAttributes.Bold,
        TextColor = Color.FromArgb("#E2E8F0"),
        Margin = new Thickness(0, 6, 0, 0),
    };

    static Label Muted(string texto) => new()
    {
        Text = texto,
        FontSize = 13,
        TextColor = Color.FromArgb("#94A3B8"),
    };

    static Border CrearStatCard(string emoji, string valor, string titulo, string detalle) => new()
    {
        BackgroundColor = Color.FromArgb("#0D1236"),
        Stroke = Color.FromArgb("#2A3A6A"), StrokeThickness = 1,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(14) },
        Padding = new Thickness(14, 10),
        Margin = new Thickness(0, 0, 10, 10),
        Content = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new HorizontalStackLayout
                {
                    Spacing = 6,
                    Children =
                    {
                        new Label { Text = emoji, FontSize = 15, VerticalOptions = LayoutOptions.Center },
                        new Label { Text = valor, FontSize = 22, FontAttributes = FontAttributes.Bold, TextColor = Colors.White },
                    },
                },
                new Label { Text = titulo, FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#22D3EE") },
                new Label { Text = detalle, FontSize = 11, TextColor = Color.FromArgb("#94A3B8") },
            },
        },
    };

    static Border FilaClicable(string emoji, string titulo, string detalle, Action accion)
    {
        var contenido = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Auto),
                new(GridLength.Star),
            },
            ColumnSpacing = 10,
        };
        contenido.Children.Add(new Label
        {
            Text = emoji,
            FontSize = 16,
            VerticalOptions = LayoutOptions.Center,
        });
        var etiquetas = new VerticalStackLayout
        {
            Spacing = 1,
            Children =
            {
                new Label { Text = titulo, FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#E2E8F0"), LineBreakMode = LineBreakMode.TailTruncation },
                new Label { Text = detalle, FontSize = 11, TextColor = Color.FromArgb("#94A3B8"), LineBreakMode = LineBreakMode.WordWrap },
            },
        };
        Grid.SetColumn(etiquetas, 1);
        contenido.Children.Add(etiquetas);

        var fila = new Border
        {
            BackgroundColor = Color.FromArgb("#0D1236"),
            Stroke = Color.FromArgb("#2A3A6A"), StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
            Padding = new Thickness(14, 10),
            Margin = new Thickness(0, 0, 0, 8),
            Content = contenido,
        };
        fila.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(accion) });
        return fila;
    }

    async Task IrATemaAsync(string? nombre)
    {
        MainPage.Actual?.IrA("chat");
        if (nombre is not null && MainPage.Actual is not null)
            await MainPage.Actual.VisorChat.AbrirTemaPorNombreAsync(nombre);
    }

    static (string, bool) ParsearFecha(string iso)
    {
        try
        {
            var f = DateTime.Parse(iso, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
            return (f.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), true);
        }
        catch { return (iso, false); }
    }

    void Card_Chat(object? sender, TappedEventArgs e) => MainPage.Actual?.IrA("chat");

    void Card_Quiz(object? sender, TappedEventArgs e) => MainPage.Actual?.IrA("quiz");

    void Card_Diagramas(object? sender, TappedEventArgs e) => _ = MainPage.Actual?.IrADiagramasAsync();

    void Card_Documentos(object? sender, TappedEventArgs e) => MainPage.Actual?.IrA("documentos");
}
