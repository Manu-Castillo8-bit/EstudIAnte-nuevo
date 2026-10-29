using EstudIAnte.Models;

namespace EstudIAnte.Services;

public enum AccionIA
{
    Chat,
    Documento,
    Cuestionario,
    Diagrama
}

public sealed class EstadoUsoIA
{
    public bool LimiteHabilitado { get; init; }
    public int Limite { get; init; }
    public int Usadas { get; init; }
    public bool Exento { get; init; }
    public bool Personalizado { get; init; }
    public string Fecha { get; init; } = string.Empty;

    public int Restantes => !LimiteHabilitado || Exento ? int.MaxValue : Math.Max(0, Limite - Usadas);

    public bool Agotado => LimiteHabilitado && !Exento && Restantes <= 0;

    public string Texto => !LimiteHabilitado
        ? "IA: sin límite"
        : Exento
            ? "IA: exento"
            : $"IA: {Restantes}/{Limite}";

    public string Detalle => !LimiteHabilitado
        ? "El límite global de consultas IA está desactivado."
        : Exento
            ? "Los administradores están exentos de consumir consultas IA."
            : $"{Usadas} de {Limite} consulta(s) usada(s) hoy ({Fecha}).";
}

public sealed class ConsumoIA
{
    internal ConsumoIA(bool permitido, string mensaje, EstadoUsoIA estado,
        string usuarioId, string fecha, bool exento)
    {
        Permitido = permitido;
        Mensaje = mensaje;
        Estado = estado;
        UsuarioId = usuarioId;
        Fecha = fecha;
        Exento = exento;
    }

    public bool Permitido { get; }
    public string Mensaje { get; }
    public EstadoUsoIA Estado { get; }
    internal string UsuarioId { get; }
    internal string Fecha { get; }
    internal bool Exento { get; }

    int _devuelto;

    public async Task DevolverAsync()
    {
        if (!Permitido || Exento)
            return;

        if (Interlocked.Exchange(ref _devuelto, 1) != 0)
            return;

        await UsoIAService.DevolverAsync(UsuarioId, Fecha);
    }
}

public static class UsoIAService
{
    public const int LimitePorDefecto = 3;
    public const int MaximoConsultasDiarias = 10000;
    public const int MaximoCaracteresChat = 600;

    public static bool AdminsExentos { get; set; } = true;

    public static event Action<EstadoUsoIA>? EstadoCambiado;

    static readonly SemaphoreSlim _lock = new(1, 1);

    static string Hoy => DateTime.Now.ToString("yyyy-MM-dd");

    public static async Task<EstadoUsoIA> ObtenerEstadoAsync(string usuarioId)
    {
        EstadoUsoIA? estado = null;
        await _lock.WaitAsync();
        try
        {
            var conn = await LocalDatabase.GetAsync();
            var config = await ObtenerConfiguracionLocalAsync(conn);
            var uso = await ObtenerUsoLocalAsync(conn, usuarioId);
            var usuario = await conn.Table<Usuario>().Where(u => u.Id == usuarioId).FirstOrDefaultAsync();

            if (await ReiniciarSiCambioDeDiaAsync(conn, uso))
                uso = await conn.Table<UsoIA>().Where(u => u.UsuarioId == usuarioId).FirstOrDefaultAsync() ?? uso;

            estado = CrearEstado(config, uso, usuario);
        }
        finally
        {
            _lock.Release();
        }

        if (estado is not null && usuarioId == AuthService.UsuarioActual?.Id)
            EstadoCambiado?.Invoke(estado);

        return estado ?? new EstadoUsoIA();
    }

    public static async Task<ConsumoIA> ConsumirAsync(string usuarioId, AccionIA accion)
    {
        EstadoUsoIA? estado = null;
        ConsumoIA? resultado = null;

        await _lock.WaitAsync();
        try
        {
            var conn = await LocalDatabase.GetAsync();
            var config = await ObtenerConfiguracionLocalAsync(conn);
            var uso = await ObtenerUsoLocalAsync(conn, usuarioId);
            var usuario = await conn.Table<Usuario>().Where(u => u.Id == usuarioId).FirstOrDefaultAsync();

            await ReiniciarSiCambioDeDiaAsync(conn, uso);
            uso = await conn.Table<UsoIA>().Where(u => u.UsuarioId == usuarioId).FirstOrDefaultAsync() ?? uso;

            estado = CrearEstado(config, uso, usuario);

            if (estado.Exento)
            {
                resultado = new ConsumoIA(true, string.Empty, estado, usuarioId, uso.Fecha, true);
                return resultado;
            }

            if (config.LimiteHabilitado && uso.ConsultasUsadas >= estado.Limite)
            {
                resultado = new ConsumoIA(false, MensajeAgotado(estado.Limite), estado, usuarioId, uso.Fecha, false);
                return resultado;
            }

            uso.ConsultasUsadas++;
            uso.ActualizadoEn = DateTime.UtcNow.ToString("o");
            await conn.InsertOrReplaceAsync(uso);
            _ = SubirUsoAsync(uso);

            estado = CrearEstado(config, uso, usuario);
            resultado = new ConsumoIA(true, string.Empty, estado, usuarioId, uso.Fecha, false);
            return resultado;
        }
        finally
        {
            _lock.Release();
            if (usuarioId == AuthService.UsuarioActual?.Id)
            {
                if (resultado is not null)
                    EstadoCambiado?.Invoke(resultado.Estado);
                else if (estado is not null)
                    EstadoCambiado?.Invoke(estado);
            }
        }
    }

    internal static async Task DevolverAsync(string usuarioId, string fecha)
    {
        EstadoUsoIA? estado = null;
        await _lock.WaitAsync();
        try
        {
            var conn = await LocalDatabase.GetAsync();
            var config = await ObtenerConfiguracionLocalAsync(conn);
            var uso = await ObtenerUsoLocalAsync(conn, usuarioId);
            var usuario = await conn.Table<Usuario>().Where(u => u.Id == usuarioId).FirstOrDefaultAsync();

            if (await ReiniciarSiCambioDeDiaAsync(conn, uso))
                uso = await conn.Table<UsoIA>().Where(u => u.UsuarioId == usuarioId).FirstOrDefaultAsync() ?? uso;

            if (uso.Fecha == fecha && uso.ConsultasUsadas > 0)
            {
                uso.ConsultasUsadas--;
                uso.ActualizadoEn = DateTime.UtcNow.ToString("o");
                await conn.InsertOrReplaceAsync(uso);
                _ = SubirUsoAsync(uso);
            }

            estado = CrearEstado(config, uso, usuario);
        }
        finally
        {
            _lock.Release();
        }

        if (estado is not null && usuarioId == AuthService.UsuarioActual?.Id)
            EstadoCambiado?.Invoke(estado);
    }

    public static async Task<ConfiguracionIA> ObtenerConfiguracionAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var conn = await LocalDatabase.GetAsync();
            return await ObtenerConfiguracionLocalAsync(conn);
        }
        finally
        {
            _lock.Release();
        }
    }

    public static async Task<(bool Ok, string Error)> GuardarConfiguracionAsync(bool habilitado, int limiteDiario)
    {
        if (limiteDiario < 1 || limiteDiario > MaximoConsultasDiarias)
            return (false, $"El límite debe estar entre 1 y {MaximoConsultasDiarias} consultas diarias.");

        EstadoUsoIA? estado = null;
        await _lock.WaitAsync();
        try
        {
            var conn = await LocalDatabase.GetAsync();
            var config = await ObtenerConfiguracionLocalAsync(conn);
            config.LimiteHabilitado = habilitado;
            config.LimiteDiario = limiteDiario;
            config.ActualizadoEn = DateTime.UtcNow.ToString("o");
            await conn.InsertOrReplaceAsync(config);
            _ = SubirConfiguracionAsync(config);

            var actual = AuthService.UsuarioActual;
            if (actual is not null)
            {
                var uso = await ObtenerUsoLocalAsync(conn, actual.Id);
                if (await ReiniciarSiCambioDeDiaAsync(conn, uso))
                    uso = await conn.Table<UsoIA>().Where(u => u.UsuarioId == actual.Id).FirstOrDefaultAsync() ?? uso;
                var usuario = await conn.Table<Usuario>().Where(u => u.Id == actual.Id).FirstOrDefaultAsync();
                estado = CrearEstado(config, uso, usuario);
            }
        }
        finally
        {
            _lock.Release();
        }

        if (estado is not null)
            EstadoCambiado?.Invoke(estado);

        return (true, string.Empty);
    }

    public static async Task<(bool Ok, string Error)> AsignarLimitePersonalizadoAsync(string usuarioId, int? limite)
    {
        if (limite is < 0 or > MaximoConsultasDiarias)
            return (false, $"El límite debe estar entre 0 y {MaximoConsultasDiarias}, o vacío para usar el general.");

        EstadoUsoIA? estado = null;
        await _lock.WaitAsync();
        try
        {
            var conn = await LocalDatabase.GetAsync();
            var uso = await ObtenerUsoLocalAsync(conn, usuarioId);
            if (uso.Fecha != Hoy)
            {
                uso.Fecha = Hoy;
                uso.ConsultasUsadas = 0;
            }
            uso.LimitePersonalizado = limite;
            uso.ActualizadoEn = DateTime.UtcNow.ToString("o");
            await conn.InsertOrReplaceAsync(uso);
            _ = SubirUsoAsync(uso);

            var actual = AuthService.UsuarioActual;
            if (actual?.Id == usuarioId)
            {
                var config = await ObtenerConfiguracionLocalAsync(conn);
                var usuario = await conn.Table<Usuario>().Where(u => u.Id == usuarioId).FirstOrDefaultAsync();
                estado = CrearEstado(config, uso, usuario);
            }
        }
        finally
        {
            _lock.Release();
        }

        if (estado is not null)
            EstadoCambiado?.Invoke(estado);

        return (true, string.Empty);
    }

    public static async Task RefrescarDesdeNubeAsync(string? usuarioId = null)
    {
        var client = await SupabaseService.GetClientAsync();
        if (client is null)
            return;

        try
        {
            var conn = await LocalDatabase.GetAsync();

            var respuestaConfig = await client.From<ConfiguracionIA>()
                .Where(c => c.Id == ConfiguracionIA.IdGlobal)
                .Get();
            var configRemota = respuestaConfig?.Models?.FirstOrDefault();
            if (configRemota is not null)
                await conn.InsertOrReplaceAsync(configRemota);

            if (string.IsNullOrWhiteSpace(usuarioId))
                return;

            var respuestaUso = await client.From<UsoIA>()
                .Where(u => u.UsuarioId == usuarioId)
                .Get();
            var usoRemoto = respuestaUso?.Models?.FirstOrDefault();
            if (usoRemoto is null)
                return;

            var usoLocal = await conn.Table<UsoIA>()
                .Where(u => u.UsuarioId == usuarioId)
                .FirstOrDefaultAsync();

            if (usoLocal is not null)
            {
                if (string.CompareOrdinal(usoRemoto.Fecha, usoLocal.Fecha) < 0)
                    return;

                if (usoRemoto.Fecha == usoLocal.Fecha && usoRemoto.ConsultasUsadas < usoLocal.ConsultasUsadas)
                {
                    usoRemoto.ConsultasUsadas = usoLocal.ConsultasUsadas;
                    usoRemoto.LimitePersonalizado ??= usoLocal.LimitePersonalizado;
                }
            }

            await conn.InsertOrReplaceAsync(usoRemoto);
        }
        catch
        {
        }
    }

    static async Task<ConfiguracionIA> ObtenerConfiguracionLocalAsync(SQLite.SQLiteAsyncConnection conn)
    {
        var config = await conn.Table<ConfiguracionIA>()
            .Where(c => c.Id == ConfiguracionIA.IdGlobal)
            .FirstOrDefaultAsync();

        if (config is not null)
            return config;

        config = new ConfiguracionIA { ActualizadoEn = DateTime.MinValue.ToString("o") };
        await conn.InsertAsync(config);
        return config;
    }

    static async Task<UsoIA> ObtenerUsoLocalAsync(SQLite.SQLiteAsyncConnection conn, string usuarioId)
    {
        var uso = await conn.Table<UsoIA>()
            .Where(u => u.UsuarioId == usuarioId)
            .FirstOrDefaultAsync();

        if (uso is not null)
            return uso;

        uso = new UsoIA { UsuarioId = usuarioId, Fecha = Hoy };
        await conn.InsertAsync(uso);
        return uso;
    }

    static async Task<bool> ReiniciarSiCambioDeDiaAsync(SQLite.SQLiteAsyncConnection conn, UsoIA uso)
    {
        if (uso.Fecha == Hoy)
            return false;

        uso.Fecha = Hoy;
        uso.ConsultasUsadas = 0;
        uso.ActualizadoEn = DateTime.UtcNow.ToString("o");
        await conn.InsertOrReplaceAsync(uso);
        _ = SubirUsoAsync(uso);
        return true;
    }

    static EstadoUsoIA CrearEstado(ConfiguracionIA config, UsoIA uso, Usuario? usuario)
    {
        var exento = AdminsExentos && usuario?.Rol == "admin";
        var limite = uso.LimitePersonalizado ?? config.LimiteDiario;

        return new EstadoUsoIA
        {
            LimiteHabilitado = config.LimiteHabilitado,
            Limite = limite,
            Usadas = uso.ConsultasUsadas,
            Exento = exento,
            Personalizado = uso.LimitePersonalizado.HasValue,
            Fecha = uso.Fecha
        };
    }

    static string MensajeAgotado(int limite) =>
        limite <= 0
            ? "Un administrador ha bloqueado tus consultas de IA.\n\n" +
              "Puedes seguir usando la app sin IA o pedirle a un administrador que reactive tu acceso."
            : $"Has agotado tus {limite} consulta(s) de IA de hoy. Tu cupo se renueva mañana.\n\n" +
              "Puedes seguir usando la app sin IA o pedirle a un administrador que amplíe tu límite.";

    static async Task SubirUsoAsync(UsoIA uso)
    {
        var client = await SupabaseService.GetClientAsync();
        if (client is null)
            return;

        try
        {
            var respuesta = await client.From<UsoIA>()
                .Where(u => u.UsuarioId == uso.UsuarioId)
                .Get();

            var remoto = respuesta?.Models?.FirstOrDefault();
            if (remoto is null)
            {
                await client.From<UsoIA>().Insert(uso);
                return;
            }

            if (string.CompareOrdinal(remoto.Fecha, uso.Fecha) > 0)
                return;

            var cambioLocal = false;
            if (remoto.Fecha == uso.Fecha && remoto.ConsultasUsadas > uso.ConsultasUsadas)
            {
                uso.ConsultasUsadas = remoto.ConsultasUsadas;
                cambioLocal = true;
            }

            if (RemotoEsMasNuevo(remoto.ActualizadoEn, uso.ActualizadoEn))
            {
                uso.LimitePersonalizado = remoto.LimitePersonalizado;
                cambioLocal = true;
            }

            if (cambioLocal)
            {
                uso.ActualizadoEn = DateTime.UtcNow.ToString("o");
                var conn = await LocalDatabase.GetAsync();
                await conn.InsertOrReplaceAsync(uso);
            }

            await client.From<UsoIA>().Update(uso);
        }
        catch
        {
        }
    }

    static async Task SubirConfiguracionAsync(ConfiguracionIA config)
    {
        var client = await SupabaseService.GetClientAsync();
        if (client is null)
            return;

        try
        {
            var respuesta = await client.From<ConfiguracionIA>()
                .Where(c => c.Id == ConfiguracionIA.IdGlobal)
                .Get();

            var remoto = respuesta?.Models?.FirstOrDefault();
            if (remoto is null)
                await client.From<ConfiguracionIA>().Insert(config);
            else if (!RemotoEsMasNuevo(remoto.ActualizadoEn, config.ActualizadoEn))
                await client.From<ConfiguracionIA>().Update(config);
        }
        catch
        {
        }
    }

    static bool RemotoEsMasNuevo(string? remoto, string? local)
    {
        return DateTimeOffset.TryParse(remoto, System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.RoundtripKind, out var fechaRemota)
               && DateTimeOffset.TryParse(local, System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.RoundtripKind, out var fechaLocal)
               && fechaRemota > fechaLocal;
    }
}
