namespace EstudIAnte.Services;

/// <summary>
/// IA alternativa vía Supabase: Groq (GPT-OSS). Se usa cuando el usuario acepta
/// cambiar en el popup que aparece cuando Gemini está saturada.
/// </summary>
public class SupabaseGroqProvider : SupabaseIAProvider
{
    public SupabaseGroqProvider() : base("groq") { }
}
