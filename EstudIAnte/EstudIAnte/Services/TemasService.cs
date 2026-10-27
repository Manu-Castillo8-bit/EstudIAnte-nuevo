using EstudIAnte.Models;

namespace EstudIAnte.Services;

/// <summary>
/// Crea, lista y elimina los temas de conversación del usuario.
/// Al borrar un tema se eliminan también sus chats y mensajes.
/// </summary>
public static class TemasService
{
    public static async Task<List<Tema>> ListarAsync(string usuarioId)
    {
        var conn = await LocalDatabase.GetAsync();
        return await conn.Table<Tema>()
            .Where(t => t.UsuarioId == usuarioId)
            .OrderBy(t => t.CreadoEn)
            .ToListAsync();
    }

    public static async Task<Tema> CrearAsync(string usuarioId, string nombre)
    {
        var tema = new Tema { UsuarioId = usuarioId, Nombre = nombre };
        var conn = await LocalDatabase.GetAsync();
        await conn.InsertAsync(tema);

        var client = await SupabaseService.GetClientAsync();
        if (client is not null)
        {
            try { await client.From<Tema>().Insert(tema); }
            catch { /* se sincroniza después */ }
        }

        return tema;
    }

    public static async Task EliminarAsync(Tema tema)
    {
        var conn = await LocalDatabase.GetAsync();

        var chats = await conn.Table<Chat>().Where(c => c.Tema == tema.Nombre && c.UsuarioId == tema.UsuarioId).ToListAsync();
        foreach (var chat in chats)
        {
            var mensajes = await conn.Table<Mensaje>().Where(m => m.ChatId == chat.Id).ToListAsync();
            foreach (var mensaje in mensajes)
                await SyncService.MarcarBorradoAsync("mensajes", mensaje.Id);

            await conn.Table<Mensaje>().Where(m => m.ChatId == chat.Id).DeleteAsync();
            await SyncService.MarcarBorradoAsync("chats", chat.Id);
            await conn.DeleteAsync(chat);
        }

        await SyncService.MarcarBorradoAsync("temas", tema.Id);
        await conn.DeleteAsync(tema);

        var client = await SupabaseService.GetClientAsync();
        if (client is not null)
        {
            try { await client.From<Tema>().Where(t => t.Id == tema.Id).Delete(); }
            catch { /* se sincroniza después */ }
        }
    }
}
