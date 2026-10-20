namespace EstudIAnte.Models;

[SQLite.Table("resultados")]
[Postgrest.Attributes.Table("resultados")]
public class Resultado : SyncModel
{
    [SQLite.PrimaryKey]
    [SQLite.Column("id")]
    [Postgrest.Attributes.PrimaryKey("id", false)]
    [Postgrest.Attributes.Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [SQLite.Column("cuestionario_id")]
    [Postgrest.Attributes.Column("cuestionario_id")]
    public string CuestionarioId { get; set; } = string.Empty;

    [SQLite.Column("usuario_id")]
    [Postgrest.Attributes.Column("usuario_id")]
    public string UsuarioId { get; set; } = string.Empty;

    [SQLite.Column("puntaje")]
    [Postgrest.Attributes.Column("puntaje")]
    public int Puntaje { get; set; }

    [SQLite.Column("total")]
    [Postgrest.Attributes.Column("total")]
    public int Total { get; set; }

    [SQLite.Column("tiempo_segundos")]
    [Postgrest.Attributes.Column("tiempo_segundos")]
    public int TiempoSegundos { get; set; }

    [SQLite.Column("fecha")]
    [Postgrest.Attributes.Column("fecha")]
    public string Fecha { get; set; } = DateTime.UtcNow.ToString("o");
}
