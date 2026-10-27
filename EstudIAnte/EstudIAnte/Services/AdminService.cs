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

    /// <summary>Elimina un usuario (los FK en Supabase borran su contenido en cascada).</summary>
    public static async Task EliminarUsuarioAsync(Usuario u)
    {
        await SyncService.MarcarBorradoAsync("usuarios", u.Id);

        var conn = await LocalDatabase.GetAsync();
        await conn.DeleteAsync(u);

        var client = await SupabaseService.GetClientAsync();
        if (client is not null)
        {
            try { await client.From<Usuario>().Where(x => x.Id == u.Id).Delete(); }
            catch { /* se registra el borrado y se aplica al sincronizar */ }
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

    /// <summary>Elimina un documento (incluye su archivo en Storage).</summary>
    public static async Task EliminarDocumentoAsync(Documento doc)
    {
        await SyncService.MarcarBorradoAsync("documentos", doc.Id);

        var conn = await LocalDatabase.GetAsync();
        await conn.DeleteAsync(doc);

        var client = await SupabaseService.GetClientAsync();
        if (client is null) return;

        try
        {
            if (!string.IsNullOrWhiteSpace(doc.RutaStorage))
                await client.Storage.From("documentos").Remove(doc.RutaStorage);
        }
        catch { /* el archivo puede no existir */ }

        try { await client.From<Documento>().Where(d => d.Id == doc.Id).Delete(); }
        catch { /* la tumba se aplica al sincronizar */ }
    }

    /// <summary>Elimina un cuestionario (sus preguntas se borran por FK).</summary>
    public static async Task EliminarCuestionarioAsync(Cuestionario quiz)
    {
        await SyncService.MarcarBorradoAsync("cuestionarios", quiz.Id);

        var conn = await LocalDatabase.GetAsync();
        await conn.DeleteAsync(quiz);

        var client = await SupabaseService.GetClientAsync();
        if (client is null) return;

        try { await client.From<Cuestionario>().Where(c => c.Id == quiz.Id).Delete(); }
        catch { /* la tumba se aplica al sincronizar */ }
    }

    /// <summary>Elimina un diagrama.</summary>
    public static async Task EliminarDiagramaAsync(Diagrama d)
    {
        await SyncService.MarcarBorradoAsync("diagramas", d.Id);

        var conn = await LocalDatabase.GetAsync();
        await conn.DeleteAsync(d);

        var client = await SupabaseService.GetClientAsync();
        if (client is null) return;

        try { await client.From<Diagrama>().Where(x => x.Id == d.Id).Delete(); }
        catch { /* la tumba se aplica al sincronizar */ }
    }

    /// <summary>Elimina un resultado de cuestionario.</summary>
    public static async Task EliminarResultadoAsync(Resultado r)
    {
        await SyncService.MarcarBorradoAsync("resultados", r.Id);

        var conn = await LocalDatabase.GetAsync();
        await conn.DeleteAsync(r);

        var client = await SupabaseService.GetClientAsync();
        if (client is null) return;

        try { await client.From<Resultado>().Where(x => x.Id == r.Id).Delete(); }
        catch { /* la tumba se aplica al sincronizar */ }
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
