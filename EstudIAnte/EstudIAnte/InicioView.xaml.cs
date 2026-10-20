using EstudIAnte.Services;

namespace EstudIAnte;

public partial class InicioView : ContentView
{
    public InicioView()
    {
        InitializeComponent();
        Loaded += (_, _) => ActualizarSaludo();
    }

    void ActualizarSaludo()
    {
        var u = AuthService.UsuarioActual;
        if (u is null)
            return;

        var nombre = !string.IsNullOrWhiteSpace(u.Nombre) ? u.Nombre : u.NombreUsuario;
        SaludoNombre.Text = $"¡Hola, {nombre}!";
    }
}
