using Microsoft.Maui.Controls.Shapes;
using System.Diagnostics;
namespace EstudIAnte;

public partial class IntroPage : ContentPage
{
    class Tile
    {
        public Border View = null!;
        public double Angle;   // posición inicial en la órbita (grados)
        public double Appear;  // 0 a 1 (animación de salida desde el logo)
        public double Phase;   // desfase del movimiento flotante
    }

    // (imagen, ángulo, fondo claro?)
    static readonly (string Img, double Angle, bool Light)[] Defs =
    {
        ("chatgpt.png",    -100, true),
        ("perplexity.png",  -35, false),
        ("claude.png",       30, true),
        ("figma.png",        95, false),
        ("qwen.png",        160, true),
        ("notion.png",      225, false),
    };

    readonly List<Tile> _tiles = new();
    readonly Stopwatch _clock = new();
    IDispatcherTimer? _timer;
    double _radius, _size;
    bool _started, _closed;

    public IntroPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_started) return;
        _started = true;

        // espera a conocer el tamaño de pantalla
        while (Width <= 0 || Height <= 0) await Task.Delay(50);

        _radius = Math.Min(Math.Min(Width, Height) * 0.36, 230);
        _size = Math.Clamp(_radius * 0.45, 60, 100);
        Logo.WidthRequest = Logo.HeightRequest = Math.Clamp(_radius * 0.7, 100, 160);

        BuildTiles();

        _clock.Start();
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += (_, _) => UpdateScene();
        _timer.Start();

        await RunIntroAsync();
    }

    void BuildTiles()
    {
        var rnd = new Random(7);
        foreach (var d in Defs)
        {
            var view = new Border
            {
                WidthRequest = _size,
                HeightRequest = _size,
                BackgroundColor = Color.FromArgb(d.Light ? "#F3F3F5" : "#0F0F12"),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = (float)(_size * 0.28) },
                Shadow = new Shadow { Brush = Brush.Black, Opacity = 0.45f, Radius = 20, Offset = new Point(0, 10) },
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Opacity = 0,
                Scale = 0.2,
                Content = new Image
                {
                    Source = d.Img,
                    WidthRequest = _size * 0.52,
                    HeightRequest = _size * 0.52,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
            };

            Stage.Children.Insert(0, view);   // detrás del logo
            _tiles.Add(new Tile { View = view, Angle = d.Angle, Phase = rnd.NextDouble() * 6 });
        }
    }

    // Se ejecuta ~60 veces por segundo: mueve la órbita y el flote de cada tarjeta
    void UpdateScene()
    {
        double t = _clock.Elapsed.TotalSeconds;
        double rotation = t * 8;   // 360 grados cada 45 s

        foreach (var tile in _tiles)
        {
            double r = _radius * tile.Appear;
            double a = (tile.Angle + rotation) * Math.PI / 180;
            double bob = Math.Sin(t * 1.6 + tile.Phase) * 6 * tile.Appear;

            tile.View.TranslationX = r * Math.Cos(a);
            tile.View.TranslationY = r * Math.Sin(a) + bob;
            tile.View.Scale = 0.2 + 0.8 * Math.Min(tile.Appear, 1.1);
            tile.View.Opacity = Math.Min(1, tile.Appear * 1.5);
        }

        if (Logo.Opacity > 0.9)
            Logo.TranslationY = Math.Sin(t * 1.3) * 6;
    }

    async Task RunIntroAsync()
    {
        _ = PulseGlowAsync();
        _ = Glow.FadeTo(1, 2000);

        await Task.Delay(300);
        await Task.WhenAll(Logo.FadeTo(1, 800), Logo.ScaleTo(1, 1400, Easing.SpringOut));

        // Las herramientas salen una por una desde el logo
        for (int i = 0; i < _tiles.Count; i++)
        {
            var tile = _tiles[i];
            await Task.Delay(200);
            new Animation(v => tile.Appear = v, 0, 1, Easing.SpringOut)
                .Commit(this, $"tile{i}", 16, 1500);
        }

        await Task.Delay(500);
        await TitleLabel.FadeTo(1, 900);
        await SubLabel.FadeTo(1, 700);
        await EnterBtn.FadeTo(1, 700);

        await Task.Delay(5000);   // entra solo después de unos segundos
        await EnterAsync();
    }

    async Task PulseGlowAsync()
    {
        while (!_closed)
        {
            await Glow.ScaleTo(1.15, 2000, Easing.SinInOut);
            await Glow.ScaleTo(1.0, 2000, Easing.SinInOut);
        }
    }

    async void OnEnterClicked(object? sender, EventArgs e) => await EnterAsync();

    async Task EnterAsync()
    {
        if (_closed) return;
        _closed = true;

        EnterBtn.IsEnabled = false;
        SkipBtn.IsEnabled = false;

        // 1) Se oscurece la pantalla y aparece "Bienvenido a EstudIAnte"
        Welcome.InputTransparent = false;
        await Welcome.FadeTo(1, 900);
        _timer?.Stop();   // la escena ya está tapada; se detiene la animación

        await Task.WhenAll(
            WelcomeContent.FadeTo(1, 800),
            WelcomeContent.ScaleTo(1, 800, Easing.CubicOut));

        await Task.Delay(2200);   // tiempo para leer el mensaje

        // 2) Desvanece todo y pasa al login
        await Root.FadeTo(0, 700);

        var window = Application.Current?.Windows.FirstOrDefault();
        if (window != null) window.Page = new LoginWindow();
    }
}
