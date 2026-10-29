using EstudIAnte.Models;

namespace EstudIAnte.Services;

/// <summary>
/// Operaciones del panel de administración: consultar y gestionar usuarios
/// (incluye ver contraseñas, que se guardan en texto plano por requisito
/// del proyecto), documentos, cuestionarios, diagramas y resultados.
/// Usa la nube cuando hay conexión y la base local como respaldo.
/// </summary>
public static class AdminService
{
    // ---------------- ESTADÍSTICAS ----------------

    /// <summary>Número de registros por tabla (nube si hay conexión, si no local).</summary>
    public static async Task<Dictionary<string, int>> StatsAsync()
    {
        var stats = new Dictionary<string, int>();
        var client = await SupabaseService.GetClientAsync();

        try
        {
            if (client is not null)
            {
                stats["usuarios"] = (await client.From<Usuario>().Get()).Models.Count;
                stats["documentos"] = (await client.From<Documento>().Get()).Models.Count;
                stats["cuestionarios"] = (await client.From<Cuestionario>().Get()).Models.Count;
                stats["diagramas"] = (await client.From<Diagrama>().Get()).Models.Count;
                stats["resultados"] = (await client.From<Resultado>().Get()).Models.Count;
                return stats;
            }
        }
        catch { /* respaldo local */ }

        var conn = await LocalDatabase.GetAsync();
        stats["usuarios"] = await conn.Table<Usuario>().CountAsync();
        stats["documentos"] = await conn.Table<Documento>().CountAsync();
        stats["cuestionarios"] = await conn.Table<Cuestionario>().CountAsync();
        stats["diagramas"] = await conn.Table<Diagrama>().CountAsync();
        stats["resultados"] = await conn.Table<Resultado>().CountAsync();
        return stats;
    }

    // ---------------- USUARIOS ----------------

    /// <summary>Lista todos los usuarios. Devuelve también si venían de la nube.</summary>
    public static async Task<(List<Usuario> Usuarios, bool Nube)> ListarUsuariosAsync()
    {
        var client = await SupabaseService.GetClientAsync();
        if (client is not null)
        {
            try
            {
                var respuesta = await client.From<Usuario>().Get();
                return (respuesta.Models.OrderBy(u => u.NombreUsuario).ToList(), true);
            }
            catch { /* respaldo local */ }
        }

        var conn = await LocalDatabase.GetAsync();
        var locales = await conn.Table<Usuario>().OrderBy(u => u.NombreUsuario).ToListAsync();
        return (locales, false);
    }

    /// <summary>Crea un usuario con rol (estudiante o admin) y contraseña visible.</summary>
    public static async Task<(bool Ok, string Error)> CrearUsuarioAsync(
        string usuario, string clave, string? email, string nombre, string rol)
    {
        var (valido, error) = ValidarAsync(usuario, clave, email);
        if (!valido)
            return (false, error);

        var (existentes, _) = await ListarUsuariosAsync();
        if (existentes.Any(u => u.NombreUsuario.Equals(usuario.Trim(), StringComparison.OrdinalIgnoreCase)))
            return (false, "Ya existe un usuario con ese nombre.");

        var nuevo = new Usuario
        {
            NombreUsuario = usuario.Trim(),
            Password = clave,
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            Nombre = string.IsNullOrWhiteSpace(nombre) ? usuario.Trim() : nombre.Trim(),
            Rol = rol == "admin" ? "admin" : "estudiante",
        };

        var conn = await LocalDatabase.GetAsync();
        await conn.InsertAsync(nuevo);

        var client = await SupabaseService.GetClientAsync();
        if (client is not null)
        {
            try { await client.From<Usuario>().Insert(nuevo); }
            catch { /* se sincroniza después */ }
        }

        return (true, string.Empty);
    }

    /// <summary>Cambia la contraseña de un usuario (se muestra en el panel por requisito).</summary>
    public static async Task CambiarPasswordAsync(Usuario u, string nuevaClave)
    {
        u.Password = nuevaClave;
        u.ActualizadoEn = DateTime.UtcNow.ToString("o");

        var conn = await LocalDatabase.GetAsync();
        await conn.UpdateAsync(u);

        var client = await SupabaseService.GetClientAsync();
        if (client is not null)
        {
            try { await client.From<Usuario>().Update(u); }
            catch { /* se sincroniza después */ }
        }
    }

    /// <summary>Cambia el rol (admin / estudiante).</summary>
    public static async Task CambiarRolAsync(Usuario u, string rol)
    {
        u.Rol = rol == "admin" ? "admin" : "estudiante";
        u.ActualizadoEn = DateTime.UtcNow.ToString("o");
        await CambiarPasswordAsync(u, u.Password); // reutiliza la actualización local+nube
    }

    /// <summary>Elimina un usuario y todo su contenido (local + nube; FKs cascada en Supabase).</summary>
    public static async Task EliminarUsuarioAsync(Usuario u)
    {
        var conn = await LocalDatabase.GetAsync();

        // Documentos con su Storage (los hijos: quizzes, preguntas, resultados, diagramas).
        var docs = await conn.Table<Documento>().Where(d => d.UsuarioId == u.Id).ToListAsync();
        await BorrarDocumentoAsync(conn, docs);
        foreach (var doc in docs)
            await SyncService.MarcarBorradoAsync("documentos", doc.Id);

        // Chats y mensajes.
        var chats = await conn.Table<Chat>().Where(c => c.UsuarioId == u.Id).ToListAsync();
        await BorrarChatsAsync(conn, chats);

        // Temas.
        var temas = await conn.Table<Tema>().Where(t => t.UsuarioId == u.Id).ToListAsync();
        await BorrarFilaAsync(conn, "temas", temas, t => t.Id);

        var usosIA = await conn.Table<UsoIA>().Where(x => x.UsuarioId == u.Id).ToListAsync();
        await BorrarFilaAsync(conn, "usos_ia", usosIA, x => x.UsuarioId);

        // El usuario al final.
        await SyncService.MarcarBorradoAsync("usuarios", u.Id);
        await conn.DeleteAsync(u);

        var client = await SupabaseService.GetClientAsync();
        if (client is not null)
        {
            try { await client.From<Usuario>().Where(x => x.Id == u.Id).Delete(); }
            catch { /* la tumba se aplica al sincronizar */ }
        }
    }

    // ---------------- LISTAS (resto de tablas) ----------------

    public static async Task<List<Documento>> ListarDocumentosAsync()
        => await ListarNubeOLocalAsync<Documento>(d => d.CreadoEn);

    public static async Task<List<Cuestionario>> ListarCuestionariosAsync()
        => await ListarNubeOLocalAsync<Cuestionario>(c => c.CreadoEn);

    public static async Task<List<Diagrama>> ListarDiagramasAsync()
        => await ListarNubeOLocalAsync<Diagrama>(d => d.CreadoEn);

    public static async Task<List<Resultado>> ListarResultadosAsync()
        => await ListarNubeOLocalAsync<Resultado>(r => r.Fecha);

    public static async Task<List<Chat>> ListarChatsAsync()
        => await ListarNubeOLocalAsync<Chat>(c => c.CreadoEn);

    public static async Task<List<Mensaje>> ListarMensajesAsync()
        => await ListarNubeOLocalAsync<Mensaje>(m => m.CreadoEn);

    static async Task<List<T>> ListarNubeOLocalAsync<T>(Func<T, string> ordenarPor) where T : SyncModel, new()
    {
        var client = await SupabaseService.GetClientAsync();
        if (client is not null)
        {
            try
            {
                var respuesta = await client.From<T>().Get();
                return respuesta.Models.OrderByDescending(ordenarPor).ToList();
            }
            catch { /* respaldo local */ }
        }

        var conn = await LocalDatabase.GetAsync();
        var locales = await conn.Table<T>().ToListAsync();
        return locales.OrderByDescending(ordenarPor).ToList();
    }

    // ---------------- BORRADO EN CASCADA ----------------

    /// <summary>Borra de forma TOTAL: local + nube, con toda la cascada de datos hijos.</summary>
    public static async Task EliminarDocumentoAsync(Documento doc)
    {
        var conn = await LocalDatabase.GetAsync();
        await BorrarDocumentoAsync(conn, new List<Documento> { doc });
    }

    public static async Task EliminarCuestionarioAsync(Cuestionario quiz)
    {
        var conn = await LocalDatabase.GetAsync();
        await BorrarCuestionarioAsync(conn, new List<Cuestionario> { quiz });
    }

    public static async Task EliminarDiagramaAsync(Diagrama diagrama)
    {
        var conn = await LocalDatabase.GetAsync();
        await BorrarDiagramasAsync(conn, new List<Diagrama> { diagrama });
    }

    public static async Task EliminarResultadoAsync(Resultado resultado)
    {
        var conn = await LocalDatabase.GetAsync();
        await BorrarResultadosAsync(conn, new List<Resultado> { resultado });
    }

    public static async Task EliminarChatAsync(Chat chat)
    {
        var conn = await LocalDatabase.GetAsync();
        await BorrarChatsAsync(conn, new List<Chat> { chat });
    }

    /// <summary>
    /// Borra de forma TOTAL una lista de documentos (los llama la vista/panel admin).
    /// </summary>
    public static async Task EliminarDocumentosAsync(IEnumerable<Documento> docs)
    {
        var conn = await LocalDatabase.GetAsync();
        await BorrarDocumentoAsync(conn, docs.ToList());
    }

    // (Borrados en cascada internos)

    /// <summary>Borra una fila localmente y la marca para borrarla en la nube.</summary>
    static async Task BorrarFilaAsync<T>(SQLite.SQLiteAsyncConnection conn, string tabla, List<T> filas, Func<T, string> idDe)
    {
        foreach (var fila in filas)
        {
            await SyncService.MarcarBorradoAsync(tabla, idDe(fila));
            await conn.DeleteAsync(fila);
        }
    }

    /// <summary>
    /// Borra TODO el contenido relacionado con un documento: el documento,
    /// sus cuestionarios y preguntas, los resultados y sus diagramas.
    /// </summary>
    static async Task BorrarDocumentoAsync(SQLite.SQLiteAsyncConnection conn, List<Documento> docs)
    {
        var ids = docs.Select(d => d.Id).ToList();

        var (quizzes, preguntas, diagramas, resultados, mensajes, chats, usuarios) = await RelacionadosDocAsync(conn, ids);

        await BorrarFilaAsync(conn, "resultados", resultados, r => r.Id);
        await BorrarFilaAsync(conn, "preguntas", preguntas, p => p.Id);
        await BorrarFilaAsync(conn, "cuestionarios", quizzes, q => q.Id);
        await BorrarFilaAsync(conn, "diagramas", diagramas, d => d.Id);
        await BorrarFilaAsync(conn, "documentos", docs, d => d.Id);

        var client = await SupabaseService.GetClientAsync();
        if (client is not null)
        {
            foreach (var doc in docs)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(doc.RutaStorage))
                        await client.Storage.From("documentos").Remove(doc.RutaStorage);
                }
                catch { /* el archivo puede no existir */ }
            }

            try
            {
                foreach (var doc in docs)
                    await client.From<Documento>().Where(d => d.Id == doc.Id).Delete();
            }
            catch { /* las tumbas se aplican al sincronizar */ }
        }
    }

    /// <summary>Devuelve todos los registros hijos de documentos dados (quizzes, preguntas, diagramas, resultados).</summary>
    static async Task<(List<Cuestionario>, List<Pregunta>, List<Diagrama>, List<Resultado>, List<Mensaje>, List<Chat>, List<Usuario>)>
        RelacionadosDocAsync(SQLite.SQLiteAsyncConnection conn, List<string> idsDocumentos)
    {
        var quizzes = (await conn.Table<Cuestionario>().ToListAsync())
            .Where(q => q.DocumentoId is not null && idsDocumentos.Contains(q.DocumentoId)).ToList();
        var idsQuizzes = quizzes.Select(q => q.Id).ToList();
        var preguntas = (await conn.Table<Pregunta>().ToListAsync())
            .Where(p => idsQuizzes.Contains(p.CuestionarioId ?? "")).ToList();
        var diagramas = (await conn.Table<Diagrama>().ToListAsync())
            .Where(d => d.DocumentoId is not null && idsDocumentos.Contains(d.DocumentoId)).ToList();
        var resultados = (await conn.Table<Resultado>().ToListAsync())
            .Where(r => idsQuizzes.Contains(r.CuestionarioId ?? "")).ToList();
        return (quizzes, preguntas, diagramas, resultados, new List<Mensaje>(), new List<Chat>(), new List<Usuario>());
    }

    /// <summary>
    /// Borra un cuestionario con sus preguntas y resultados.
    /// </summary>
    static async Task BorrarCuestionarioAsync(SQLite.SQLiteAsyncConnection conn, List<Cuestionario> quizzes)
    {
        var ids = quizzes.Select(q => q.Id).ToList();

        var preguntas = (await conn.Table<Pregunta>().ToListAsync()).Where(p => ids.Contains(p.CuestionarioId ?? "")).ToList();
        var resultados = (await conn.Table<Resultado>().ToListAsync()).Where(r => ids.Contains(r.CuestionarioId ?? "")).ToList();

        await BorrarFilaAsync(conn, "resultados", resultados, r => r.Id);
        await BorrarFilaAsync(conn, "preguntas", preguntas, p => p.Id);
        await BorrarFilaAsync(conn, "cuestionarios", quizzes, q => q.Id);

        var client = await SupabaseService.GetClientAsync();
        if (client is not null)
        {
            try
            {
                foreach (var quiz in quizzes)
                    await client.From<Cuestionario>().Where(q => q.Id == quiz.Id).Delete();
            }
            catch { /* las tumbas se aplican al sincronizar */ }
        }
    }

    /// <summary>Borra una lista de chats con todos sus mensajes.</summary>
    static async Task BorrarChatsAsync(SQLite.SQLiteAsyncConnection conn, List<Chat> chats)
    {
        var ids = chats.Select(c => c.Id).ToList();
        var mensajes = (await conn.Table<Mensaje>().ToListAsync()).Where(m => ids.Contains(m.ChatId)).ToList();

        await BorrarFilaAsync(conn, "mensajes", mensajes, m => m.Id);
        await BorrarFilaAsync(conn, "chats", chats, c => c.Id);

        var client = await SupabaseService.GetClientAsync();
        if (client is not null)
        {
            try
            {
                foreach (var chat in chats)
                    await client.From<Chat>().Where(c => c.Id == chat.Id).Delete();
            }
            catch { /* las tumbas se aplican al sincronizar */ }
        }
    }

    /// <summary>Borra diagramas.</summary>
    static async Task BorrarDiagramasAsync(SQLite.SQLiteAsyncConnection conn, List<Diagrama> diagramas)
    {
        await BorrarFilaAsync(conn, "diagramas", diagramas, d => d.Id);

        var client = await SupabaseService.GetClientAsync();
        if (client is not null)
        {
            try
            {
                foreach (var d in diagramas)
                    await client.From<Diagrama>().Where(x => x.Id == d.Id).Delete();
            }
            catch { /* las tumbas se aplican al sincronizar */ }
        }
    }

    /// <summary>Borra resultados de cuestionarios.</summary>
    static async Task BorrarResultadosAsync(SQLite.SQLiteAsyncConnection conn, List<Resultado> resultados)
    {
        await BorrarFilaAsync(conn, "resultados", resultados, r => r.Id);

        var client = await SupabaseService.GetClientAsync();
        if (client is not null)
        {
            try
            {
                foreach (var r in resultados)
                    await client.From<Resultado>().Where(x => x.Id == r.Id).Delete();
            }
            catch { /* las tumbas se aplican al sincronizar */ }
        }
    }

    // ---------------- VALIDACIÓN ----------------

    static (bool, string) ValidarAsync(string usuario, string clave, string? email)
    {
        if (usuario.Trim().Length < 3)
            return (false, "El nombre de usuario debe tener al menos 3 caracteres.");
        if (clave.Length < 4)
            return (false, "La contraseña debe tener al menos 4 caracteres.");
        if (!string.IsNullOrWhiteSpace(email) && !email.Contains('@'))
            return (false, "El correo no es válido (o déjalo vacío).");
        return (true, string.Empty);
    }
}
