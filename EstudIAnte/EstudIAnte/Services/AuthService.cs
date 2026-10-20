using EstudIAnte.Models;
using SQLite;

namespace EstudIAnte.Services;

/// <summary>
/// Login, registro y sesion. La fuente de verdad es Supabase;
/// SQLite guarda la sesion y los datos para funcionar sin internet.
/// </summary>
public static class AuthService
{
    const string KeyUsuarioId = "sesion_usuario_id";
    const string KeyRecordar = "sesion_recordar";

    public static Usuario? UsuarioActual { get; private set; }

    public static async Task<(bool Ok, string Error)> LoginAsync(string usuario, string clave, bool recordar)
    {
        Usuario? encontrado = null;

        // 1) Intenta contra Supabase (nube)
        var client = await SupabaseService.GetClientAsync();
        if (client is not null)
        {
            try
            {
                var resp = await client.From<Usuario>()
                    .Where(u => u.NombreUsuario == usuario && u.Password == clave)
                    .Get();
                encontrado = resp?.Models?.FirstOrDefault();
            }
            catch
            {
                // Sin internet: seguimos con la base local
            }
        }

        // 2) Fallback local (SQLite)
        if (encontrado is null)
        {
            var conn = await LocalDatabase.GetAsync();
            encontrado = await conn.Table<Usuario>()
                .Where(u => u.NombreUsuario == usuario && u.Password == clave)
                .FirstOrDefaultAsync();
        }

        if (encontrado is null)
            return (false, "Usuario o contraseña incorrectos.");

        await GuardarSesionAsync(encontrado, recordar);
        return (true, string.Empty);
    }

    public static async Task<(bool Ok, string Error)> RegistrarAsync(string usuario, string clave, string? email = null)
    {
        var nuevo = new Usuario
        {
            NombreUsuario = usuario,
            Password = clave,
            Email = email,
            Nombre = usuario
        };

        // Guarda siempre en SQLite (funciona offline)
        var conn = await LocalDatabase.GetAsync();
        var existe = await conn.Table<Usuario>()
            .Where(u => u.NombreUsuario == usuario)
            .FirstOrDefaultAsync();
        if (existe is not null)
            return (false, "Ya existe una cuenta con ese usuario.");

        await conn.InsertAsync(nuevo);

        // Intenta sincronizar a la nube (si no hay internet queda pendiente)
        var client = await SupabaseService.GetClientAsync();
        if (client is not null)
        {
            try
            {
                await client.From<Usuario>().Insert(nuevo);
            }
            catch
            {
                // Se sincronizara despues con SyncService
            }
        }

        return (true, string.Empty);
    }

    public static async Task<Usuario?> RestaurarSesionAsync()
    {
        if (!Preferences.Default.Get(KeyRecordar, false))
            return null;

        var id = Preferences.Default.Get(KeyUsuarioId, string.Empty);
        if (string.IsNullOrEmpty(id))
            return null;

        var conn = await LocalDatabase.GetAsync();
        var u = await conn.Table<Usuario>().Where(x => x.Id == id).FirstOrDefaultAsync();
        UsuarioActual = u;
        return u;
    }

    public static void CerrarSesion()
    {
        Preferences.Default.Remove(KeyUsuarioId);
        Preferences.Default.Remove(KeyRecordar);
        UsuarioActual = null;
    }

    static async Task GuardarSesionAsync(Usuario u, bool recordar)
    {
        UsuarioActual = u;

        Preferences.Default.Set(KeyUsuarioId, u.Id);
        Preferences.Default.Set(KeyRecordar, recordar);

        // Siempre persiste el usuario en local para uso offline
        var conn = await LocalDatabase.GetAsync();
        await conn.InsertOrReplaceAsync(u);
    }
}
