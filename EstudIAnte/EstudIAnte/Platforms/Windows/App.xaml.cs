using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace EstudIAnte.WinUI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : MauiWinUIApplication
    {
        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();

            // Registra cualquier excepción WinUI no controlada en un log de texto
            // (útil para diagnosticar cuelgues al abrir la app).
            this.UnhandledException += (sender, args) =>
            {
                try
                {
                    var carpeta = System.IO.Path.Combine(
                        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                        "EstudIAnte");
                    System.IO.Directory.CreateDirectory(carpeta);
                    var log = System.IO.Path.Combine(carpeta, "crash.log");
                    System.IO.File.AppendAllText(log,
                        $"[{DateTime.Now:HH:mm:ss}] {args.Message}\n{args.Exception}\n\n");
                }
                catch { }
            };
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }

}
