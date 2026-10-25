namespace EstudIAnte.Services;

/// <summary>
/// IA alternativa vía Supabase: OpenRouter (modelos gratuitos). Se usa cuando el
/// usuario acepta cambiar en el popup y Gemini y Groq ya están saturadas.
/// </summary>
public class SupabaseOpenRouterProvider : SupabaseIAProvider
{
    public SupabaseOpenRouterProvider() : base("openrouter") { }
}
