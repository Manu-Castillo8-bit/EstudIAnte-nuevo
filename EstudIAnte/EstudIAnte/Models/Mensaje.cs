namespace EstudIAnte.Models;

[SQLite.Table("mensajes")]
[Postgrest.Attributes.Table("mensajes")]
public class Mensaje : SyncModel
{
    [SQLite.PrimaryKey]
    [SQLite.Column("id")]
    [Postgrest.Attributes.PrimaryKey("id", false)]
    [Postgrest.Attributes.Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [SQLite.Column("chat_id")]
    [Postgrest.Attributes.Column("chat_id")]
    public string ChatId { get; set; } = string.Empty;

    [SQLite.Column("rol")]
    [Postgrest.Attributes.Column("rol")]
    public string Rol { get; set; } = "user";

    [SQLite.Column("contenido")]
    [Postgrest.Attributes.Column("contenido")]
    public string Contenido { get; set; } = string.Empty;

    [SQLite.Column("creado_en")]
    [Postgrest.Attributes.Column("creado_en")]
    public string CreadoEn { get; set; } = DateTime.UtcNow.ToString("o");
}
