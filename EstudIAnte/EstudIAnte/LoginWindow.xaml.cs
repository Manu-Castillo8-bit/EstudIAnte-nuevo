using EstudIAnte.Services;

namespace EstudIAnte;

public partial class LoginWindow : ContentPage
{
    public LoginWindow()
    {
        InitializeComponent();
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

            // Sincroniza datos de la nube en segundo plano
            _ = SyncService.PullAsync();

            Application.Current!.Windows[0].Page = new AppShell();
        }
        finally
        {
            if (boton is not null) boton.IsEnabled = true;
        }
    }

    void Ver_Click(object? sender, EventArgs e) => Clave.IsPassword = !Clave.IsPassword;
}