namespace EstudIAnte;

public partial class LoginWindow : ContentPage
{
    public LoginWindow()
    {
        InitializeComponent();
    }

    async void Login_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Usuario.Text) ||
            string.IsNullOrWhiteSpace(Clave.Text))
        {
            await DisplayAlert("Aviso", "Ingresa tu usuario y contraseña.", "OK");
            return;
        }

        // Aquí va tu validación real (base de datos, API, etc.)
        // Por ahora deja pasar a cualquiera con datos escritos:
        Application.Current!.Windows[0].Page = new AppShell();
    }

    void Ver_Click(object? sender, EventArgs e) => Clave.IsPassword = !Clave.IsPassword;
}