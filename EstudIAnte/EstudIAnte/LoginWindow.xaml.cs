using EstudIAnte.Services;

namespace EstudIAnte;

public partial class LoginWindow : ContentPage
{
    bool? _compactLayout;

    public LoginWindow()
    {
        InitializeComponent();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0)
            return;

        var compact = width < 820;
        if (_compactLayout == compact)
            return;

        _compactLayout = compact;
        LoginLayout.ColumnDefinitions.Clear();
        LoginLayout.RowDefinitions.Clear();

        if (compact)
        {
            LoginLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            LoginLayout.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            LoginLayout.Padding = new Thickness(12);
            BrandPanel.IsVisible = false;
            LoginCard.WidthRequest = -1;
            LoginCard.Padding = new Thickness(20);
            LoginCard.HorizontalOptions = LayoutOptions.Fill;
            Grid.SetColumn(LoginCard, 0);
            Grid.SetRow(LoginCard, 0);
        }
        else
        {
            LoginLayout.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1.15, GridUnitType.Star)));
            LoginLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            LoginLayout.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            LoginLayout.Padding = new Thickness(40);
            BrandPanel.IsVisible = true;
            LoginCard.WidthRequest = 420;
            LoginCard.Padding = new Thickness(36);
            LoginCard.HorizontalOptions = LayoutOptions.Center;
            Grid.SetColumn(BrandPanel, 0);
            Grid.SetColumn(LoginCard, 1);
            Grid.SetRow(BrandPanel, 0);
            Grid.SetRow(LoginCard, 0);
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        EstadoSupabase.Text = "Comprobando conexión con Supabase...";
        EstadoSupabase.TextColor = Color.FromArgb("#94A3B8");

        var (ok, mensaje) = await SupabaseService.TestConnectionAsync();
        EstadoSupabase.Text = (ok ? "🟢 " : "🔴 ") + mensaje;
        EstadoSupabase.TextColor = Color.FromArgb(ok ? "#34D399" : "#F87171");
    }

    // ---------------- LOGIN ----------------

    async void Login_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Usuario.Text) ||
            string.IsNullOrWhiteSpace(Clave.Text))
        {
            await DisplayAlertAsync("Aviso", "Ingresa tu usuario y contraseña.", "OK");
            return;
        }

        var boton = sender as Button;
        if (boton is not null) boton.IsEnabled = false;

        try
        {
            var usuario = Usuario.Text.Trim();
            var clave = Clave.Text;

            var (ok, error) = await AuthService.LoginAsync(usuario, clave, Recordar.IsChecked);

            if (!ok)
            {
                bool crear = await DisplayAlertAsync(
                    "Usuario no encontrado",
                    "No existe la cuenta o la contraseña es incorrecta.\n\n¿Deseas crear la cuenta con estos datos?",
                    "Sí", "No");

                if (!crear)
                    return;

                var (okReg, errorReg) = await AuthService.RegistrarAsync(usuario, clave);
                if (!okReg)
                {
                    await DisplayAlertAsync("Aviso", errorReg, "OK");
                    return;
                }

                await AuthService.LoginAsync(usuario, clave, Recordar.IsChecked);
            }

            await EntrarAsync();
        }
        finally
        {
            if (boton is not null) boton.IsEnabled = true;
        }
    }

    // ---------------- REGISTRO ----------------

    void IrRegistro_Tapped(object? sender, EventArgs e) => MostrarRegistro(true);

    void VolverLogin_Tapped(object? sender, EventArgs e) => MostrarRegistro(false);

    void MostrarRegistro(bool registro)
    {
        PanelLogin.IsVisible = !registro;
        PanelRegistro.IsVisible = registro;

        EstadoSupabase.Text = registro
            ? "La cuenta se creará en la nube y quedará disponible en todos tus dispositivos."
            : "Comprobando conexión con Supabase...";
        EstadoSupabase.TextColor = Color.FromArgb(registro ? "#22D3EE" : "#94A3B8");
    }

    async void Registrar_Click(object? sender, EventArgs e)
    {
        var usuario = RegUsuario.Text?.Trim() ?? string.Empty;
        var email = RegEmail.Text?.Trim();
        var clave = RegClave.Text ?? string.Empty;
        var clave2 = RegClave2.Text ?? string.Empty;

        if (usuario.Length < 3)
        {
            await DisplayAlertAsync("Aviso", "El nombre de usuario debe tener al menos 3 caracteres.", "OK");
            return;
        }

        if (!string.IsNullOrEmpty(email) && !email.Contains('@'))
        {
            await DisplayAlertAsync("Aviso", "Ingresa un correo válido (o déjalo vacío).", "OK");
            return;
        }

        if (clave.Length < 4)
        {
            await DisplayAlertAsync("Aviso", "La contraseña debe tener al menos 4 caracteres.", "OK");
            return;
        }

        if (clave != clave2)
        {
            await DisplayAlertAsync("Aviso", "Las contraseñas no coinciden.", "OK");
            return;
        }

        var boton = sender as Button;
        if (boton is not null) boton.IsEnabled = false;

        try
        {
            EstadoSupabase.Text = "⏳ Creando cuenta...";
            EstadoSupabase.TextColor = Color.FromArgb("#FBBF24");

            var (ok, error) = await AuthService.RegistrarAsync(usuario, clave, email);
            if (!ok)
            {
                EstadoSupabase.Text = "🔴 " + error;
                EstadoSupabase.TextColor = Color.FromArgb("#F87171");
                await DisplayAlertAsync("No se pudo registrar", error, "OK");
                return;
            }

            // Inicia sesión automáticamente con la cuenta recién creada.
            await AuthService.LoginAsync(usuario, clave, Recordar.IsChecked);

            await EntrarAsync();
        }
        finally
        {
            if (boton is not null) boton.IsEnabled = true;
        }
    }

    async void Olvidaste_Tapped(object? sender, EventArgs e)
    {
        await DisplayAlertAsync("Recuperar contraseña",
            "Si tienes conexión, tu contraseña se guarda en la nube del sistema.\n\n" +
            "Pide al administrador que la revise desde el panel de Supabase " +
            "(tabla «usuarios») o prueba con tus datos habituales.\n\n" +
            "Cuenta de ejemplo: admin / admin123", "OK");
    }

    // ---------------- ENTRADA CON SINCRONIZACIÓN ----------------

    /// <summary>
    /// Sincroniza (sube lo local y baja lo de la nube) antes de entrar, para que
    /// en otro dispositivo con la misma cuenta aparezca todo el contenido.
    /// </summary>
    async Task EntrarAsync()
    {
        EstadoSupabase.Text = "🔄 Sincronizando tus datos...";
        EstadoSupabase.TextColor = Color.FromArgb("#22D3EE");

        var (ok, mensaje) = await SyncService.SincronizarTodoAsync();

        EstadoSupabase.Text = (ok ? "🟢 " : "⚠ ") + mensaje;
        EstadoSupabase.TextColor = Color.FromArgb(ok ? "#34D399" : "#FBBF24");

        Application.Current!.Windows[0].Page = new AppShell();
    }

    void Ver_Click(object? sender, EventArgs e) => Clave.IsPassword = !Clave.IsPassword;
}
