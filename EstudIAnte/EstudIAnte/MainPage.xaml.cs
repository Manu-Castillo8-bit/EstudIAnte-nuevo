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
            N8.IsVisible = u.Rol == "admin";
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
            VArchivos.IsVisible = ReferenceEquals(radioButton, N6);
            VConfig.IsVisible = ReferenceEquals(radioButton, N7);
            VAdmin.IsVisible = ReferenceEquals(radioButton, N8);

            // El panel admin solo se ve para administradores.
            if (ReferenceEquals(radioButton, N8) && AuthService.UsuarioActual?.Rol != "admin")
            {
                IrA("inicio");
                return;
            }

            // Refresca las listas cada vez que se entra a la sección.
            if (ReferenceEquals(radioButton, N1))
                _ = VInicio.RefrescarAsync();
            if (ReferenceEquals(radioButton, N5))
                _ = VDiag.RefrescarAsync();
            if (ReferenceEquals(radioButton, N4))
                _ = VDocs.RefrescarAsync();
            if (ReferenceEquals(radioButton, N6))
                _ = VArchivos.RefrescarAsync();
            if (ReferenceEquals(radioButton, N7))
                _ = VConfig.RefrescarAsync();
            if (ReferenceEquals(radioButton, N8))
                _ = VAdmin.RefrescarAsync();
        }

        /// <summary>
        /// La búsqueda global filtra Mis Archivos (documentos y diagramas) en vivo.
        /// </summary>
        void BuscadorGlobal_TextChanged(object? sender, TextChangedEventArgs e)
        {
            var texto = BuscadorGlobal.Text?.Trim() ?? string.Empty;
            if (texto.Length == 0)
                return;

            if (!VArchivos.IsVisible)
                IrA("archivos");

            VArchivos.AplicarBusqueda(texto);
        }

        /// <summary>El avatar superior abre la sección de Configuración.</summary>
        void Avatar_Tapped(object? sender, EventArgs e) => IrA("config");

        /// <summary>Acceso público al chat para navegación desde otras vistas.</summary>
    public ChatView VisorChat => VChat;

    /// <summary>Referencia a la MainPage activa, para navegar desde las vistas.</summary>
        public static MainPage? Actual =>
            Application.Current?.Windows.FirstOrDefault()?.Page as MainPage;

        /// <summary>
        /// Cambia a la sección indicada: "inicio", "chat", "quiz", "diagramas", "documentos",
        /// "archivos" o "config".
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
                "archivos" => N6,
                "config" => N7,
                "admin" => AuthService.UsuarioActual?.Rol == "admin" ? N8 : null,
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
