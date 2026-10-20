namespace EstudIAnte.Models;

[SQLite.Table("documentos")]
[Postgrest.Attributes.Table("documentos")]
public class Documento : SyncModel
{
    [SQLite.PrimaryKey]
    [SQLite.Column("id")]
    [Postgrest.Attributes.PrimaryKey("id", false)]
    [Postgrest.Attributes.Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [SQLite.Column("usuario_id")]
    [Postgrest.Attributes.Column("usuario_id")]
    public string UsuarioId { get; set; } = string.Empty;

    [SQLite.Column("nombre")]
    [Postgrest.Attributes.Column("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [SQLite.Column("tipo")]
    [Postgrest.Attributes.Column("tipo")]
    public string Tipo { get; set; } = "pdf";

    [SQLite.Column("tamano_bytes")]
    [Postgrest.Attributes.Column("tamano_bytes")]
    public long TamanoBytes { get; set; }

    [SQLite.Column("ruta_storage")]
    [Postgrest.Attributes.Column("ruta_storage")]
    public string? RutaStorage { get; set; }

    [SQLite.Column("resumen")]
    [Postgrest.Attributes.Column("resumen")]
    public string Resumen { get; set; } = string.Empty;

    [SQLite.Column("texto")]
    [Postgrest.Attributes.Column("texto")]
    public string Texto { get; set; } = string.Empty;

    [SQLite.Column("estado")]
    [Postgrest.Attributes.Column("estado")]
    public string Estado { get; set; } = "subido";

    [SQLite.Column("creado_en")]
    [Postgrest.Attributes.Column("creado_en")]
    public string CreadoEn { get; set; } = DateTime.UtcNow.ToString("o");
}
