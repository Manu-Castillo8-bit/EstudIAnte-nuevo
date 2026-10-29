using System.Diagnostics;
using EstudIAnte.Services;

namespace EstudIAnte
{
    public partial class App : Application
    {
        /// <summary>
        /// Restaura la sesión guardada sin retrasar la aparición del splash nativo
        /// ni la pantalla de bienvenida.
        /// </summary>
        public Task InicializacionSesion { get; private set; } = Task.CompletedTask;

        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            InicializacionSesion = InicializarSesionAsync();

            // La pantalla de bienvenida siempre es la primera vista tras el splash.
            // IntroPage decide después si continúa al login o directamente al panel.
            return new Window(new IntroPage())
            {
                Title = "EstudIAnte",
                Width = 1100,
                Height = 680
            };
        }

        static async Task InicializarSesionAsync()
        {
            try
            {
                var usuario = await AuthService.RestaurarSesionAsync();
                if (usuario is not null)
                    _ = SincronizarEnSegundoPlanoAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("No se pudo restaurar la sesión: " + ex.Message);
            }
        }

        static async Task SincronizarEnSegundoPlanoAsync()
        {
            try
            {
                await SyncService.SincronizarTodoAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("No se pudo sincronizar al iniciar: " + ex.Message);
            }
        }
    }
}
