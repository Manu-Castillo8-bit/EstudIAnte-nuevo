using EstudIAnte.Services;
using Microsoft.Maui.Layouts;

namespace EstudIAnte
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
            ActualizarUsuario();
        }

        bool _compactLayout;

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            if (width <= 0 || _compactLayout == (width < 900))
                return;

            _compactLayout = width < 900;
            MainLayout.ColumnDefinitions.Clear();
            MainLayout.RowDefinitions.Clear();

            if (_compactLayout)
            {
                MainLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                MainLayout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                MainLayout.RowDefinitions.Add(new RowDefinition(GridLength.Star));

                NavigationPanel.Padding = new Thickness(6, 4);
                NavigationPanel.HeightRequest = 60;
                NavigationPanel.RowDefinitions.Clear();
                NavigationPanel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                BrandHeader.IsVisible = false;
                UserCard.IsVisible = false;
                Grid.SetRow(NavigationScroll, 0);
                Grid.SetRow(NavigationPanel, 0);
                Grid.SetColumn(NavigationPanel, 0);
                Grid.SetRow(ContentPanel, 1);
                Grid.SetColumn(ContentPanel, 0);

                NavItems.Direction = FlexDirection.Row;
                NavigationScroll.Orientation = ScrollOrientation.Horizontal;
                TopBar.Padding = new Thickness(12, 10, 12, 8);
                TopBar.ColumnSpacing = 8;
            }
            else
            {
                MainLayout.ColumnDefinitions.Add(new ColumnDefinition(220));
                MainLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                MainLayout.RowDefinitions.Add(new RowDefinition(GridLength.Star));

                NavigationPanel.Padding = new Thickness(14);
                NavigationPanel.HeightRequest = -1;
                NavigationPanel.RowDefinitions.Clear();
                NavigationPanel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                NavigationPanel.RowDefinitions.Add(new RowDefinition(GridLength.Star));
                NavigationPanel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                BrandHeader.IsVisible = true;
                UserCard.IsVisible = true;
                Grid.SetRow(BrandHeader, 0);
                Grid.SetRow(NavigationScroll, 1);
                Grid.SetRow(UserCard, 2);
                Grid.SetRow(NavigationPanel, 0);
                Grid.SetColumn(NavigationPanel, 0);
                Grid.SetRow(ContentPanel, 0);
                Grid.SetColumn(ContentPanel, 1);

                NavItems.Direction = FlexDirection.Column;
                NavigationScroll.Orientation = ScrollOrientation.Vertical;
                TopBar.Padding = new Thickness(28, 22, 28, 14);
                TopBar.ColumnSpacing = 16;
            }

            foreach (var item in new[] { N1, N2, N3, N4, N5, N6, N7, N8 })
            {
                item.WidthRequest = _compactLayout ? 150 : -1;
                item.HeightRequest = _compactLayout ? 48 : -1;
            }
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
