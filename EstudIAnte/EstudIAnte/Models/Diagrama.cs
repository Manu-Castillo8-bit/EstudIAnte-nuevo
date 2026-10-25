namespace EstudIAnte.Models;

[SQLite.Table("diagramas")]
[Postgrest.Attributes.Table("diagramas")]
public class Diagrama : SyncModel
{
    [SQLite.PrimaryKey]
    [SQLite.Column("id")]
    [Postgrest.Attributes.PrimaryKey("id", false)]
    [Postgrest.Attributes.Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [SQLite.Column("usuario_id")]
    [Postgrest.Attributes.Column("usuario_id")]
    public string UsuarioId { get; set; } = string.Empty;

    [SQLite.Column("documento_id")]
    [Postgrest.Attributes.Column("documento_id")]
    public string? DocumentoId { get; set; }

    [SQLite.Column("titulo")]
    [Postgrest.Attributes.Column("titulo")]
    public string Titulo { get; set; } = string.Empty;

    [SQLite.Column("codigo")]
    [Postgrest.Attributes.Column("codigo")]
    public string Codigo { get; set; } = string.Empty;

    [SQLite.Column("tipo")]
    [Postgrest.Attributes.Column("tipo")]
    public string Tipo { get; set; } = "flujo";

    [SQLite.Column("fuente")]
    [Postgrest.Attributes.Column("fuente")]
    public string Fuente { get; set; } = string.Empty;

    [SQLite.Column("creado_en")]
    [Postgrest.Attributes.Column("creado_en")]
    public string CreadoEn { get; set; } = DateTime.UtcNow.ToString("o");
}
