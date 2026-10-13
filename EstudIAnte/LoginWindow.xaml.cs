namespace EstudIAnte;

public partial class LoginWindow : ContentPage
{
    public LoginWindow()
    {
        InitializeComponent();
    }

    private async void Login_Click(object? sender, EventArgs e)
    {
        // Efecto fade (equivalente al DoubleAnimation de WPF).
        await Root.FadeToAsync(0, 450, Easing.CubicOut);

        if (Window is not null)
            Window.Page = new MainPage();
    }
}
