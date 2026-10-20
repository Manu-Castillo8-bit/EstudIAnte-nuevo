namespace EstudIAnte;

/// <summary>
/// Claves de las APIs de IA. Para agregar más proveedores o claves,
/// revisa <see cref="Services.AIProviderManager"/> (lista de solo texto editable a mano).
/// </summary>
public static class ApiKeys
{
    /// <summary>Google AI Studio (Gemini). API key gratuita: https://aistudio.google.com/app/apikey</summary>
    public const string Gemini = "PEGA-AQUI-TU-GEMINI-API-KEY";

    public static bool GeminiConfigured =>
        !string.IsNullOrWhiteSpace(Gemini) && !Gemini.StartsWith("PEGA-AQUI");
}
