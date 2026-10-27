using EstudIAnte.Models;
using Postgrest.Models;

namespace EstudIAnte.Services;

/// <summary>
/// Sincronizacion entre Supabase (nube) y SQLite (local).
/// PushAsync: sube a la nube lo creado/modificado sin conexión y aplica borrados pendientes.
/// PullAsync: baja todo desde la nube y lo guarda en SQLite.
/// </summary>
public static class SyncService
{
    /// <summary>
    /// Registra un borrado para aplicarlo en la nube en la próxima sincronización.
    /// Llamar SIEMPRE antes de borrar localmente (aunque haya conexión, es inofensivo).
    /// </summary>
    public static async Task MarcarBorradoAsync(string tabla, string registroId)
    {
        var conn = await LocalDatabase.GetAsync();
        await conn.InsertAsync(new SyncBorrado { Tabla = tabla, RegistroId = registroId });
    }

    /// <summary>
    /// Sube a Supabase los datos locales: borrados pendientes, registros nuevos
    /// y cambios en documentos (estado/resumen). Devuelve (ok, mensaje).
    /// </summary>
    public static async Task<(bool Ok, string Mensaje)> PushAsync()
    {
        var client = await SupabaseService.GetClientAsync();
        if (client is null)
            return (false, "Sin conexión con la nube.");

        var conn = await LocalDatabase.GetAsync();
        var subidos = 0;
        var borrados = 0;

        try
        {
            // 1) Borrados pendientes: se aplican primero para que la descarga
            //    no restaure elementos que el usuario eliminó.
            var pendientes = await conn.Table<SyncBorrado>().ToListAsync();
            foreach (var b in pendientes)
            {
                try
                {
                    switch (b.Tabla)
                    {
                        case "documentos": await client.From<Documento>().Where(x => x.Id == b.RegistroId).Delete(); break;
                        case "temas": await client.From<Tema>().Where(x => x.Id == b.RegistroId).Delete(); break;
                        case "chats": await client.From<Chat>().Where(x => x.Id == b.RegistroId).Delete(); break;
                        case "mensajes": await client.From<Mensaje>().Where(x => x.Id == b.RegistroId).Delete(); break;
                        case "cuestionarios": await client.From<Cuestionario>().Where(x => x.Id == b.RegistroId).Delete(); break;
                        case "preguntas": await client.From<Pregunta>().Where(x => x.Id == b.RegistroId).Delete(); break;
                        case "diagramas": await client.From<Diagrama>().Where(x => x.Id == b.RegistroId).Delete(); break;
                        case "resultados": await client.From<Resultado>().Where(x => x.Id == b.RegistroId).Delete(); break;
                        case "usuarios": await client.From<Usuario>().Where(x => x.Id == b.RegistroId).Delete(); break;
                    }

                    await conn.DeleteAsync(b);
                    borrados++;
                }
                catch
                {
                    // Sin conexión o error: se reintenta en la próxima sincronización.
                }
            }

            // 2) Usuarios: sube los que falten en la nube (p. ej. cuentas creadas offline).
            var idsUsuarios = await IdsEnNubeAsync<Usuario>(client);
            foreach (var u in await conn.Table<Usuario>().ToListAsync())
            {
                if (idsUsuarios.Contains(u.Id))
                    continue;
                try { await client.From<Usuario>().Insert(u); subidos++; } catch { }
            }

            // 3) Documentos: inserta los faltantes y actualiza estado/resumen si difieren.
            var idsDocs = await IdsEnNubeAsync<Documento>(client);
            foreach (var d in await conn.Table<Documento>().ToListAsync())
            {
                try
                {
                    if (!idsDocs.Contains(d.Id))
                    {
                        await client.From<Documento>().Insert(d);
                        subidos++;
                    }
                    else
                    {
                        var remoto = await client.From<Documento>().Where(x => x.Id == d.Id).Get();
                        var actual = remoto?.Models?.FirstOrDefault();
                        if (actual is not null &&
                            (actual.Estado != d.Estado || actual.Resumen != d.Resumen))
                        {
                            await client.From<Documento>().Update(d);
                            subidos++;
                        }
                    }
                }
                catch { }
            }

            // 4) Resto de tablas: inserta en la nube las filas que falten.
            subidos += await SubirFaltantes<Tema>(client, conn);
            subidos += await SubirFaltantes<Chat>(client, conn);
            subidos += await SubirFaltantes<Mensaje>(client, conn);
            subidos += await SubirFaltantes<Cuestionario>(client, conn);
            subidos += await SubirFaltantes<Pregunta>(client, conn);
            subidos += await SubirFaltantes<Diagrama>(client, conn);
            subidos += await SubirFaltantes<Resultado>(client, conn);

            return (true, $"Sincronizado: {subidos} cambio(s) subido(s), {borrados} borrado(s) aplicado(s).");
        }
        catch (Exception ex)
        {
            return (false, "No se pudo subir todo: " + ex.Message);
        }
    }

    /// <summary>Descarga todas las tablas de Supabase a SQLite. Devuelve false si no hay conexion.</summary>
    public static async Task<bool> PullAsync()
    {
        var client = await SupabaseService.GetClientAsync();
        if (client is null)
            return false;

        try
        {
            var conn = await LocalDatabase.GetAsync();

            await PullTable<Usuario>(client, conn);
            await PullTable<Documento>(client, conn);
            await PullTable<Tema>(client, conn);
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

    /// <summary>Push + Pull en orden correcto: primero sube lo local, después baja la nube.</summary>
    public static async Task<(bool Ok, string Mensaje)> SincronizarTodoAsync()
    {
        var (okPush, msg) = await PushAsync();
        if (!okPush)
            return (false, msg);

        var okPull = await PullAsync();
        return okPull
            ? (true, msg)
            : (true, msg + " (la descarga no se completó)");
    }

    static async Task<int> SubirFaltantes<T>(Supabase.Client client, SQLite.SQLiteAsyncConnection conn)
        where T : BaseModel, new()
    {
        var idsNube = await IdsEnNubeAsync<T>(client);
        var subidos = 0;

        foreach (var modelo in await conn.Table<T>().ToListAsync())
        {
            var id = ObtenerId(modelo);
            if (id is null || idsNube.Contains(id))
                continue;
            try { await client.From<T>().Insert(modelo); subidos++; } catch { }
        }

        return subidos;
    }

    static async Task<HashSet<string>> IdsEnNubeAsync<T>(Supabase.Client client) where T : BaseModel, new()
    {
        try
        {
            var resp = await client.From<T>().Get();
            return resp?.Models?.Select(ObtenerId).Where(id => id is not null).Cast<string>().ToHashSet()
                   ?? new HashSet<string>();
        }
        catch
        {
            return new HashSet<string>();
        }
    }

    static string? ObtenerId<T>(T modelo)
    {
        var prop = typeof(T).GetProperty("Id");
        return prop?.GetValue(modelo)?.ToString();
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
