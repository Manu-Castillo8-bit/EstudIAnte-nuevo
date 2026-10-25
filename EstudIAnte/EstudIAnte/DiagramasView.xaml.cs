using EstudIAnte.Models;
using EstudIAnte.Services;

namespace EstudIAnte;

public partial class DiagramasView : ContentView
{
    string _tipo = "flujo";
    readonly List<(string Id, string Nombre, bool EsDocumento)> _opciones = new();
    Diagrama? _actual;
    bool _generando;

    public DiagramasView()
    {
        InitializeComponent();
        Loaded += (_, _) => _ = CargarHistorialAsync();
    }

    // ---------------- TIPO ----------------

    void Tipo_Flujo(object? sender, EventArgs e)
    {
        _tipo = "flujo";
        LblTipoSel.Text = "Tipo: diagrama de flujo";
    }

    void Tipo_Mapa(object? sender, EventArgs e)
    {
        _tipo = "mapa";
        LblTipoSel.Text = "Tipo: mapa mental";
    }

    // ---------------- FUENTES ----------------

    async Task CargarFuentesAsync()
    {
        var usuario = AuthService.UsuarioActual;
        if (usuario is null)
            return;

        var conn = await LocalDatabase.GetAsync();
        Selector.Items.Clear();
        _opciones.Clear();

        var docs = await conn.Table<Documento>()
            .Where(d => d.UsuarioId == usuario.Id && d.Estado == "listo")
            .OrderByDescending(d => d.CreadoEn)
            .ToListAsync();

        foreach (var d in docs)
        {
            _opciones.Add((d.Id, d.Nombre, true));
            Selector.Items.Add("📄  " + d.Nombre);
        }

        var temas = await TemasService.ListarAsync(usuario.Id);
        foreach (var t in temas)
        {
            _opciones.Add((t.Id, t.Nombre, false));
            Selector.Items.Add("💬  " + t.Nombre);
        }

        if (_opciones.Count == 0)
        {
            LblEstadoGen.Text = "No hay fuentes. Analiza un documento o chatea en un tema primero.";
            return;
        }

        if (Selector.SelectedIndex < 0)
            Selector.SelectedIndex = 0;
    }

    /// <summary>
    /// Prepara la vista (carga fuentes) y preselecciona un documento si se indica.
    /// La llama MainPage cuando se navega desde Documentos o Inicio.
    /// </summary>
    public async Task PrepararAsync(string? documentoId = null)
    {
        await CargarFuentesAsync();

        if (documentoId is null)
            return;

        var indice = _opciones.FindIndex(o => o.Id == documentoId && o.EsDocumento);
        if (indice >= 0)
            Selector.SelectedIndex = indice;
    }

    /// <summary>Recarga fuentes e historial (la llama MainPage al entrar a la sección).</summary>
    public async Task RefrescarAsync()
    {
        await CargarFuentesAsync();
        await CargarHistorialAsync();
    }

    // ---------------- GENERADOR ----------------

    async void Generar_Click(object? sender, EventArgs e)
    {
        if (_generando)
            return;

        var usuario = AuthService.UsuarioActual;
        if (usuario is null)
        {
            await MostrarAsync("Aviso", "Debes iniciar sesión.");
            return;
        }

        if (Selector.SelectedIndex < 0 || Selector.SelectedIndex >= _opciones.Count)
        {
            await MostrarAsync("Aviso", "Elige una fuente para generar el diagrama.");
            return;
        }

        var (id, nombre, esDocumento) = _opciones[Selector.SelectedIndex];
        var conn = await LocalDatabase.GetAsync();

        string material;
        string? documentoId = null;
        string fuenteTexto;

        if (esDocumento)
        {
            var doc = await conn.Table<Documento>().Where(d => d.Id == id).FirstOrDefaultAsync();
            if (doc is null || string.IsNullOrWhiteSpace(doc.Texto))
            {
                await MostrarAsync("Aviso", "El documento no tiene texto analizado. Usa «Analizar con IA» primero.");
                return;
            }
            material = doc.Texto;
            documentoId = doc.Id;
            fuenteTexto = $"Documento: {doc.Nombre}";
        }
        else
        {
            material = await ObtenerMensajesDeTemaAsync(nombre);
            if (string.IsNullOrWhiteSpace(material))
            {
                await MostrarAsync("Aviso", "Ese tema aún no tiene conversación. Chatea un poco primero.");
                return;
            }
            fuenteTexto = $"Tema: {nombre}";
        }

        _generando = true;
        BtnGenerar.IsEnabled = false;
        SpinnerGen.IsRunning = SpinnerGen.IsVisible = true;
        LblEstadoGen.Text = "Generando diagrama con IA, espera un momento...";

        try
        {
            var (diagrama, ia) = await DiagramaGenerator.GenerarAsync(
                usuario.Id, nombre, fuenteTexto, material, documentoId, _tipo);

            _actual = diagrama;
            LblEstadoGen.Text = $"Diagrama generado con {ia}.";
            MostrarDiagrama(diagrama);
            await CargarHistorialAsync();
        }
        catch (Exception ex)
        {
            LblEstadoGen.Text = "No se pudo generar el diagrama.";
            await MostrarAsync("No se pudo generar", ex.Message);
        }
        finally
        {
            _generando = false;
            BtnGenerar.IsEnabled = true;
            SpinnerGen.IsRunning = SpinnerGen.IsVisible = false;
        }
    }

    async Task<string> ObtenerMensajesDeTemaAsync(string temaNombre)
    {
        var usuario = AuthService.UsuarioActual!;
        var conn = await LocalDatabase.GetAsync();

        var chat = await conn.Table<Chat>()
            .Where(c => c.UsuarioId == usuario.Id && c.Tema == temaNombre)
            .OrderByDescending(c => c.CreadoEn)
            .FirstOrDefaultAsync();

        if (chat is null)
            return string.Empty;

        var mensajes = await conn.Table<Mensaje>()
            .Where(m => m.ChatId == chat.Id)
            .OrderBy(m => m.CreadoEn)
            .ToListAsync();

        return string.Join("\n", mensajes.Select(m => $"{(m.Rol == "user" ? "Estudiante" : "Tutor")}: {m.Contenido}"));
    }

    // ---------------- VISOR ----------------

    void MostrarDiagrama(Diagrama diagrama)
    {
        TituloVisor.Text = diagrama.Titulo;
        EditorCodigo.Text = diagrama.Codigo;
        PanelVisor.IsVisible = true;
        Visor.Source = new HtmlWebViewSource { Html = HtmlDiagrama(diagrama.Codigo) };
    }

    const string PlantillaHtml = """
        <!DOCTYPE html>
        <html>
        <head>
        <meta charset="utf-8">
        <script src="https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.min.js"></script>
        <style>
          body { background:#070B21; margin:0; padding:14px; font-family:'Segoe UI',sans-serif; }
          #cont { display:flex; justify-content:center; overflow:auto; }
          #cont svg { max-width:100%; height:auto; }
          #err { display:none; color:#FCA5A5; background:#3F1D2B; border:1px solid #FB7185;
                 border-radius:10px; padding:12px; font-family:Consolas,monospace;
                 white-space:pre-wrap; font-size:12px; }
        </style>
        </head>
        <body>
        <div id="cont"></div>
        <div id="err"></div>
        <script>
          function mostrarError(t){
            var el = document.getElementById('err');
            el.style.display = 'block';
            el.textContent = t;
          }
          async function renderizar(datos){
            try{
              var r = await mermaid.render('grafo' + Date.now(), datos.codigo);
              document.getElementById('cont').innerHTML = r.svg;
            }catch(e){
              mostrarError('El diagrama no se pudo dibujar.\n\nCódigo Mermaid generado:\n\n' + datos.codigo);
            }
          }
          if (typeof mermaid === 'undefined') {
            mostrarError('No se pudo cargar el motor de diagramas (revisa tu conexión a internet).');
          } else {
            mermaid.initialize({ startOnLoad:false, theme:'dark', securityLevel:'loose' });
            try { renderizar(__CODIGO__); }
            catch(e){ mostrarError('Error al iniciar el diagrama: ' + e); }
          }
        </script>
        </body>
        </html>
        """;

    static string HtmlDiagrama(string codigo) =>
        PlantillaHtml.Replace("__CODIGO__", DiagramaGenerator.AJson(codigo).Replace("</", "<\\/"));

    async void Copiar_Click(object? sender, EventArgs e)
    {
        if (_actual is null || string.IsNullOrWhiteSpace(_actual.Codigo))
            return;

        await Clipboard.SetTextAsync(_actual.Codigo);
        LblEstadoGen.Text = "Código Mermaid copiado al portapapeles.";
    }

    void VerCodigo_Click(object? sender, EventArgs e)
    {
        EditorCodigo.IsVisible = !EditorCodigo.IsVisible;
        BtnVerCodigo.Text = EditorCodigo.IsVisible ? "🔀  Ver diagrama" : "📜  Ver código";
    }

    // ---------------- HISTORIAL ----------------

    async Task CargarHistorialAsync()
    {
        var usuario = AuthService.UsuarioActual;
        if (usuario is null)
            return;

        var conn = await LocalDatabase.GetAsync();
        var diagramas = await conn.Table<Diagrama>()
            .Where(d => d.UsuarioId == usuario.Id)
            .OrderByDescending(d => d.CreadoEn)
            .ToListAsync();

        ListaDiagramas.ItemsSource = diagramas.Select(d => new DiagramaItem(d)).ToList();
    }

    async void VerDiagrama_Click(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not string id)
            return;

        var conn = await LocalDatabase.GetAsync();
        var diagrama = await conn.Table<Diagrama>().Where(d => d.Id == id).FirstOrDefaultAsync();
        if (diagrama is null)
            return;

        _actual = diagrama;
        LblEstadoGen.Text = $"Abierto: {diagrama.Titulo}.";
        MostrarDiagrama(diagrama);

        // Sube el visor para que el usuario lo vea al instante
        await Scroll.ScrollToAsync(PanelVisor, ScrollToPosition.Start, true);
    }

    async void EliminarDiagrama_Click(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not string id)
            return;

        var conn = await LocalDatabase.GetAsync();
        var diagrama = await conn.Table<Diagrama>().Where(d => d.Id == id).FirstOrDefaultAsync();
        if (diagrama is null)
            return;

        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null)
            return;

        var confirmar = await page.DisplayAlertAsync("Eliminar diagrama",
            $"¿Borrar «{diagrama.Titulo}»? Esta acción no se puede deshacer.", "Sí", "No");
        if (!confirmar)
            return;

        await conn.DeleteAsync(diagrama);

        var client = await SupabaseService.GetClientAsync();
        if (client is not null)
        {
            try { await client.From<Diagrama>().Where(d => d.Id == id).Delete(); }
            catch { /* sin conexión: queda borrado localmente */ }
        }

        if (_actual?.Id == id)
        {
            _actual = null;
            PanelVisor.IsVisible = false;
        }

        await CargarHistorialAsync();
    }

    Task MostrarAsync(string titulo, string mensaje)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        return page is null ? Task.CompletedTask : page.DisplayAlertAsync(titulo, mensaje, "OK");
    }
}

public class DiagramaItem
{
    public string Id { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string Fuente { get; set; } = string.Empty;
    public string FechaTexto { get; set; } = string.Empty;
    public string Icono { get; set; } = "🔀";

    public DiagramaItem() { }

    public DiagramaItem(Diagrama d)
    {
        Id = d.Id;
        Titulo = d.Titulo;
        Fuente = d.Fuente;
        FechaTexto = FormatearFecha(d.CreadoEn);
        Icono = d.Tipo == "mapa" ? "🧠" : "🔀";
    }

    static string FormatearFecha(string creadoEn)
    {
        try
        {
            var fecha = DateTime.Parse(creadoEn, null, System.Globalization.DateTimeStyles.RoundtripKind);
            return fecha.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        }
        catch
        {
            return string.Empty;
        }
    }
}
