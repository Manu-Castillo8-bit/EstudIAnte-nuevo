using EstudIAnte.Services;

namespace EstudIAnte
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
            ActualizarUsuario();
        }

        void ActualizarUsuario()
        {
            var u = AuthService.UsuarioActual;
            if (u is null)
                return;

            var nombre = !string.IsNullOrWhiteSpace(u.Nombre) ? u.Nombre : u.NombreUsuario;
            NombreUsuario.Text = nombre;
            RolUsuario.Text = u.Rol == "admin" ? "Administrador" : "Estudiante";
            var inicial = string.IsNullOrEmpty(nombre) ? "?" : nombre[..1].ToUpperInvariant();
            AvatarInicial.Text = inicial;
            AvatarSuperior.Text = inicial;
        }

        private void OnNavChanged(object? sender, CheckedChangedEventArgs e)
        {
            if (sender is not RadioButton radioButton || !radioButton.IsChecked)
                return;

            VInicio.IsVisible = ReferenceEquals(radioButton, N1);
            VChat.IsVisible = ReferenceEquals(radioButton, N2);
            VQuiz.IsVisible = ReferenceEquals(radioButton, N3);
            VDocs.IsVisible = ReferenceEquals(radioButton, N4);
        }
    }
}
