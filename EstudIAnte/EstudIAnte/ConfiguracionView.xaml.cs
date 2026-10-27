using EstudIAnte.Services;

namespace EstudIAnte;

public partial class ConfiguracionView : ContentView
{
    bool _trabajando;
    bool? _compactLayout;

    public ConfiguracionView()
    {
        InitializeComponent();
        Loaded += (_, _) => _ = ActualizarAsync();
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
        ConfiguracionLayout.Padding = compact
            ? new Thickness(14, 8, 14, 20)
            : new Thickness(28, 8, 28, 28);
    }

    /// <summary>Actualiza la información de cuenta y estado de la nube (la llama MainPage).</summary>
    public async Task RefrescarAsync()
    {
        var u = AuthService.UsuarioActual;
        if (u is not null)
        {
            LblUsuario.Text = string.IsNullOrWhiteSpace(u.Nombre) ? u.NombreUsuario : u.Nombre;
            LblRol.Text = u.Rol == "admin" ? "Administrador" : "Estudiante";
        }

        await ActualizarAsync();
    }

    async Task ActualizarAsync()
    {
        if (_trabajando)
            return;

        LblEstadoNube.Text = "Comprobando conexión...";
        var (ok, mensaje) = await SupabaseService.TestConnectionAsync();
        LblEstadoNube.Text = (ok ? "🟢 " : "🔴 ") + mensaje;
        LblEstadoNube.TextColor = Color.FromArgb(ok ? "#34D399" : "#F87171");
    }

    async void Sincronizar_Click(object? sender, EventArgs e)
    {
        if (_trabajando)
            return;

        _trabajando = true;
        BtnSincronizar.IsBusy = true;
        LblResultadoSync.Text = "⏳ Sincronizando (subiendo y descargando datos)...";
        LblResultadoSync.TextColor = Color.FromArgb("#22D3EE");

        try
        {
            var (ok, mensaje) = await SyncService.SincronizarTodoAsync();
            LblResultadoSync.Text = (ok ? "✅ " : "⚠ ") + mensaje;
            LblResultadoSync.TextColor = Color.FromArgb(ok ? "#34D399" : "#FBBF24");
        }
        finally
        {
            _trabajando = false;
            BtnSincronizar.IsBusy = false;
        }
    }

    async void ProbarConexion_Click(object? sender, EventArgs e) => await ActualizarAsync();

    async void CerrarSesion_Click(object? sender, EventArgs e)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is not null)
        {
            var ok = await page.DisplayAlertAsync("Cerrar sesión",
                "¿Seguro que quieres salir de tu cuenta?", "Sí", "No");
            if (!ok)
                return;
        }

        AuthService.CerrarSesion();
        Application.Current!.Windows[0].Page = new LoginWindow();
    }
}
