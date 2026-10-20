namespace EstudIAnte.Models;

[SQLite.Table("usuarios")]
[Postgrest.Attributes.Table("usuarios")]
public class Usuario : SyncModel
{
    [SQLite.PrimaryKey]
    [SQLite.Column("id")]
    [Postgrest.Attributes.PrimaryKey("id", false)]
    [Postgrest.Attributes.Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [SQLite.Column("usuario")]
    [Postgrest.Attributes.Column("usuario")]
    public string NombreUsuario { get; set; } = string.Empty;

    [SQLite.Column("email")]
    [Postgrest.Attributes.Column("email")]
    public string? Email { get; set; }

    [SQLite.Column("password")]
    [Postgrest.Attributes.Column("password")]
    public string Password { get; set; } = string.Empty;

    [SQLite.Column("nombre")]
    [Postgrest.Attributes.Column("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [SQLite.Column("rol")]
    [Postgrest.Attributes.Column("rol")]
    public string Rol { get; set; } = "estudiante";

    [SQLite.Column("creado_en")]
    [Postgrest.Attributes.Column("creado_en")]
    public string CreadoEn { get; set; } = DateTime.UtcNow.ToString("o");

    [SQLite.Column("actualizado_en")]
    [Postgrest.Attributes.Column("actualizado_en")]
    public string ActualizadoEn { get; set; } = DateTime.UtcNow.ToString("o");
}
