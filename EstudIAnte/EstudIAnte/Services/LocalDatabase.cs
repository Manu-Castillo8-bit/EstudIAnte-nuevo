using SQLite;

namespace EstudIAnte.Services;

/// <summary>
/// Base de datos local (SQLite). Caché offline de todas las tablas.
/// El archivo se crea automáticamente en el directorio de datos de la app.
/// </summary>
public static class LocalDatabase
{
    static SQLiteAsyncConnection? _connection;
    static readonly SemaphoreSlim _lock = new(1, 1);

    public static async Task<SQLiteAsyncConnection> GetAsync()
    {
        if (_connection is not null)
            return _connection;

        await _lock.WaitAsync();
        try
        {
            if (_connection is not null)
                return _connection;

            var path = Path.Combine(FileSystem.AppDataDirectory, "estudiante.db3");
            var conn = new SQLiteAsyncConnection(path,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);

            await conn.CreateTableAsync<Models.Usuario>();
            await conn.CreateTableAsync<Models.Tema>();
            await conn.CreateTableAsync<Models.Documento>();
            await conn.CreateTableAsync<Models.Chat>();
            await conn.CreateTableAsync<Models.Mensaje>();
            await conn.CreateTableAsync<Models.Cuestionario>();
            await conn.CreateTableAsync<Models.Pregunta>();
            await conn.CreateTableAsync<Models.Diagrama>();
            await conn.CreateTableAsync<Models.Resultado>();
            await conn.CreateTableAsync<Models.SyncBorrado>();
            await conn.CreateTableAsync<Models.UsoIA>();

            await SembrarAdminAsync(conn);

            _connection = conn;
            return _connection;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Crea un usuario admin local la primera vez, para poder entrar
    /// sin internet con admin / admin123 (igual que el seed de Supabase).
    /// </summary>
    static async Task SembrarAdminAsync(SQLiteAsyncConnection conn)
    {
        var admin = await conn.Table<Models.Usuario>()
            .Where(u => u.Rol == "admin")
            .FirstOrDefaultAsync();

        if (admin is not null)
            return;

        await conn.InsertAsync(new Models.Usuario
        {
            NombreUsuario = "admin",
            Password = "admin123",
            Email = "admin@estudiante.com",
            Nombre = "Administrador",
            Rol = "admin"
        });
    }
}
