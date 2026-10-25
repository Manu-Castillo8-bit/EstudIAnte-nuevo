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
            VDiag.IsVisible = ReferenceEquals(radioButton, N5);
            VDocs.IsVisible = ReferenceEquals(radioButton, N4);

            // Refresca las listas cada vez que se entra a la sección.
            if (ReferenceEquals(radioButton, N5))
                _ = VDiag.RefrescarAsync();
            if (ReferenceEquals(radioButton, N4))
                _ = VDocs.RefrescarAsync();
        }

        /// <summary>Referencia a la MainPage activa, para navegar desde las vistas.</summary>
        public static MainPage? Actual =>
            Application.Current?.Windows.FirstOrDefault()?.Page as MainPage;

        /// <summary>
        /// Cambia a la sección indicada: "inicio", "chat", "quiz", "diagramas" o "documentos".
        /// </summary>
        public void IrA(string seccion)
        {
            RadioButton? destino = seccion switch
            {
                "inicio" => N1,
                "chat" => N2,
                "quiz" => N3,
                "diagramas" => N5,
                "documentos" => N4,
                _ => null
            };

            if (destino is not null)
                destino.IsChecked = true;
        }

        /// <summary>Navega a Diagramas y preselecciona un documento como fuente.</summary>
        public async Task IrADiagramasAsync(string? documentoId = null)
        {
            IrA("diagramas");
            await VDiag.PrepararAsync(documentoId);
        }
    }
}
