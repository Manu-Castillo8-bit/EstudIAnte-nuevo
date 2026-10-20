namespace EstudIAnte.Models;

[SQLite.Table("chats")]
[Postgrest.Attributes.Table("chats")]
public class Chat : SyncModel
{
    [SQLite.PrimaryKey]
    [SQLite.Column("id")]
    [Postgrest.Attributes.PrimaryKey("id", false)]
    [Postgrest.Attributes.Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [SQLite.Column("usuario_id")]
    [Postgrest.Attributes.Column("usuario_id")]
    public string UsuarioId { get; set; } = string.Empty;

    [SQLite.Column("tema")]
    [Postgrest.Attributes.Column("tema")]
    public string Tema { get; set; } = string.Empty;

    [SQLite.Column("titulo")]
    [Postgrest.Attributes.Column("titulo")]
    public string Titulo { get; set; } = "Nueva conversacion";

    [SQLite.Column("creado_en")]
    [Postgrest.Attributes.Column("creado_en")]
    public string CreadoEn { get; set; } = DateTime.UtcNow.ToString("o");
}
