using EstudIAnte.Services;

namespace EstudIAnte
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            // Si hay sesión guardada ("Recordar sesión"), entra directo y sincroniza en segundo plano.
            // Nota: Task.Run evita el interbloqueo de restaurar el contexto de la UI al usar .GetResult().
            var usuario = Task.Run(async () => await AuthService.RestaurarSesionAsync()).GetAwaiter().GetResult();
            if (usuario is not null)
            {
                _ = Task.Run(async () =>
                {
                    await SyncService.SincronizarTodoAsync();
                });

                return new Window(new AppShell())
                {
                    Title = "EstudIAnte",
                    Width = 1100,
                    Height = 680
                };
            }

            return new Window(new IntroPage())
            {
                Title = "EstudIAnte",
                Width = 1100,
                Height = 680
            };
        }
    }
}
