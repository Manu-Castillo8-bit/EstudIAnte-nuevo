namespace EstudIAnte.Models;

/// <summary>
/// Registro local de un elemento borrado mientras no había conexión.
/// SyncService lo usa para aplicar el borrado en Supabase al sincronizar,
/// evitando que la descarga lo restaure (efecto "fantasma").
/// </summary>
[SQLite.Table("sync_borrados")]
public class SyncBorrado
{
    [SQLite.PrimaryKey]
    [SQLite.Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [SQLite.Column("tabla")]
    public string Tabla { get; set; } = string.Empty;

    [SQLite.Column("registro_id")]
    public string RegistroId { get; set; } = string.Empty;

    [SQLite.Column("borrado_en")]
    public string BorradoEn { get; set; } = DateTime.UtcNow.ToString("o");
}
