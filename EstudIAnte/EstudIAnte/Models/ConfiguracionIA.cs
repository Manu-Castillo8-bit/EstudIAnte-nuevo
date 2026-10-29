namespace EstudIAnte.Models;

[SQLite.Table("configuracion_ia")]
[Postgrest.Attributes.Table("configuracion_ia")]
public class ConfiguracionIA : SyncModel
{
    public const string IdGlobal = "global";

    [SQLite.PrimaryKey]
    [SQLite.Column("id")]
    [Postgrest.Attributes.PrimaryKey("id", false)]
    [Postgrest.Attributes.Column("id")]
    public string Id { get; set; } = IdGlobal;

    [SQLite.Column("limite_habilitado")]
    [Postgrest.Attributes.Column("limite_habilitado")]
    public bool LimiteHabilitado { get; set; } = true;

    [SQLite.Column("limite_diario")]
    [Postgrest.Attributes.Column("limite_diario")]
    public int LimiteDiario { get; set; } = 3;

    [SQLite.Column("actualizado_en")]
    [Postgrest.Attributes.Column("actualizado_en")]
    public string ActualizadoEn { get; set; } = DateTime.UtcNow.ToString("o");
}
