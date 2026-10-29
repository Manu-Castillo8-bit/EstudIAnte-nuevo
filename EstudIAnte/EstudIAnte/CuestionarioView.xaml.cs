using EstudIAnte.Models;
using EstudIAnte.Services;
using Microsoft.Maui.Controls.Shapes;

namespace EstudIAnte;

public partial class CuestionarioView : ContentView
{
    enum Fuente { Tema, Documento }

    Fuente _fuente = Fuente.Tema;
    Cuestionario? _cuestionario;
    List<Pregunta> _preguntas = new();
    readonly Dictionary<int, string> _respuestas = new();
    readonly HashSet<int> _reveladas = new();
    int _indice;
    bool _generando;
    bool? _compactLayout;
    CancellationTokenSource? _generacionCts;

    // Cronómetro de la tarjeta de resumen.
    DateTime _inicioQuiz;
    IDispatcherTimer? _cronometro;
    bool _quizFinalizado;

    public CuestionarioView()
    {
        InitializeComponent();
        SelectorCantidad.SelectedIndex = 0;
        Loaded += (_, _) => _ = InicializarAsync();
    }

    async void SincronizarQuiz_Click(object? sender, EventArgs e)
    {
        try
        {
            BtnSincronizarQuiz.IsBusy = true;
            var (ok, msg) = await SyncService.SincronizarTodoAsync();
            await CargarFuenteAsync();
            if (_cuestionario is not null)
                await CargarPreguntasAsync();
            await MostrarAsync("Sincronización", msg);
        }
        finally
        {
            BtnSincronizarQuiz.IsBusy = false;
        }
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0)
            return;

        var compact = width < 760;
        if (_compactLayout == compact)
            return;

        _compactLayout = compact;
        QuizLayout.Padding = compact
            ? new Thickness(14, 8, 14, 20)
            : new Thickness(28, 8, 28, 28);

        SourceGrid.ColumnDefinitions.Clear();
        SourceGrid.RowDefinitions.Clear();
        QuizPanels.ColumnDefinitions.Clear();
        QuizPanels.RowDefinitions.Clear();

        if (compact)
        {
            SourceGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            SourceGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            SourceGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            SourceGrid.SetRow(BtnGenerar, 1);
            SourceGrid.SetColumn(BtnGenerar, 0);

            QuizPanels.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            QuizPanels.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            QuizPanels.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            QuizPanels.SetRow(QuizPanels.Children[0], 0);
            QuizPanels.SetColumn(QuizPanels.Children[0], 0);
            QuizPanels.SetRow(QuizPanels.Children[1], 1);
            QuizPanels.SetColumn(QuizPanels.Children[1], 0);

            PreguntaTexto.FontSize = 18;
        }
        else
        {
            SourceGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            SourceGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            SourceGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            SourceGrid.SetRow(BtnGenerar, 0);
            SourceGrid.SetColumn(BtnGenerar, 1);

            QuizPanels.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            QuizPanels.ColumnDefinitions.Add(new ColumnDefinition(280));
            QuizPanels.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            QuizPanels.SetRow(QuizPanels.Children[0], 0);
            QuizPanels.SetColumn(QuizPanels.Children[0], 0);
            QuizPanels.SetRow(QuizPanels.Children[1], 0);
            QuizPanels.SetColumn(QuizPanels.Children[1], 1);

            PreguntaTexto.FontSize = 21;
        }
    }

    async Task InicializarAsync()
    {
        await CargarFuenteAsync();

        // Carga el último cuestionario existente, si lo hay.
        var usuario = AuthService.UsuarioActual;
        if (usuario is null)
            return;

        var conn = await LocalDatabase.GetAsync();
        _cuestionario = await conn.Table<Cuestionario>()
            .Where(c => c.UsuarioId == usuario.Id)
            .OrderByDescending(c => c.CreadoEn)
            .FirstOrDefaultAsync();

        if (_cuestionario is not null)
            await CargarPreguntasAsync();
    }

    // ---------------- GENERADOR ----------------

    void Fuente_Tema(object? sender, EventArgs e)
    {
        _fuente = Fuente.Tema;
        LblFuenteSel.Text = "Fuente: tema de conversación";
        _ = CargarFuenteAsync();
    }

    void Fuente_Doc(object? sender, EventArgs e)
    {
        _fuente = Fuente.Documento;
        LblFuenteSel.Text = "Fuente: documento analizado";
        _ = CargarFuenteAsync();
    }

    async Task CargarFuenteAsync()
    {
        var usuario = AuthService.UsuarioActual;
        if (usuario is null)
            return;

        var conn = await LocalDatabase.GetAsync();
        Selector.Items.Clear();
        _opciones.Clear();

        if (_fuente == Fuente.Tema)
        {
            var temas = await TemasService.ListarAsync(usuario.Id);
            if (temas.Count == 0)
            {
                LblEstadoGen.Text = "No tienes temas. Créalos en la sección Chat Temático.";
                return;
            }

            foreach (var t in temas)
            {
                _opciones.Add((t.Id, t.Nombre));
                Selector.Items.Add("💬  " + t.Nombre);
            }
            LblEstadoGen.Text = $"{temas.Count} tema(s) disponible(s).";
        }
        else
        {
            var docs = await conn.Table<Documento>()
                .Where(d => d.UsuarioId == usuario.Id && d.Estado == "listo")
                .OrderByDescending(d => d.CreadoEn)
                .ToListAsync();

            if (docs.Count == 0)
            {
                LblEstadoGen.Text = "No hay documentos analizados. Súbelos y usa «Analizar con IA».";
                return;
            }

            foreach (var d in docs)
            {
                _opciones.Add((d.Id, d.Nombre));
                Selector.Items.Add("📄  " + d.Nombre);
            }
            LblEstadoGen.Text = $"{docs.Count} documento(s) analizado(s) disponible(s).";
        }

        if (Selector.Items.Count > 0)
            Selector.SelectedIndex = 0;
    }

    readonly List<(string Id, string Nombre)> _opciones = new();

    async void Generar_Click(object? sender, EventArgs e)
    {
        if (_generando)
            return;

        var usuario = AuthService.UsuarioActual;
        if (usuario is null)
        {
            await MostrarAsync("Aviso", "Debes iniciar sesión.");
            return;
        }

        if (Selector.SelectedIndex < 0 || Selector.SelectedIndex >= _opciones.Count)
        {
            await MostrarAsync("Aviso", "Elige una fuente para generar el cuestionario.");
            return;
        }

        var (id, nombre) = _opciones[Selector.SelectedIndex];

        // Cantidad de preguntas elegida por el usuario (5/10/15/20).
        var cantidad = 5;
        if (SelectorCantidad.SelectedItem is string texto && int.TryParse(texto, out var elegida))
            cantidad = elegida;

        var conn = await LocalDatabase.GetAsync();

        string material;
        string? documentoId = null;
        string fuenteTexto;

        if (_fuente == Fuente.Tema)
        {
            var mensajes = await ObtenerMensajesDeTemaAsync(nombre);
            if (string.IsNullOrWhiteSpace(mensajes))
            {
                await MostrarAsync("Aviso", "Ese tema aún no tiene conversación. Chatea un poco primero.");
                return;
            }
            material = mensajes;
            fuenteTexto = $"Tema: {nombre}";
        }
        else
        {
            var doc = await conn.Table<Documento>().Where(d => d.Id == id).FirstOrDefaultAsync();
            if (doc is null || string.IsNullOrWhiteSpace(doc.Texto))
            {
                await MostrarAsync("Aviso", "El documento no tiene texto analizado.");
                return;
            }
            material = doc.Texto;
            documentoId = doc.Id;
            fuenteTexto = $"Documento: {doc.Nombre}";
        }

        var consumo = await UsoIAService.ConsumirAsync(usuario.Id, AccionIA.Cuestionario);
        if (!consumo.Permitido)
        {
            await MostrarAsync("Límite de IA", consumo.Mensaje);
            return;
        }

        var completado = false;

        _generando = true;
        BtnGenerar.IsBusy = true;
        BtnCancelarGen.IsVisible = true;
        SpinnerGen.IsRunning = SpinnerGen.IsVisible = true;
        LblEstadoGen.Text = "Generando con IA, espera un momento...";
        _generacionCts = new CancellationTokenSource();

        try
        {
            var (quiz, ia) = await QuizGenerator.GenerarAsync(
                usuario.Id, nombre, fuenteTexto, material, documentoId, cantidad, _generacionCts.Token);
            completado = true;
            _cuestionario = quiz;
            ResumenIA.Text = ia;
            LblEstadoGen.Text = $"Cuestionario generado con {ia}.";
            await CargarPreguntasAsync();
        }
        catch (OperationCanceledException) when (_generacionCts?.IsCancellationRequested == true)
        {
            LblEstadoGen.Text = "Generación cancelada.";
        }
        catch (Exception ex)
        {
            LblEstadoGen.Text = "No se pudo generar.";
            await MostrarAsync("No se pudo generar", ex.Message);
        }
        finally
        {
            _generando = false;
            BtnGenerar.IsBusy = false;
            BtnCancelarGen.IsVisible = false;
            SpinnerGen.IsRunning = SpinnerGen.IsVisible = false;
            _generacionCts?.Dispose();
            _generacionCts = null;
            if (!completado)
                await consumo.DevolverAsync();
        }
    }

    void CancelarGeneracion_Click(object? sender, EventArgs e) => _generacionCts?.Cancel();

    async Task<string> ObtenerMensajesDeTemaAsync(string temaNombre)
    {
        var usuario = AuthService.UsuarioActual!;
        var conn = await LocalDatabase.GetAsync();

        var chat = await conn.Table<Chat>()
            .Where(c => c.UsuarioId == usuario.Id && c.Tema == temaNombre)
            .OrderByDescending(c => c.CreadoEn)
            .FirstOrDefaultAsync();

        if (chat is null)
            return string.Empty;

        var mensajes = await conn.Table<Mensaje>()
            .Where(m => m.ChatId == chat.Id)
            .OrderBy(m => m.CreadoEn)
            .ToListAsync();

        return string.Join("\n", mensajes.Select(m => $"{(m.Rol == "user" ? "Estudiante" : "Tutor")}: {m.Contenido}"));
    }

    // ---------------- CUESTIONARIO ----------------

    /// <summary>Abre un cuestionario existente por su Id.</summary>
    public async Task AbrirCuestionarioAsync(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;
        var conn = await LocalDatabase.GetAsync();
        var quiz = await conn.Table<Cuestionario>().Where(c => c.Id == id).FirstOrDefaultAsync();
        if (quiz is null)
            return;
        _cuestionario = quiz;
        ResumenIA.Text = string.Empty;
        await CargarPreguntasAsync();
    }

    async Task CargarPreguntasAsync()
    {
        if (_cuestionario is null)
            return;

        var conn = await LocalDatabase.GetAsync();
        _preguntas = await conn.Table<Pregunta>()
            .Where(p => p.CuestionarioId == _cuestionario.Id)
            .ToListAsync();

        _respuestas.Clear();
        _reveladas.Clear();
        _indice = 0;

        ResumenPreguntas.Text = _preguntas.Count.ToString();
        TituloQuiz.Text = _cuestionario.Titulo;
        BadgeNivel.Text = "Nivel: " + _cuestionario.Nivel;

        // El cronómetro arranca (o se reinicia) cada vez que se carga un cuestionario.
        _quizFinalizado = false;
        IniciarCronometro();

        // Si el cuestionario ya tenía respuestas guardadas en esta sesión, muestra el puntaje real.
        ActualizarPuntaje();
        ResumenPuntaje.TextColor = Color.FromArgb("#EC4899");

        MostrarPregunta();
    }

    // ---------------- CRONÓMETRO ----------------

    void IniciarCronometro()
    {
        DetenerCronometro();
        _inicioQuiz = DateTime.Now;

        _cronometro = Application.Current?.Dispatcher.CreateTimer();
        if (_cronometro is null)
        {
            ActualizarTiempoVisual(DateTime.Now - _inicioQuiz);
            return;
        }

        _cronometro.Interval = TimeSpan.FromSeconds(1);
        _cronometro.Tick += (_, _) =>
        {
            if (_quizFinalizado)
            {
                DetenerCronometro();
                return;
            }
            ActualizarTiempoVisual(DateTime.Now - _inicioQuiz);
        };
        _cronometro.Start();
        ActualizarTiempoVisual(TimeSpan.Zero);
    }

    void DetenerCronometro()
    {
        _cronometro?.Stop();
        _cronometro = null;
    }

    void ActualizarTiempoVisual(TimeSpan transcurrido)
    {
        ResumenTiempo.Text = transcurrido.TotalHours >= 1
            ? $"{(int)transcurrido.TotalHours}:{transcurrido.Minutes:00}:{transcurrido.Seconds:00}"
            : $"{(int)transcurrido.TotalMinutes:00}:{transcurrido.Seconds:00}";
    }

    /// <summary>Cantidad de respuestas correctas marcadas hasta ahora.</summary>
    int CorrectasCount() => _respuestas.Count(kv =>
        kv.Key < _preguntas.Count && _preguntas[kv.Key].RespuestaCorrecta == kv.Value);

    /// <summary>
    /// Actualiza el puntaje de la tarjeta de resumen al instante y lo resalta
    /// en verde si la última respuesta fue correcta, o en rosa si fue incorrecta.
    /// </summary>
    void ActualizarPuntaje(bool? ultimaFueCorrecta = null)
    {
        var correctas = CorrectasCount();
        ResumenPuntaje.Text = $"{correctas} / {_preguntas.Count}";

        if (ultimaFueCorrecta is true)
            ResumenPuntaje.TextColor = Color.FromArgb("#10B981");
        else if (ultimaFueCorrecta is false)
            ResumenPuntaje.TextColor = Color.FromArgb("#EC4899");
    }

    void MostrarPregunta()
    {
        if (_preguntas.Count == 0)
        {
            PreguntaTexto.Text = "Genera un cuestionario arriba para comenzar.";
            OpcionesPanel.Clear();
            ProgresoTexto.Text = "Sin cuestionario activo";
            ProgresoPct.Text = "0%";
            BarraProgreso.Progress = 0;
            return;
        }

        var p = _preguntas[_indice];
        PreguntaTexto.Text = p.Enunciado;
        TipTexto.Text = string.IsNullOrWhiteSpace(p.Tip) ? "Lee con atención cada pregunta." : p.Tip;

        var total = _preguntas.Count;
        ProgresoTexto.Text = $"Pregunta {_indice + 1} de {total}";
        var pct = (double)(_indice + 1) / total;
        BarraProgreso.Progress = pct;
        ProgresoPct.Text = $"{(int)(pct * 100)}%";

        OpcionesPanel.Clear();
        var opciones = new (string Valor, string Texto)[]
        {
            ("A", p.OpcionA), ("B", p.OpcionB), ("C", p.OpcionC), ("D", p.OpcionD)
        };

        var revelada = _reveladas.Contains(_indice);

        foreach (var (valor, texto) in opciones)
        {
            var fila = CrearFilaOpcion(valor, texto, p.RespuestaCorrecta, revelada,
                _respuestas.TryGetValue(_indice, out var elegida) && elegida == valor);

            var tap = new TapGestureRecognizer { CommandParameter = valor };
            tap.Tapped += (_, _) => SeleccionarOpcion(valor);
            fila.GestureRecognizers.Add(tap);

            OpcionesPanel.Add(fila);
        }

        BtnAnterior.IsEnabled = _indice > 0;
        BtnSiguiente.Text = _indice == total - 1 ? "Finalizar  ✓" : "Siguiente  →";
    }

    /// <summary>
    /// Al tocar una opción se revela la respuesta: la correcta se pinta en verde
    /// y la elegida incorrecta en rojo. La pregunta queda bloqueada al revelarse.
    /// </summary>
    void SeleccionarOpcion(string valor)
    {
        if (_reveladas.Contains(_indice))
            return; // Ya se respondió esta pregunta.

        _respuestas[_indice] = valor;
        _reveladas.Add(_indice);

        // Marca el resultado al instante en la tarjeta de resumen.
        var esCorrecta = _indice < _preguntas.Count && valor == _preguntas[_indice].RespuestaCorrecta;
        ActualizarPuntaje(esCorrecta);

        MostrarPregunta();
    }

    /// <summary>
    /// Crea la fila visual de una opción (A, B, C o D). Cuando la pregunta ya
    /// fue respondida: la opción correcta se muestra en verde, la incorrecta
    /// elegida en rojo y las demás se atenúan.
    /// </summary>
    static Border CrearFilaOpcion(string valor, string texto, string correcta, bool revelada, bool elegida)
    {
        var esCorrecta = valor == correcta;

        // Estados visuales según el resultado.
        var fondo = Color.FromArgb("#161F4F");
        var borde = Color.FromArgb("#243066");
        var fondoBadge = Color.FromArgb("#222D6E");
        var colorLetra = Color.FromArgb("#E8ECFF");
        string icono = "";
        var colorIcono = Colors.Transparent;

        if (revelada && esCorrecta)
        {
            fondo = Color.FromArgb("#0F3D2E");
            borde = Color.FromArgb("#10B981");
            fondoBadge = Color.FromArgb("#10B981");
            colorLetra = Color.FromArgb("#052E1F");
            icono = "✓";
            colorIcono = Color.FromArgb("#10B981");
        }
        else if (revelada && elegida && !esCorrecta)
        {
            fondo = Color.FromArgb("#3F1D2B");
            borde = Color.FromArgb("#EC4899");
            fondoBadge = Color.FromArgb("#EC4899");
            colorLetra = Colors.White;
            icono = "✗";
            colorIcono = Color.FromArgb("#EC4899");
        }

        var badge = new Border
        {
            WidthRequest = 34,
            HeightRequest = 34,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 17 },
            BackgroundColor = fondoBadge,
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = valor,
                FontAttributes = FontAttributes.Bold,
                TextColor = colorLetra,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };

        var fila = new Border
        {
            Padding = new Thickness(16, 12),
            Margin = new Thickness(0, 5),
            BackgroundColor = fondo,
            Stroke = borde,
            StrokeThickness = 1.5,
            StrokeShape = new RoundRectangle { CornerRadius = 14 }
        };

        if (revelada && !esCorrecta && !elegida)
            fila.Opacity = 0.55;

        var grid = new Grid
        {
            ColumnSpacing = 14,
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            }
        };
        grid.Add(badge);
        grid.Add(new Label
        {
            Text = texto,
            FontSize = 16,
            TextColor = Color.FromArgb("#E8ECFF"),
            VerticalOptions = LayoutOptions.Center
        }, 1);

        if (icono.Length > 0)
        {
            grid.Add(new Label
            {
                Text = icono,
                FontSize = 20,
                FontAttributes = FontAttributes.Bold,
                TextColor = colorIcono,
                VerticalOptions = LayoutOptions.Center
            }, 2);
        }

        fila.Content = grid;
        return fila;
    }

    void Anterior_Click(object? sender, EventArgs e)
    {
        if (_indice > 0)
        {
            _indice--;
            MostrarPregunta();
        }
    }

    async void Siguiente_Click(object? sender, EventArgs e)
    {
        if (_preguntas.Count == 0)
            return;

        if (_indice < _preguntas.Count - 1)
        {
            _indice++;
            MostrarPregunta();
            return;
        }

        await FinalizarAsync();
    }

    async Task FinalizarAsync()
    {
        if (_quizFinalizado)
            return;

        // Detiene el cronómetro y guarda el tiempo real invertido.
        _quizFinalizado = true;
        DetenerCronometro();
        var transcurrido = DateTime.Now - _inicioQuiz;
        ActualizarTiempoVisual(transcurrido);

        var correctas = 0;
        for (var i = 0; i < _preguntas.Count; i++)
        {
            if (_respuestas.TryGetValue(i, out var r) && r == _preguntas[i].RespuestaCorrecta)
                correctas++;
        }

        var usuario = AuthService.UsuarioActual;
        if (usuario is not null)
        {
            var resultado = new Resultado
            {
                CuestionarioId = _cuestionario!.Id,
                UsuarioId = usuario.Id,
                Puntaje = correctas,
                Total = _preguntas.Count,
                TiempoSegundos = (int)transcurrido.TotalSeconds
            };

            var conn = await LocalDatabase.GetAsync();
            await conn.InsertAsync(resultado);

            var client = await SupabaseService.GetClientAsync();
            if (client is not null)
            {
                try { await client.From<Resultado>().Insert(resultado); }
                catch { /* se sincroniza después */ }
            }
        }

        ActualizarPuntaje(correctas == _preguntas.Count && _preguntas.Count > 0 ? true : null);
        ProgresoPct.Text = "100%";
        ProgresoTexto.Text = $"Cuestionario finalizado en {ResumenTiempo.Text}";

        await MostrarAsync("Resultado",
            $"Obtuviste {correctas} de {_preguntas.Count} respuestas correctas.\nTiempo: {ResumenTiempo.Text}");
    }

    Task MostrarAsync(string titulo, string mensaje)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        return page is null ? Task.CompletedTask : page.DisplayAlertAsync(titulo, mensaje, "OK");
    }
}
