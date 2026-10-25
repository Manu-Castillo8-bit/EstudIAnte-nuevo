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

    void Card_Chat(object? sender, TappedEventArgs e) => MainPage.Actual?.IrA("chat");

    void Card_Quiz(object? sender, TappedEventArgs e) => MainPage.Actual?.IrA("quiz");

    void Card_Diagramas(object? sender, TappedEventArgs e) => _ = MainPage.Actual?.IrADiagramasAsync();

    void Card_Documentos(object? sender, TappedEventArgs e) => MainPage.Actual?.IrA("documentos");
}
