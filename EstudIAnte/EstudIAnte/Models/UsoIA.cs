using Postgrest.Attributes;

namespace EstudIAnte.Models;

[SQLite.Table("uso_ia")]
[Postgrest.Attributes.Table("uso_ia")]
public class UsoIA : SyncModel
{
    [SQLite.PrimaryKey, SQLite.AutoIncrement]
    [SQLite.Column("id")]
    [Postgrest.Attributes.PrimaryKey("id", false)]
    [Postgrest.Attributes.Column("id")]
    public int Id { get; set; }

    [SQLite.Column("usuario_id")]
    [Postgrest.Attributes.Column("usuario_id")]
    public string UsuarioId { get; set; } = string.Empty;

    [SQLite.Column("fecha")]
    [Postgrest.Attributes.Column("fecha")]
    public string Fecha { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");

    [SQLite.Column("llamadas")]
    [Postgrest.Attributes.Column("llamadas")]
    public int Llamadas { get; set; }

    [SQLite.Column("tokens_aprox")]
    [Postgrest.Attributes.Column("tokens_aprox")]
    public int TokensAprox { get; set; }

    [SQLite.Column("actualizado_en")]
    [Postgrest.Attributes.Column("actualizado_en")]
    public string ActualizadoEn { get; set; } = DateTime.UtcNow.ToString("o");
}