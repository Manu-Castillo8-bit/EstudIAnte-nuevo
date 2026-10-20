namespace EstudIAnte.Models;

[SQLite.Table("cuestionarios")]
[Postgrest.Attributes.Table("cuestionarios")]
public class Cuestionario : SyncModel
{
    [SQLite.PrimaryKey]
    [SQLite.Column("id")]
    [Postgrest.Attributes.PrimaryKey("id", false)]
    [Postgrest.Attributes.Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [SQLite.Column("documento_id")]
    [Postgrest.Attributes.Column("documento_id")]
    public string? DocumentoId { get; set; }

    [SQLite.Column("usuario_id")]
    [Postgrest.Attributes.Column("usuario_id")]
    public string UsuarioId { get; set; } = string.Empty;

    [SQLite.Column("titulo")]
    [Postgrest.Attributes.Column("titulo")]
    public string Titulo { get; set; } = string.Empty;

    [SQLite.Column("nivel")]
    [Postgrest.Attributes.Column("nivel")]
    public string Nivel { get; set; } = "Medio";

    [SQLite.Column("total_preguntas")]
    [Postgrest.Attributes.Column("total_preguntas")]
    public int TotalPreguntas { get; set; } = 5;

    [SQLite.Column("tiempo_limite")]
    [Postgrest.Attributes.Column("tiempo_limite")]
    public int TiempoLimite { get; set; } = 10;

    [SQLite.Column("creado_en")]
    [Postgrest.Attributes.Column("creado_en")]
    public string CreadoEn { get; set; } = DateTime.UtcNow.ToString("o");
}
