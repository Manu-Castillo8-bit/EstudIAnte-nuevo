using System.Text;
using EstudIAnte.Models;
using EstudIAnte.Services;

namespace EstudIAnte;

public partial class MisArchivosView : ContentView
{
    List<Documento> _docs = new();
    List<Diagrama> _diagramas = new();
    bool _trabajando;
    bool? _compactLayout;

    public MisArchivosView()
    {
        InitializeComponent();
        Loaded += (_, _) => _ = CargarTodoAsync();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0)
            return;

        var compact = width < 760;
        var muyEstrecho = width < 420;
        BtnSincronizar.Text = muyEstrecho ? "🔄 Sincronizar" : "🔄  Sincronizar";
        BtnSincronizar.HorizontalOptions = compact ? LayoutOptions.Fill : LayoutOptions.End;
        if (_compactLayout == compact)
            return;

        _compactLayout = compact;
        FilesLayout.Padding = compact
            ? new Thickness(14, 8, 14, 20)
            : new Thickness(28, 8, 28, 28);
    }

    /// <summary>Recarga documentos y diagramas (la llama MainPage al entrar a la sección).</summary>
    public async Task RefrescarAsync() => await CargarTodoAsync();

    /// <summary>Filtra por el texto de la búsqueda global de la barra superior.</summary>
    public void AplicarBusqueda(string texto)
    {
        BuscadorArchivos.Text = texto;
    }

    async Task CargarTodoAsync()
    {
        var usuario = AuthService.UsuarioActual;
        if (usuario is null)
            return;

        var conn = await LocalDatabase.GetAsync();
        _docs = await conn.Table<Documento>()
            .Where(d => d.UsuarioId == usuario.Id)
            .OrderByDescending(d => d.CreadoEn)
            .ToListAsync();

        _diagramas = await conn.Table<Diagrama>()
            .Where(d => d.UsuarioId == usuario.Id)
            .OrderByDescending(d => d.CreadoEn)
            .ToListAsync();

        Subtitulo.Text = $"{_docs.Count} documento(s) · {_diagramas.Count} diagrama(s)";
        TituloDocs.Text = $"📄  Documentos ({_docs.Count})";
        TituloDiags.Text = $"🔀  Diagramas ({_diagramas.Count})";

        AplicarFiltro();
    }

    void AplicarFiltro()
    {
        var texto = BuscadorArchivos.Text?.Trim();

        var docs = string.IsNullOrWhiteSpace(texto)
            ? _docs
            : _docs.Where(d =>
                d.Nombre.Contains(texto, StringComparison.OrdinalIgnoreCase) ||
                d.Resumen.Contains(texto, StringComparison.OrdinalIgnoreCase) ||
                d.Texto.Contains(texto, StringComparison.OrdinalIgnoreCase));

        var diags = string.IsNullOrWhiteSpace(texto)
            ? _diagramas
            : _diagramas.Where(d =>
                d.Titulo.Contains(texto, StringComparison.OrdinalIgnoreCase) ||
                d.Fuente.Contains(texto, StringComparison.OrdinalIgnoreCase));

        ListaDocumentos.ItemsSource = docs.Select(d => new DocumentoItem(d)).ToList();
        ListaDiagramas.ItemsSource = diags.Select(d => new DiagramaItem(d)).ToList();
    }

    void Buscador_TextChanged(object? sender, TextChangedEventArgs e) => AplicarFiltro();

    // ---------------- SINCRONIZACIÓN ----------------

    async void Sincronizar_Click(object? sender, EventArgs e)
    {
        if (_trabajando)
            return;

        _trabajando = true;
        BtnSincronizar.IsBusy = true;
        EstadoSync.Text = "Sincronizando con la nube...";
        EstadoSync.TextColor = Color.FromArgb("#22D3EE");

        try
        {
            var (ok, mensaje) = await SyncService.SincronizarTodoAsync();
            EstadoSync.Text = (ok ? "✅ " : "⚠ ") + mensaje;
            EstadoSync.TextColor = Color.FromArgb(ok ? "#34D399" : "#FBBF24");
            await CargarTodoAsync();
        }
        finally
        {
            _trabajando = false;
            BtnSincronizar.IsBusy = false;
        }
    }

    // ---------------- EXPORTAR DOCUMENTO ----------------

    async void ExportarDocumento_Click(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not string id || _trabajando)
            return;

        var conn = await LocalDatabase.GetAsync();
        var doc = await conn.Table<Documento>().Where(d => d.Id == id).FirstOrDefaultAsync();
        if (doc is null)
            return;

        _trabajando = true;
        try
        {
            byte[]? bytes = null;
            var nombreArchivo = doc.Nombre;

            // 1) Intenta descargar el archivo ORIGINAL desde Supabase Storage.
            var client = await SupabaseService.GetClientAsync();
            if (client is not null && !string.IsNullOrWhiteSpace(doc.RutaStorage))
            {
                try
                {
                    bytes = await client.Storage.From("documentos")
                        .Download(doc.RutaStorage, (Supabase.Storage.TransformOptions?)null, null);
                }
                catch
                {
                    bytes = null;
                }
            }

            // 2) Sin conexión o sin Storage: exporta el texto analizado como .txt.
            if (bytes is null)
            {
                if (string.IsNullOrWhiteSpace(doc.Texto))
                {
                    await MostrarAsync("Sin contenido",
                        "No se pudo descargar el archivo original (requiere conexión) " +
                        "y el documento no tiene texto analizado para exportar.");
                    return;
                }

                nombreArchivo = CambiarExtension(doc.Nombre, ".txt");
                bytes = Encoding.UTF8.GetBytes(doc.Texto);
            }

            var ruta = await FileSaveHelper.GuardarAsync(nombreArchivo, bytes);
            if (ruta is null)
                return; // cancelado por el usuario
            await MostrarAsync("Documento exportado", $"Guardado en:\n{ruta}");
        }
        catch (Exception ex)
        {
            await MostrarAsync("Error", "No se pudo exportar: " + ex.Message);
        }
        finally
        {
            _trabajando = false;
        }
    }

    // ---------------- EXPORTAR DIAGRAMA ----------------

    async void ExportarDiagrama_Click(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not string id || _trabajando)
            return;

        var conn = await LocalDatabase.GetAsync();
        var diagrama = await conn.Table<Diagrama>().Where(d => d.Id == id).FirstOrDefaultAsync();
        if (diagrama is null || string.IsNullOrWhiteSpace(diagrama.Codigo))
            return;

        _trabajando = true;
        try
        {
            // HTML autocontenido: se abre en cualquier navegador y dibuja el diagrama.
            var html = DiagramasView.HtmlDiagrama(diagrama.Codigo, diagrama.Tipo);
            var bytes = Encoding.UTF8.GetBytes(html);
            var nombre = NombreSeguro(diagrama.Titulo) + ".html";

            var ruta = await FileSaveHelper.GuardarAsync(nombre, bytes);
            if (ruta is null)
                return; // cancelado por el usuario
            await MostrarAsync("Diagrama exportado", $"Guardado en:\n{ruta}\n\nÁbrelo con tu navegador para verlo.");
        }
        catch (Exception ex)
        {
            await MostrarAsync("Error", "No se pudo exportar: " + ex.Message);
        }
        finally
        {
            _trabajando = false;
        }
    }

    async void CopiarDiagrama_Click(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not string id)
            return;

        var conn = await LocalDatabase.GetAsync();
        var diagrama = await conn.Table<Diagrama>().Where(d => d.Id == id).FirstOrDefaultAsync();
        if (diagrama is null || string.IsNullOrWhiteSpace(diagrama.Codigo))
            return;

        await Clipboard.SetTextAsync(diagrama.Codigo);
        EstadoSync.Text = $"📋 Código de «{diagrama.Titulo}» copiado al portapapeles.";
        EstadoSync.TextColor = Color.FromArgb("#22D3EE");
    }

    // ---------------- ELIMINAR ----------------

    async void EliminarDocumento_Click(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not string id || _trabajando)
            return;

        var conn = await LocalDatabase.GetAsync();
        var doc = await conn.Table<Documento>().Where(d => d.Id == id).FirstOrDefaultAsync();
        if (doc is null)
            return;

        var confirmar = await MostrarConfirmacionAsync("Eliminar documento",
            $"¿Borrar «{doc.Nombre}» de tus archivos? Esta acción no se puede deshacer.");
        if (!confirmar)
            return;

        _trabajando = true;
        try
        {
            await SyncService.MarcarBorradoAsync("documentos", doc.Id);
            await conn.DeleteAsync(doc);

            var client = await SupabaseService.GetClientAsync();
            if (client is not null)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(doc.RutaStorage))
                        await client.Storage.From("documentos").Remove(doc.RutaStorage);
                    await client.From<Documento>().Where(d => d.Id == id).Delete();
                }
                catch { /* sin conexión: SyncService lo borra después */ }
            }

            await CargarTodoAsync();
        }
        finally
        {
            _trabajando = false;
        }
    }

    async void EliminarDiagrama_Click(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not string id || _trabajando)
            return;

        var conn = await LocalDatabase.GetAsync();
        var diagrama = await conn.Table<Diagrama>().Where(d => d.Id == id).FirstOrDefaultAsync();
        if (diagrama is null)
            return;

        var confirmar = await MostrarConfirmacionAsync("Eliminar diagrama",
            $"¿Borrar «{diagrama.Titulo}»? Esta acción no se puede deshacer.");
        if (!confirmar)
            return;

        _trabajando = true;
        try
        {
            await SyncService.MarcarBorradoAsync("diagramas", diagrama.Id);
            await conn.DeleteAsync(diagrama);

            var client = await SupabaseService.GetClientAsync();
            if (client is not null)
            {
                try { await client.From<Diagrama>().Where(d => d.Id == id).Delete(); }
                catch { /* sin conexión: SyncService lo borra después */ }
            }

            await CargarTodoAsync();
        }
        finally
        {
            _trabajando = false;
        }
    }

    // ---------------- UTILIDADES ----------------

    static string CambiarExtension(string nombre, string extension)
    {
        try { return Path.ChangeExtension(nombre, extension); }
        catch { return nombre + extension; }
    }

    static string NombreSeguro(string titulo)
    {
        var limpio = string.Join("_", (titulo ?? "diagrama").Split(Path.GetInvalidFileNameChars()));
        return string.IsNullOrWhiteSpace(limpio) ? "diagrama" : limpio;
    }

    Task MostrarAsync(string titulo, string mensaje)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        return page is null ? Task.CompletedTask : page.DisplayAlertAsync(titulo, mensaje, "OK");
    }

    async Task<bool> MostrarConfirmacionAsync(string titulo, string mensaje)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null)
            return false;
        return await page.DisplayAlertAsync(titulo, mensaje, "Sí", "No");
    }
}
