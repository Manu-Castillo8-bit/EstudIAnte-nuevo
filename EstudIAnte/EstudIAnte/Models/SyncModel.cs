namespace EstudIAnte.Models;

/// <summary>
/// Base común para los modelos que se usan a la vez en SQLite y en Supabase.
/// Oculta propiedades heredadas de <see cref="Postgrest.Models.BaseModel"/>
/// (como RequestClientOptions) que sqlite-net no sabe mapear a columnas.
/// </summary>
public abstract class SyncModel : Postgrest.Models.BaseModel
{
    [SQLite.Ignore]
    [Newtonsoft.Json.JsonIgnore]
    public new string? BaseUrl { get; set; }

    [SQLite.Ignore]
    [Newtonsoft.Json.JsonIgnore]
    public new Postgrest.ClientOptions? RequestClientOptions { get; set; }
}
