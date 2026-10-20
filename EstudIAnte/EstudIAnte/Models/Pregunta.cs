namespace EstudIAnte.Models;

[SQLite.Table("preguntas")]
[Postgrest.Attributes.Table("preguntas")]
public class Pregunta : SyncModel
{
    [SQLite.PrimaryKey]
    [SQLite.Column("id")]
    [Postgrest.Attributes.PrimaryKey("id", false)]
    [Postgrest.Attributes.Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [SQLite.Column("cuestionario_id")]
    [Postgrest.Attributes.Column("cuestionario_id")]
    public string CuestionarioId { get; set; } = string.Empty;

    [SQLite.Column("enunciado")]
    [Postgrest.Attributes.Column("enunciado")]
    public string Enunciado { get; set; } = string.Empty;

    [SQLite.Column("opcion_a")]
    [Postgrest.Attributes.Column("opcion_a")]
    public string OpcionA { get; set; } = string.Empty;

    [SQLite.Column("opcion_b")]
    [Postgrest.Attributes.Column("opcion_b")]
    public string OpcionB { get; set; } = string.Empty;

    [SQLite.Column("opcion_c")]
    [Postgrest.Attributes.Column("opcion_c")]
    public string OpcionC { get; set; } = string.Empty;

    [SQLite.Column("opcion_d")]
    [Postgrest.Attributes.Column("opcion_d")]
    public string OpcionD { get; set; } = string.Empty;

    [SQLite.Column("respuesta_correcta")]
    [Postgrest.Attributes.Column("respuesta_correcta")]
    public string RespuestaCorrecta { get; set; } = "A";

    [SQLite.Column("tip")]
    [Postgrest.Attributes.Column("tip")]
    public string Tip { get; set; } = string.Empty;

    [SQLite.Column("fuente")]
    [Postgrest.Attributes.Column("fuente")]
    public string Fuente { get; set; } = string.Empty;
}
