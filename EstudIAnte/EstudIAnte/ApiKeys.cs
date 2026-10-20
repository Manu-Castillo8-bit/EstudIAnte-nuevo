namespace EstudIAnte;

/// <summary>
/// Claves de las APIs de IA. Para agregar más proveedores o claves,
/// revisa <see cref="Services.AIProviderManager"/> (lista de solo texto editable a mano).
///
/// IMPORTANTE: no escribas tu clave aquí (este archivo sí se sube al repositorio).
/// La clave se busca, en este orden:
///   1. La variable de entorno <c>ESTUDIANTE_GEMINI_KEY</c>.
///   2. El archivo <c>api-key-gemini.txt</c> en la carpeta de datos de la app
///      (o junto al ejecutable), que está ignorado por git.
/// Si no encuentra ninguna, la IA queda desactivada hasta que configures una.
/// </summary>
public static class ApiKeys
{
    /// <summary>Google AI Studio (Gemini). API key gratuita: https://aistudio.google.com/app/apikey</summary>
    public const string Gemini = "PEGA-AQUI-TU-GEMINI-API-KEY";

    public static string GeminiKey { get; } = ResolverGemini();

    public static bool GeminiConfigured =>
        !string.IsNullOrWhiteSpace(GeminiKey) && !GeminiKey.StartsWith("PEGA-AQUI");

    static string ResolverGemini()
    {
        var entorno = Environment.GetEnvironmentVariable("ESTUDIANTE_GEMINI_KEY");
        if (!string.IsNullOrWhiteSpace(entorno))
            return entorno.Trim();

        foreach (var ruta in RutasPosibles())
        {
            try
            {
                if (File.Exists(ruta))
                {
                    var texto = File.ReadAllText(ruta).Trim();
                    if (!string.IsNullOrWhiteSpace(texto))
                        return texto;
                }
            }
            catch
            {
                // Ignora archivos ilegibles y sigue buscando.
            }
        }

        return Gemini;
    }

    static IEnumerable<string> RutasPosibles()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "api-key-gemini.txt");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                  "EstudIAnte", "api-key-gemini.txt");
    }
}
