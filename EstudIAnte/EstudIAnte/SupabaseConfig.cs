namespace EstudIAnte;

/// <summary>
/// Datos de conexión a tu proyecto de Supabase.
/// Dashboard de Supabase -> Project Settings -> API
/// </summary>
public static class SupabaseConfig
{
    public const string Url = "https://qywxhtvlbpnzkcgxhbgo.supabase.co";
    public const string AnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InF5d3hodHZsYnBuemtjZ3hoYmdvIiwicm9sZSI6ImFub24iLCJpYXQiOjE3OTI0MzA0OTcsImV4cCI6MjEwODAwNjQ5N30.P2RJrfpaCkpdK104QU2DYm5Jw2uNz9LumF8yLwBqfKY";

    public static bool IsConfigured =>
        Url.StartsWith("http") && !Url.Contains("TU-PROYECTO") && !AnonKey.StartsWith("PEGA-AQUI");
}
