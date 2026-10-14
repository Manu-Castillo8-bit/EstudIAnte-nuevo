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
            return new Window(new IntroPage())
            {
                Title = "EstudIAnte",
                Width = 1100,
                Height = 680
            };
        }
    }
}
