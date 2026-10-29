using EstudIAnte.Models;

namespace EstudIAnte.Services;

/// <summary>
/// Registra y limita el uso de la IA por usuario/cuenta, para que el consumo
/// de un usuario no agote la cuota global del proveedor.
/// </summary>
public static class UsoIAService
{
    /// <summary>Llamadas de IA permitidas por usuario y día.</summary>
    public static int LimiteDiario => Microsoft.Maui.Storage.Preferences.Get("ia_limite_3", false) ? 3 : 50;

    static string FechaHoy() => DateTime.UtcNow.ToString("yyyy-MM-dd");

    public static async Task<(bool Permitido, int Usadas, int Restantes)> PuedeUsarAsync(string? usuarioId)
    {
        if (string.IsNullOrWhiteSpace(usuarioId))
            return (true, 0, LimiteDiario);

        int usadas = 0;
        try
        {
            var client = await SupabaseService.GetClientAsync();
            if (client is not null)
            {
                try
                {
                    var fechaHoy = FechaHoy();
                    var respuesta = await client.From<UsoIA>()
                        .Where(u => u.UsuarioId == usuarioId && u.Fecha == fechaHoy)
                        .Get();
                    var usoNube = respuesta.Models.FirstOrDefault();
                    if (usoNube is not null)
                        usadas = Math.Max(usadas, usoNube.Llamadas);
                }
                catch { /* respaldo local */ }
            }
        }
        catch { /* respaldo local */ }

        try
        {
            var conn = await LocalDatabase.GetAsync();
            var fechaHoy = FechaHoy();
            var uso = await conn.Table<UsoIA>()
                .Where(u => u.UsuarioId == usuarioId && u.Fecha == fechaHoy)
                .FirstOrDefaultAsync();

            usadas = Math.Max(usadas, uso?.Llamadas ?? 0);
        }
        catch { }

        try
        {
            usadas = Math.Max(usadas, Microsoft.Maui.Storage.Preferences.Get($"usoia_day_{usuarioId}_{FechaHoy()}", 0));
        }
        catch { }

        var restantes = Math.Max(0, LimiteDiario - usadas);
        return (usadas < LimiteDiario, usadas, restantes);
    }

    public static async Task RegistrarUsoAsync(string? usuarioId, string? proveedor = null,
        int tokensAprox = 0)
    {
        if (string.IsNullOrWhiteSpace(usuarioId))
            return;

        Diag($"RegistrarUso inicio usuario={usuarioId} proveedor={proveedor} tokens={tokensAprox}");

        UsoIA usoLocal;
        try
        {
            var conn = await LocalDatabase.GetAsync();
            var fechaHoy = FechaHoy();
            var uso = await conn.Table<UsoIA>()
                .Where(u => u.UsuarioId == usuarioId && u.Fecha == fechaHoy)
                .FirstOrDefaultAsync();

            if (uso is null)
            {
                usoLocal = new UsoIA
                {
                    UsuarioId = usuarioId,
                    Fecha = FechaHoy(),
                    Llamadas = 1,
                    TokensAprox = Math.Max(0, tokensAprox),
                    ActualizadoEn = DateTime.UtcNow.ToString("o")
                };
                await conn.InsertAsync(usoLocal);
            }
            else
            {
                uso.Llamadas += 1;
                uso.TokensAprox += Math.Max(0, tokensAprox);
                uso.ActualizadoEn = DateTime.UtcNow.ToString("o");
                await conn.UpdateAsync(uso);
                usoLocal = uso;
            }
        }
        catch (Exception ex)
        {
            // No se bloquea la generación si falla el registro local.
            Diag("local error capturado: " + ex);
            return;
        }

        Diag($"local OK usuario={usuarioId} uso={usoLocal.Llamadas} fecha={usoLocal.Fecha}");
        try
        {
            var dayKey = $"usoia_day_{usuarioId}_{FechaHoy()}";
            Microsoft.Maui.Storage.Preferences.Set(dayKey,
                Microsoft.Maui.Storage.Preferences.Get(dayKey, 0) + 1);
            var totalKey = $"usoia_total_{usuarioId}";
            Microsoft.Maui.Storage.Preferences.Set(totalKey,
                Microsoft.Maui.Storage.Preferences.Get(totalKey, 0) + 1);
        }
        catch { }

        // Intenta sincronizar con Supabase (si la tabla existe).
        try
        {
            var client = await SupabaseService.GetClientAsync();
            if (client is null)
                return;

            var fechaHoy = FechaHoy();
            var respuesta = await client.From<UsoIA>()
                .Where(u => u.UsuarioId == usuarioId && u.Fecha == fechaHoy)
                .Get();
            var nube = respuesta.Models.FirstOrDefault();

            if (nube is null)
            {
                await client.From<UsoIA>().Insert(usoLocal);
            }
            else
            {
                nube.Llamadas = usoLocal.Llamadas > nube.Llamadas ? usoLocal.Llamadas : nube.Llamadas + 0;
                nube.TokensAprox += Math.Max(0, tokensAprox);
                nube.ActualizadoEn = DateTime.UtcNow.ToString("o");
                await client.From<UsoIA>().Update(nube);
            }
        }
        catch (Exception ex)
        {
            // Sin conexión o tabla inexistente: queda local.
            Diag("cloud error: " + ex.Message);
        }
    }

    static void Diag(string msg)
    {
        try
        {
            var carpeta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EstudIAnte");
            Directory.CreateDirectory(carpeta);
            File.AppendAllText(Path.Combine(carpeta, "usoia.log"),
                $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        }
        catch { }
    }

    public static async Task<int> TotalLlamadasAsync(string? usuarioId)
    {
        if (string.IsNullOrWhiteSpace(usuarioId))
            return 0;

        try
        {
            var conn = await LocalDatabase.GetAsync();
            var lista = await conn.Table<UsoIA>()
                .Where(u => u.UsuarioId == usuarioId)
                .ToListAsync();
            var totalSql = lista.Sum(u => u.Llamadas);
            var totalPref = Microsoft.Maui.Storage.Preferences.Get($"usoia_total_{usuarioId}", 0);
            return Math.Max(totalSql, totalPref);
        }
        catch
        {
            return Microsoft.Maui.Storage.Preferences.Get($"usoia_total_{usuarioId}", 0);
        }
    }
}