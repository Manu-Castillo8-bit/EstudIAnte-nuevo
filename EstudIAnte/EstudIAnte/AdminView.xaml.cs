using System.Globalization;
using EstudIAnte.Models;
using EstudIAnte.Services;

namespace EstudIAnte;

public partial class AdminView : ContentView
{
    string _tab = "usuarios";
    bool _trabajando;
    bool? _compactLayout;
    bool _cargandoConfigIA;

    public AdminView()
    {
        InitializeComponent();
        ConstruirTabs();
        Loaded += (_, _) => _ = RefrescarAsync();
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
        AdminLayout.Padding = compact
            ? new Thickness(14, 8, 14, 20)
            : new Thickness(28, 8, 28, 28);
    }

    // ---------------- PESTAÑAS ----------------

    void ConstruirTabs()
    {
        var tabs = new (string Id, string Titulo)[]
        {
            ("usuarios", "👤  Usuarios"),
            ("documentos", "📁  Documentos"),
            ("cuestionarios", "📋  Cuestionarios"),
            ("diagramas", "🔀  Diagramas"),
            ("resultados", "🏁  Resultados"),
        };

        foreach (var (id, titulo) in tabs)
        {
            var boton = new Controls.PulsarButton
            {
                Text = titulo,
                Style = Res<Style>("ChipButton"),
            };
            boton.CommandParameter = id;
            boton.Clicked += Tab_Click;
            TabsLayout.Children.Add(boton);
        }
    }

    void Tab_Click(object? sender, EventArgs e)
    {
        if ((sender as Controls.PulsarButton)?.CommandParameter is string id)
            _ = EstablecerTabAsync(id);
    }

    async Task EstablecerTabAsync(string tab)
    {
        _tab = tab;
        MarcarTabActivo();
        await CargarTabAsync();
    }

    void MarcarTabActivo()
    {
        var indice = _tab switch
        {
            "documentos" => 1,
            "cuestionarios" => 2,
            "diagramas" => 3,
            "resultados" => 4,
            _ => 0,
        };

        for (var i = 0; i < TabsLayout.Children.Count && TabsLayout.Children[i] is Controls.PulsarButton b; i++)
        {
            var activo = i == indice;
            b.Opacity = activo ? 1 : 0.45;
        }

        PanelUsuarios.IsVisible = _tab == "usuarios";
        PanelDocumentos.IsVisible = _tab == "documentos";
        PanelCuestionarios.IsVisible = _tab == "cuestionarios";
        PanelDiagramas.IsVisible = _tab == "diagramas";
        PanelResultados.IsVisible = _tab == "resultados";
    }

    // ---------------- CARGA ----------------

    async void Refrescar_Click(object? sender, EventArgs e) => await RefrescarAsync();

    // ---------------- DETALLE DE USUARIO ----------------

    async void UsuarioCard_Tapped(object? sender, TappedEventArgs e) =>
        await AbrirDetalleUsuarioAsync(e.Parameter as string);

    void CerrarDetalleUsuario_Click(object? sender, EventArgs e)
    {
        PanelDetalleUsuario.IsVisible = false;
    }

    /// <summary>Borra cualquier fila de las listas de detalle según su tipo ("Clave").</summary>
    async void EliminarDetalle_Click(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not ItemDetalle item)
            return;
        var page = Pagina();
        if (page is null)
            return;

        var confirmar = await page.DisplayAlertAsync("Eliminar",
            $"¿Borrar «{item.Titulo}»? Esta acción no se puede deshacer.", "Sí", "No");
        if (!confirmar)
            return;

        _trabajando = true;
        try
        {
            switch (item.Clave)
            {
                case "doc":
                    var docs = await AdminService.ListarDocumentosAsync();
                    var doc = docs.FirstOrDefault(d => d.Id == item.Id);
                    if (doc is not null)
                        await AdminService.EliminarDocumentoAsync(doc);
                    break;

                case "chat":
                    var chats = await AdminService.ListarChatsAsync();
                    var chat = chats.FirstOrDefault(c => c.Id == item.Id);
                    if (chat is not null)
                        await AdminService.EliminarChatAsync(chat);
                    break;

                case "quiz":
                    var quizzes = await AdminService.ListarCuestionariosAsync();
                    var quiz = quizzes.FirstOrDefault(c => c.Id == item.Id);
                    if (quiz is not null)
                        await AdminService.EliminarCuestionarioAsync(quiz);
                    break;

                case "diag":
                    var diagramas = await AdminService.ListarDiagramasAsync();
                    var diag = diagramas.FirstOrDefault(d => d.Id == item.Id);
                    if (diag is not null)
                        await AdminService.EliminarDiagramaAsync(diag);
                    break;

                case "res":
                    var resultados = await AdminService.ListarResultadosAsync();
                    var res = resultados.FirstOrDefault(r => r.Id == item.Id);
                    if (res is not null)
                        await AdminService.EliminarResultadoAsync(res);
                    break;
            }

            // recarga el detalle conservando al mismo usuario
            var (usuarios, _) = await AdminService.ListarUsuariosAsync();
            var u = usuarios.FirstOrDefault(x => x.Id == BtnDetClave.CommandParameter as string);
            if (u is not null)
                await CargarContenidoUsuarioAsync(u);
        }
        finally
        {
            _trabajando = false;
        }
    }

    async Task AbrirDetalleUsuarioAsync(string? id)
    {
        if (id is null || Pagina() is null)
            return;

        var (usuarios, _) = await AdminService.ListarUsuariosAsync();
        var u = usuarios.FirstOrDefault(x => x.Id == id);
        if (u is null)
            return;

        // Datos del perfil
        DetInicial.Text = string.IsNullOrEmpty(u.NombreUsuario) ? "?" : u.NombreUsuario[..1].ToUpperInvariant();
        DetTitulo.Text = !string.IsNullOrWhiteSpace(u.Nombre) ? u.Nombre : u.NombreUsuario;
        DetRol.Text = u.Rol == "admin" ? "🛡 Administrador" : "Estudiante";
        DetRol.TextColor = u.Rol == "admin" ? Color.FromArgb("#F472B6") : Color.FromArgb("#94A3B8");
        DetNombreUsuario.Text = u.NombreUsuario;
        DetEmail.Text = string.IsNullOrWhiteSpace(u.Email) ? "—" : u.Email;
        DetNombre.Text = string.IsNullOrWhiteSpace(u.Nombre) ? "—" : u.Nombre;
        DetPassword.Text = $"🔑 Contraseña (texto plano): {u.Password}";
        DetCreado.Text = FormatearFecha(u.CreadoEn);
        DetActualizado.Text = FormatearFecha(u.ActualizadoEn);

        // Los botones de gestión del detalle actúan sobre este usuario.
        BtnDetClave.CommandParameter = u.Id;
        BtnDetRol.CommandParameter = u.Id;
        BtnDetEliminar.CommandParameter = u.Id;

        await CargarContenidoUsuarioAsync(u);

        PanelDetalleUsuario.IsVisible = true;
    }

    async Task CargarContenidoUsuarioAsync(Usuario u)
    {
        await CargarUsoUsuarioAsync(u.Id);

        var documentos = (await AdminService.ListarDocumentosAsync()).Where(d => d.UsuarioId == u.Id).ToList();
        var cuestionarios = (await AdminService.ListarCuestionariosAsync()).Where(c => c.UsuarioId == u.Id).ToList();
        var diagramas = (await AdminService.ListarDiagramasAsync()).Where(d => d.UsuarioId == u.Id).ToList();
        var resultados = (await AdminService.ListarResultadosAsync()).Where(r => r.UsuarioId == u.Id).ToList();
        var chats = (await AdminService.ListarChatsAsync()).Where(c => c.UsuarioId == u.Id).ToList();
        var mensajes = (await AdminService.ListarMensajesAsync()).Where(m => chats.Any(c => c.Id == m.ChatId)).ToList();
        var temasChats = chats.Select(c => c.Tema).Distinct().ToList();

        // Estadísticas
        DetStatsLayout.Children.Clear();
        var stats = new[]
        {
            (documentos.Count, "Documentos"),
            (chats.Count, "Chats"),
            (mensajes.Count, "Mensajes"),
            (cuestionarios.Count, "Cuestionarios"),
            (diagramas.Count, "Diagramas"),
            (resultados.Count, "Resultados"),
        };
        foreach (var (valor, titulo) in stats)
        {
            DetStatsLayout.Children.Add(new Border
            {
                Style = Res<Style>("Card"),
                Padding = new Thickness(14, 10),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(14) },
                Content = new VerticalStackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        new Label { Text = valor.ToString(), FontAttributes = FontAttributes.Bold, FontSize = 20, TextColor = Colors.White },
                        new Label { Text = titulo, FontSize = 11, TextColor = Color.FromArgb("#94A3B8") },
                    },
                },
            });
        }

        // Documentos
        DetSinDocumentos.IsVisible = documentos.Count == 0;
        DetListaDocumentos.ItemsSource = documentos.Select(d => (ItemDetalle)new ItemDetalle(
            "doc", d.Id, d.Nombre,
            $"{d.Tipo.ToUpperInvariant()} · {FormatearTamano(d.TamanoBytes)} · {FormatearFecha(d.CreadoEn)}",
            d.Resumen)).ToList();

        // Chats por tema
        DetSinChats.IsVisible = chats.Count == 0;
        DetListaChats.ItemsSource = chats.Select(c => (ItemDetalle)new ItemDetalle(
            "chat", c.Id, c.Titulo,
            $"Tema: {c.Tema} · {FormatearFecha(c.CreadoEn)}",
            string.Empty)).ToList();
        if (temasChats.Count > 0)
            DetSinChats.Text = $"Temas usados: {string.Join(", ", temasChats)}.";
        else
            DetSinChats.Text = "Sin chats.";

        // Cuestionarios
        DetSinCuestionarios.IsVisible = cuestionarios.Count == 0;
        DetListaCuestionarios.ItemsSource = cuestionarios.Select(c => (ItemDetalle)new ItemDetalle(
            "quiz", c.Id, c.Titulo,
            $"{c.TotalPreguntas} preguntas · nivel {c.Nivel} · {FormatearFecha(c.CreadoEn)}",
            string.Empty)).ToList();

        // Diagramas
        DetSinDiagramas.IsVisible = diagramas.Count == 0;
        DetListaDiagramas.ItemsSource = diagramas.Select(d => (ItemDetalle)new ItemDetalle(
            "diag", d.Id, d.Titulo,
            $"tipo {d.Tipo} · {d.Fuente} · {FormatearFecha(d.CreadoEn)}",
            string.Empty)).ToList();

        // Resultados
        DetSinResultados.IsVisible = resultados.Count == 0;
        if (resultados.Count == 0)
            DetSinResultados.Text = "Sin resultados.";
        var titulosQuiz = cuestionarios.ToDictionary(c => c.Id, c => c.Titulo);
        DetListaResultados.ItemsSource = resultados.Select(r => (ItemDetalle)new ItemDetalle(
            "res", r.Id, $"{r.Puntaje} / {r.Total} correctas",
            $"{titulosQuiz.GetValueOrDefault(r.CuestionarioId ?? "", "Cuestionario")} · {r.TiempoSegundos}s · {FormatearFecha(r.Fecha)}",
            string.Empty)).ToList();
    }

    public async Task RefrescarAsync()
    {
        if (_trabajando)
            return;

        _trabajando = true;
        Spinner.IsRunning = Spinner.IsVisible = true;
        MarcarTabActivo();

        try
        {
            await CargarStatsAsync();
            await CargarTabAsync();
        }
        catch (Exception ex)
        {
            FuenteDatos.Text = "Error: " + ex.Message;
        }
        finally
        {
            _trabajando = false;
            Spinner.IsRunning = Spinner.IsVisible = false;
        }
    }

    async Task CargarStatsAsync()
    {
        var stats = await AdminService.StatsAsync();
        StatsLayout.Children.Clear();

        var tarjetas = stats.Select(kv => (kv.Key, kv.Value));
        foreach (var (clave, valor) in tarjetas)
        {
            var titulo = clave switch
            {
                "usuarios" => "Usuarios",
                "documentos" => "Documentos",
                "cuestionarios" => "Cuestionarios",
                "diagramas" => "Diagramas",
                "resultados" => "Resultados",
                _ => clave,
            };

            var card = new Border
            {
                Style = Res<Style>("Card"),
                Padding = new Thickness(14, 10),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(14) },
                Content = new VerticalStackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        new Label { Text = valor.ToString(), FontAttributes = FontAttributes.Bold, FontSize = 20, TextColor = Colors.White },
                        new Label { Text = titulo, FontSize = 11, TextColor = Color.FromArgb("#94A3B8") },
                    },
                },
            };
            StatsLayout.Children.Add(card);
        }
    }

    async Task CargarTabAsync()
    {
        switch (_tab)
        {
            case "usuarios": await CargarUsuariosAsync(); break;
            case "documentos": await CargarDocumentosAsync(); break;
            case "cuestionarios": await CargarCuestionariosAsync(); break;
            case "diagramas": await CargarDiagramasAsync(); break;
            case "resultados": await CargarResultadosAsync(); break;
        }
    }

    async Task CargarUsuariosAsync()
    {
        var (usuarios, nube) = await AdminService.ListarUsuariosAsync();
        FuenteDatos.Text = nube ? "Datos en la nube (Supabase). Las contraseñas se muestran en texto plano por requisito del proyecto."
                                : "Sin conexión a Supabase: mostrando la base local.";

        ListaUsuarios.ItemsSource = usuarios
            .Select(u => new UsuarioAdminItem(u))
            .ToList();

        await CargarConfiguracionIAAsync();
    }

    async Task CargarDocumentosAsync()
    {
        var docs = await AdminService.ListarDocumentosAsync();
        ListaDocumentos.ItemsSource = docs.Select(d => new ItemDetalle("doc", d.Id, d.Nombre,
            $"{d.Tipo.ToUpperInvariant()} · {FormatearTamano(d.TamanoBytes)} · {FormatearFecha(d.CreadoEn)}", d.Resumen)).ToList();
    }

    async Task CargarCuestionariosAsync()
    {
        var lista = await AdminService.ListarCuestionariosAsync();
        ListaCuestionarios.ItemsSource = lista.Select(c => new ItemDetalle("quiz", c.Id, c.Titulo,
            $"{c.TotalPreguntas} preguntas · nivel {c.Nivel} · {FormatearFecha(c.CreadoEn)}", string.Empty)).ToList();
    }

    async Task CargarDiagramasAsync()
    {
        var lista = await AdminService.ListarDiagramasAsync();
        ListaDiagramas.ItemsSource = lista.Select(d => new ItemDetalle("diag", d.Id, d.Titulo,
            $"tipo {d.Tipo} · {d.Fuente} · {FormatearFecha(d.CreadoEn)}", string.Empty)).ToList();
    }

    async Task CargarResultadosAsync()
    {
        var resultados = await AdminService.ListarResultadosAsync();
        var quizzes = await AdminService.ListarCuestionariosAsync();
        var titulos = quizzes.ToDictionary(q => q.Id, q => q.Titulo);

        ListaResultados.ItemsSource = resultados.Select(r => new ItemDetalle("res", r.Id,
            $"{r.Puntaje} / {r.Total} correctas",
            $"{titulos.GetValueOrDefault(r.CuestionarioId ?? "", "Cuestionario")} · {r.TiempoSegundos}s · {FormatearFecha(r.Fecha)}",
            string.Empty)).ToList();
    }

    // ---------------- CONFIGURACION DE IA ----------------

    async Task CargarConfiguracionIAAsync()
    {
        try
        {
            _cargandoConfigIA = true;
            var config = await UsoIAService.ObtenerConfiguracionAsync();
            SwitchLimiteIA.IsToggled = config.LimiteHabilitado;
            EntryLimiteIA.Text = config.LimiteDiario.ToString();
            EntryLimiteIA.IsEnabled = config.LimiteHabilitado;
            BtnGuardarConfigIA.IsEnabled = true;
            ActualizarConfigIAVisual(config.LimiteHabilitado, config.LimiteDiario);
        }
        finally
        {
            _cargandoConfigIA = false;
        }
    }

    void ActualizarConfigIAVisual(bool habilitado, int limite)
    {
        LblConfigIA.Text = habilitado
            ? $"Activo: {limite} consulta(s) por usuario al día."
            : "Desactivado: los usuarios no tienen límite de consultas IA.";
    }

    void SwitchLimiteIA_Toggled(object? sender, ToggledEventArgs e)
    {
        if (_cargandoConfigIA)
            return;

        EntryLimiteIA.IsEnabled = e.Value;
        BtnGuardarConfigIA.IsEnabled = true;

        if (int.TryParse(EntryLimiteIA.Text, out var limite))
            ActualizarConfigIAVisual(e.Value, limite);
        else
            ActualizarConfigIAVisual(e.Value, UsoIAService.LimitePorDefecto);
    }

    async void GuardarConfigIA_Click(object? sender, EventArgs e)
    {
        var page = Pagina();
        if (page is null)
            return;

        if (!int.TryParse(EntryLimiteIA.Text?.Trim(), out var limite))
        {
            await page.DisplayAlertAsync("Límite inválido", "Escribe un número entero de consultas diarias.", "OK");
            return;
        }

        var (ok, error) = await UsoIAService.GuardarConfiguracionAsync(SwitchLimiteIA.IsToggled, limite);
        if (!ok)
        {
            await page.DisplayAlertAsync("No se pudo guardar", error, "OK");
            return;
        }

        await CargarConfiguracionIAAsync();
        await page.DisplayAlertAsync("Configuración IA", "El límite diario fue actualizado.", "OK");
    }

    async Task CargarUsoUsuarioAsync(string usuarioId)
    {
        try
        {
            var estado = await UsoIAService.ObtenerEstadoAsync(usuarioId);
            DetUsoIA.Text = estado.Detalle;
            DetLimiteIA.Text = estado.Personalizado
                ? $"Límite personalizado: {estado.Limite} consulta(s) al día."
                : !estado.LimiteHabilitado
                    ? "El límite global está desactivado."
                    : $"Límite general: {estado.Limite} consulta(s) al día.";
        }
        catch (Exception ex)
        {
            DetUsoIA.Text = "No se pudo cargar el consumo de IA: " + ex.Message;
            DetLimiteIA.Text = string.Empty;
        }
    }

    async void CambiarLimiteUsuario_Click(object? sender, EventArgs e)
    {
        if (BtnDetClave.CommandParameter is not string id || Pagina() is not { } page)
            return;

        var (usuarios, _) = await AdminService.ListarUsuariosAsync();
        var usuario = usuarios.FirstOrDefault(u => u.Id == id);
        if (usuario is null)
            return;

        var estado = await UsoIAService.ObtenerEstadoAsync(id);
        var inicial = estado.Personalizado ? estado.Limite.ToString() : string.Empty;
        var texto = await page.DisplayPromptAsync(
            "Límite personalizado de IA",
            $"Límite para «{usuario.NombreUsuario}».\nEscribe un número (0 = sin consultas) o deja vacío para usar el límite general.",
            "Guardar", "Cancelar", initialValue: inicial, keyboard: Keyboard.Numeric);

        if (texto is null)
            return;

        int? limite = null;
        texto = texto.Trim();
        if (texto.Length > 0)
        {
            if (!int.TryParse(texto, out var valor))
            {
                await page.DisplayAlertAsync("Límite inválido", "Escribe un número entero o deja el campo vacío.", "OK");
                return;
            }
            limite = valor;
        }

        var (ok, error) = await UsoIAService.AsignarLimitePersonalizadoAsync(id, limite);
        if (!ok)
        {
            await page.DisplayAlertAsync("No se pudo guardar", error, "OK");
            return;
        }

        await CargarUsoUsuarioAsync(id);
    }

    // ---------------- ACCIONES: USUARIOS ----------------

    async void CrearUsuario_Click(object? sender, EventArgs e)
    {
        var page = Pagina();
        if (page is null) return;

        var usuario = await page.DisplayPromptAsync("Crear usuario", "Nombre de usuario:", "Siguiente", "Cancelar");
        if (string.IsNullOrWhiteSpace(usuario)) return;

        var clave = await page.DisplayPromptAsync("Crear usuario", $"Contraseña para «{usuario.Trim()}»:", "Siguiente", "Cancelar");
        if (string.IsNullOrWhiteSpace(clave)) return;

        var nombre = await page.DisplayPromptAsync("Crear usuario", "Nombre completo (opcional):", "Siguiente", "Cancelar");
        if (nombre is null) return;

        var email = await page.DisplayPromptAsync("Crear usuario", "Correo electrónico (opcional):", "Siguiente", "Cancelar");
        if (email is null) return;

        var rol = await page.DisplayActionSheetAsync("Rol del usuario", "Cancelar", null, "estudiante", "admin");
        if (rol is null || rol == "Cancelar") return;

        var (ok, error) = await AdminService.CrearUsuarioAsync(usuario, clave, email, nombre, rol);
        if (!ok)
        {
            await page.DisplayAlertAsync("No se pudo crear", error, "OK");
            return;
        }

        await page.DisplayAlertAsync("Usuario creado", $"«{usuario.Trim()}» ({rol}) fue creado.", "OK");
        await RefrescarAsync();
    }

    async void Clave_Click(object? sender, EventArgs e)
    {
        var u = await BuscarUsuarioAsync(sender);
        var page = Pagina();
        if (u is null || page is null) return;

        var nueva = await page.DisplayPromptAsync("Cambiar contraseña",
            $"Nueva contraseña para «{u.NombreUsuario}». (Se guarda en texto plano por requisito del proyecto):",
            "Guardar", "Cancelar");
        if (string.IsNullOrWhiteSpace(nueva))
            return;

        if (nueva.Length < 4)
        {
            await page.DisplayAlertAsync("Aviso", "Debe tener al menos 4 caracteres.", "OK");
            return;
        }

        await AdminService.CambiarPasswordAsync(u, nueva);
        await page.DisplayAlertAsync("Listo", "Contraseña actualizada.", "OK");
        await RefrescarAsync();
    }

    async void Rol_Click(object? sender, EventArgs e)
    {
        var u = await BuscarUsuarioAsync(sender);
        var page = Pagina();
        if (u is null || page is null) return;

        var rol = await page.DisplayActionSheetAsync($"Rol de «{u.NombreUsuario}»", "Cancelar", null, "estudiante", "admin");
        if (rol is null || rol == "Cancelar" || rol == u.Rol)
            return;

        await AdminService.CambiarRolAsync(u, rol);
        await RefrescarAsync();
    }

    async void EliminarUsuario_Click(object? sender, EventArgs e)
    {
        var u = await BuscarUsuarioAsync(sender);
        var page = Pagina();
        if (u is null || page is null) return;

        var confirmar = await page.DisplayAlertAsync("Eliminar usuario",
            $"¿Borrar a «{u.NombreUsuario}»? También se borran sus documentos, chats, cuestionarios y diagramas.",
            "Sí", "No");
        if (!confirmar) return;

        await AdminService.EliminarUsuarioAsync(u);
        await RefrescarAsync();
    }

    async Task<Usuario?> BuscarUsuarioAsync(object? sender)
    {
        if ((sender as Button)?.CommandParameter is not string id)
            return null;

        var (usuarios, _) = await AdminService.ListarUsuariosAsync();
        return usuarios.FirstOrDefault(u => u.Id == id);
    }

    // ---------------- ACCIONES: DOCUMENTOS / RESTO ----------------

    async void VerDocumento_Click(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not string id) return;
        var page = Pagina();
        if (page is null) return;

        var docs = await AdminService.ListarDocumentosAsync();
        var doc = docs.FirstOrDefault(d => d.Id == id);
        if (doc is null) return;

        var texto = string.IsNullOrWhiteSpace(doc.Resumen) && !string.IsNullOrWhiteSpace(doc.Texto)
            ? doc.Texto
            : doc.Resumen;
        var vista = Recortar(texto, 800);

        await page.DisplayAlertAsync($"Texto de «{doc.Nombre}»",
            string.IsNullOrWhiteSpace(vista) ? "(sin texto extraíble)" : vista, "Cerrar");
    }

    async void VerDiagrama_Click(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not string id) return;
        var page = Pagina();
        if (page is null) return;

        var lista = await AdminService.ListarDiagramasAsync();
        var d = lista.FirstOrDefault(x => x.Id == id);
        if (d is null) return;

        await Clipboard.SetTextAsync(d.Codigo);
        await page.DisplayAlertAsync($"Diagrama «{d.Titulo}» (tipo {d.Tipo})",
            "Código copiado al portapapeles:\n\n" + Recortar(d.Codigo, 800), "Cerrar");
    }

    async void EliminarDocumento_Click(object? sender, EventArgs e) =>
        await ConfirmarYEliminarAsync(sender, "documento",
            async () => await AdminService.ListarDocumentosAsync(),
            d => d.Nombre, d => d.Id, d => AdminService.EliminarDocumentoAsync(d));

    async void EliminarCuestionario_Click(object? sender, EventArgs e) =>
        await ConfirmarYEliminarAsync(sender, "cuestionario",
            async () => await AdminService.ListarCuestionariosAsync(),
            c => c.Titulo, c => c.Id, c => AdminService.EliminarCuestionarioAsync(c));

    async void EliminarDiagrama_Click(object? sender, EventArgs e) =>
        await ConfirmarYEliminarAsync(sender, "diagrama",
            async () => await AdminService.ListarDiagramasAsync(),
            d => d.Titulo, d => d.Id, d => AdminService.EliminarDiagramaAsync(d));

    async void EliminarResultado_Click(object? sender, EventArgs e) =>
        await ConfirmarYEliminarAsync(sender, "resultado",
            async () => await AdminService.ListarResultadosAsync(),
            r => $"{r.Puntaje}/{r.Total}", r => r.Id, r => AdminService.EliminarResultadoAsync(r));

    async Task ConfirmarYEliminarAsync<T>(object? sender, string etiqueta,
        Func<Task<List<T>>> listar, Func<T, string> nombre, Func<T, string> obtenerId, Func<T, Task> eliminar)
    {
        if ((sender as Button)?.CommandParameter is not string id) return;
        var page = Pagina();
        if (page is null) return;

        var lista = await listar();
        var item = lista.FirstOrDefault(x => obtenerId(x) == id);
        if (item is null) return;

        var confirmar = await page.DisplayAlertAsync($"Eliminar {etiqueta}",
            $"¿Borrar «{nombre(item)}»? Esta acción no se puede deshacer.", "Sí", "No");
        if (!confirmar) return;

        _trabajando = true;
        try
        {
            await eliminar(item);
            await RefrescarAsync();
        }
        finally
        {
            _trabajando = false;
        }
    }

    // ---------------- UTILIDADES ----------------

    Page? Pagina() => Application.Current?.Windows.FirstOrDefault()?.Page;

    static T? Res<T>(string clave) where T : class =>
        Application.Current?.Resources.TryGetValue(clave, out var valor) == true ? valor as T : null;

    static string Recortar(string texto, int max) =>
        string.IsNullOrEmpty(texto) ? string.Empty :
        texto.Length <= max ? texto : texto[..max] + "\n\n… (texto truncado)";

    static string FormatearTamano(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):0.#} MB",
        >= 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} B"
    };

    static string FormatearFecha(string iso)
    {
        try
        {
            var f = DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            return f.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        }
        catch
        {
            return iso;
        }
    }
}

// ---------------- ITEMS DE UI ----------------

public class UsuarioAdminItem : Usuario
{
    public string Inicial => string.IsNullOrEmpty(NombreUsuario) ? "?" : NombreUsuario[..1].ToUpperInvariant();
    public string RolEstiqueta => Rol == "admin" ? "🛡 ADMIN" : "Estudiante";
    public Color ColorRol => Rol == "admin" ? Color.FromArgb("#F472B6") : Color.FromArgb("#94A3B8");
    public string Contacto => string.IsNullOrWhiteSpace(Email) ? Nombre : $"{Nombre} · {Email}";
    public string TextoBotonRol => Rol == "admin" ? "Quitar admin" : "Hacer admin";
    public bool MostrarPassword => !string.IsNullOrEmpty(Password);
    public bool PuedeEliminar => NombreUsuario != "admin";

    public UsuarioAdminItem(Usuario u) : base()
    {
        Id = u.Id;
        NombreUsuario = u.NombreUsuario;
        Email = u.Email;
        Password = u.Password;
        Nombre = u.Nombre;
        Rol = u.Rol;
        CreadoEn = u.CreadoEn;
        ActualizadoEn = u.ActualizadoEn;
    }
}

public class ItemDetalle
{
    public string Clave { get; }
    public string Id { get; }
    public string Titulo { get; }
    public string Detalle { get; }
    public string Resumen { get; }
    public bool TieneResumen => !string.IsNullOrWhiteSpace(Resumen);

    public ItemDetalle(string clave, string id, string titulo, string detalle, string resumen)
    {
        Clave = clave;
        Id = id;
        Titulo = titulo;
        Detalle = detalle;
        Resumen = resumen;
    }
}
