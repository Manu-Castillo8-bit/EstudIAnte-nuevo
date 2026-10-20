using EstudIAnte.Models;
using Supabase;

namespace EstudIAnte.Services;

/// <summary>
/// Cliente de Supabase (PostgreSQL + Storage en la nube).
/// Devuelve null si no esta configurado o no hay conexion.
/// </summary>
public static class SupabaseService
{
    static Client? _client;
    static readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Prueba la conexion con Supabase y verifica que las tablas existan.
    /// Sirve para saber desde la app si la nube responde.
    /// </summary>
    public static async Task<(bool Ok, string Mensaje)> TestConnectionAsync()
    {
        if (!SupabaseConfig.IsConfigured)
            return (false, "Supabase no configurado (revisa SupabaseConfig.cs).");

        var client = await GetClientAsync();
        if (client is null)
            return (false, "No se pudo conectar. Revisa la URL, la clave y tu internet.");

        try
        {
            var resp = await client.From<Usuario>().Get();
            var total = resp?.Models?.Count ?? 0;
            return (true, $"Supabase conectado. Tabla 'usuarios': {total} registro(s).");
        }
        catch (Exception ex)
        {
            return (false, "Conectado, pero fallo la consulta. ¿Ejecutaste schema.sql? — " + ex.Message);
        }
    }

    public static async Task<Client?> GetClientAsync()
    {
        if (!SupabaseConfig.IsConfigured)
            return null;

        if (_client is not null)
            return _client;

        await _lock.WaitAsync();
        try
        {
            if (_client is not null)
                return _client;

            var client = new Client(SupabaseConfig.Url, SupabaseConfig.AnonKey);
            await client.InitializeAsync();
            _client = client;
            return _client;
        }
        catch
        {
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }
}
