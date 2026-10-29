namespace EstudIAnte.Models;

[SQLite.Table("usos_ia")]
[Postgrest.Attributes.Table("usos_ia")]
public class UsoIA : SyncModel
{
    [SQLite.PrimaryKey]
    [SQLite.Column("usuario_id")]
    [Postgrest.Attributes.PrimaryKey("usuario_id", false)]
    [Postgrest.Attributes.Column("usuario_id")]
    public string UsuarioId { get; set; } = string.Empty;

    [SQLite.Column("fecha")]
    [Postgrest.Attributes.Column("fecha")]
    public string Fecha { get; set; } = DateTime.Now.ToString("yyyy-MM-dd");

    [SQLite.Column("consultas_usadas")]
    [Postgrest.Attributes.Column("consultas_usadas")]
    public int ConsultasUsadas { get; set; }

    [SQLite.Column("limite_personalizado")]
    [Postgrest.Attributes.Column("limite_personalizado")]
    public int? LimitePersonalizado { get; set; }

    [SQLite.Column("actualizado_en")]
    [Postgrest.Attributes.Column("actualizado_en")]
    public string ActualizadoEn { get; set; } = DateTime.UtcNow.ToString("o");
}
