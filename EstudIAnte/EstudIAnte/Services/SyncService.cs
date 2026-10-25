using EstudIAnte.Models;
using Postgrest.Models;

namespace EstudIAnte.Services;

/// <summary>
/// Sincronizacion entre Supabase (nube) y SQLite (local).
/// PullAsync: baja todo desde la nube y lo guarda en SQLite.
/// </summary>
public static class SyncService
{
    /// <summary>Descarga todas las tablas de Supabase a SQLite. Devuelve false si no hay conexion.</summary>
    public static async Task<bool> PullAsync()
    {
        var client = await SupabaseService.GetClientAsync();
        if (client is null)
            return false;

        try
        {
            var conn = await LocalDatabase.GetAsync();

            await PullTable<Documento>(client, conn);
            await PullTable<Chat>(client, conn);
            await PullTable<Mensaje>(client, conn);
            await PullTable<Cuestionario>(client, conn);
            await PullTable<Pregunta>(client, conn);
            await PullTable<Diagrama>(client, conn);
            await PullTable<Resultado>(client, conn);

            return true;
        }
        catch
        {
            return false;
        }
    }

    static async Task PullTable<T>(Supabase.Client client, SQLite.SQLiteAsyncConnection conn)
        where T : BaseModel, new()
    {
        var resp = await client.From<T>().Get();
        if (resp?.Models is null)
            return;

        foreach (var modelo in resp.Models)
            await conn.InsertOrReplaceAsync(modelo);
    }
}
