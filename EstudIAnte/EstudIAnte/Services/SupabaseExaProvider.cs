namespace EstudIAnte.Services;

/// <summary>Respuestas con búsqueda web y fuentes mediante Exa, vía Supabase.</summary>
public class SupabaseExaProvider : SupabaseIAProvider
{
    public SupabaseExaProvider() : base("exa") { }
}
