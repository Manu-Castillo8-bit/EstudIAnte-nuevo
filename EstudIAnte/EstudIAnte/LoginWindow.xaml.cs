using EstudIAnte.Services;

namespace EstudIAnte;

public partial class LoginWindow : ContentPage
{
    // Por debajo de este ancho se usa el diseño "teléfono" (sin panel de marca).
    const double AnchoEscritorio = 820;

    bool? _compactLayout;

    public LoginWindow()
    {
        InitializeComponent();
    }

    // ---------------- RESPONSIVO ----------------

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0 || height <= 0)
            return;

        bool compact = width < AnchoEscritorio;

        // 1) Estructura (columnas, filas, visibilidad): solo cuando cambia de modo.
        if (_compactLayout != compact)
        {
            _compactLayout = compact;

            LoginLayout.ColumnDefinitions.Clear();
            LoginLayout.RowDefinitions.Clear();
            LoginLayout.RowDefinitions.Add(new RowDefinition(GridLength.Star));

            if (compact)
            {
                // Teléfono / ventana angosta: solo la tarjeta, a todo el ancho.
                LoginLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                LoginLayout.Padding = new Thickness(12, 8);

                BrandPanel.IsVisible = false;

                LoginCard.Padding = new Thickness(20);
                LoginCard.HorizontalOptions = LayoutOptions.Center;
                LoginCard.VerticalOptions = LayoutOptions.Center;

                Grid.SetColumn(BrandPanel, 0);
                Grid.SetRow(BrandPanel, 0);
                Grid.SetColumn(LoginCard, 0);
                Grid.SetRow(LoginCard, 0);
            }
            else
            {
                // PC: panel de marca a la izquierda y tarjeta a la derecha.
                LoginLayout.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1.15, GridUnitType.Star)));
                LoginLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                LoginLayout.Padding = new Thickness(40);

                BrandPanel.IsVisible = true;

                LoginCard.Padding = new Thickness(36);
                LoginCard.HorizontalOptions = LayoutOptions.Center;
                LoginCard.VerticalOptions = LayoutOptions.Center;

                Grid.SetColumn(BrandPanel, 0);
                Grid.SetRow(BrandPanel, 0);
                Grid.SetColumn(LoginCard, 1);
                Grid.SetRow(LoginCard, 0);
            }
        }

        // 2) Medidas que dependen del tamaño actual (se recalculan en cada cambio).
        double altoDisponible = Math.Max(0, height - LoginLayout.Padding.VerticalThickness);

        // La tarjeta nunca será más alta que la pantalla; si el contenido no cabe, el ScrollView hace scroll.
        LoginCard.MaximumHeightRequest = altoDisponible;

        if (compact)
        {
            double anchoDisponible = Math.Max(0, width - LoginLayout.Padding.HorizontalThickness);
            LoginCard.WidthRequest = Math.Min(anchoDisponible, 460);

            // Alto mínimo del contenido = alto de la tarjeta, así queda centrado verticalmente
            // (filas *,Auto,*) pero si el contenido es más alto (registro) se puede hacer scroll.
            LoginContentGrid.MinimumHeightRequest =
                Math.Max(0, altoDisponible - LoginCard.Padding.VerticalThickness - 4);
        }
        else
        {
            LoginCard.WidthRequest = 420;
            LoginContentGrid.MinimumHeightRequest = -1; // -1 = sin mínimo (valor por defecto de Minimum*)
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

        LoginSpinner.IsVisible = LoginSpinner.IsRunning = true;
        BtnLogin.IsBusy = true;

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
            BtnLogin.IsBusy = false;
            LoginSpinner.IsRunning = LoginSpinner.IsVisible = false;
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

        RegisterSpinner.IsVisible = RegisterSpinner.IsRunning = true;
        BtnRegistrar.IsBusy = true;

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
            BtnRegistrar.IsBusy = false;
            RegisterSpinner.IsRunning = RegisterSpinner.IsVisible = false;
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