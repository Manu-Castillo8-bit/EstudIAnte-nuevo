namespace EstudIAnte.Services;

/// <summary>
/// Administra las IAs disponibles en orden de prioridad:
///   1. Google Gemini (vía Supabase)
///   2. Groq / GPT-OSS (vía Supabase)
///   3. OpenRouter, modelos gratuitos (vía Supabase)
///   4. Gemini local con clave propia (opcional, solo si hay api-key-gemini.txt)
///
/// ---- COMPORTAMIENTO ANTE SATURACIÓN ----
/// Cuando una IA está saturada (cuota agotada) se muestra un aviso al usuario:
///   "¿Quieres cambiar a <IA alternativa>?"
///   - Sí  -> se usa la siguiente IA (y así sucesivamente).
///   - No  -> se cancela con un mensaje para reintentar más tarde.
/// Cualquier otro error (p. ej. sin conexión) pasa a la siguiente IA
/// automáticamente, sin preguntar.
///
/// ---- CÓMO AGREGAR MÁS IAs ----
/// 1. Si es Gemini: agrega otra línea en <see cref="Proveedores"/> con otra API key
///    (crea claves gratis en https://aistudio.google.com/app/apikey).
/// 2. Si es otro servicio: crea una clase que implemente <see cref="IAIProvider"/>
///    (o una subclase de <see cref="SupabaseIAProvider"/>) y añádela a la lista.
/// El orden de la lista es la prioridad de uso.
/// </summary>
public static class AIProviderManager
{
    /// <summary>IA que se usó en la última petición exitosa.</summary>
    public static string UltimoProveedor { get; private set; } = "(ninguna todavía)";

    public static List<IAIProvider> Proveedores()
    {
        var lista = new List<IAIProvider>
        {
            // 1) Gemini vía Supabase: clave en el servidor, funciona en cualquier clon.
            new SupabaseGeminiProvider(),

            // 2) Groq vía Supabase: alternativa gratuita para cuando Gemini esté saturada.
            new SupabaseGroqProvider(),

            // 3) OpenRouter vía Supabase: segunda alternativa (modelos gratuitos).
            new SupabaseOpenRouterProvider()
        };

        // 3) Respaldo opcional: Gemini con clave propia (api-key-gemini.txt o
        //    variable de entorno ESTUDIANTE_GEMINI_KEY).
        if (ApiKeys.GeminiConfigured)
        {
            lista.Add(new GeminiProvider(ApiKeys.GeminiKey, "gemini-3.8-flash"));
        }

        return lista;
    }

    public static bool HayAlgunProveedor => Proveedores().Any(p => p.Configurado);

    /// <summary>
    /// Genera texto con la primera IA disponible. Si una IA está saturada, pregunta
    /// al usuario si desea cambiar a la siguiente. Devuelve también el nombre de la
    /// IA usada para poder mostrarlo al usuario.
    /// </summary>
    public static async Task<(string Texto, string Proveedor)> GenerarAsync(string prompt, CancellationToken ct = default)
    {
        var proveedores = Proveedores().Where(p => p.Configurado).ToList();
        if (proveedores.Count == 0)
            throw new IAException("No hay ninguna IA configurada. Agrega una API key en ApiKeys.cs.");

        var errores = new List<string>();

        for (var i = 0; i < proveedores.Count; i++)
        {
            var proveedor = proveedores[i];

            try
            {
                var texto = await proveedor.GenerarAsync(prompt, ct);
                UltimoProveedor = proveedor.Nombre;
                return (texto, proveedor.Nombre);
            }
            catch (IAException ex) when (ex.CuotaAgotada)
            {
                errores.Add($"{proveedor.Nombre}: saturada ({ex.Message})");

                if (i + 1 >= proveedores.Count)
                {
                    throw new IAException(
                        "Todas las IAs están saturadas ahora mismo. Espera unos minutos e intenta de nuevo.\n\n" +
                        string.Join("\n", errores), true);
                }

                // Saturada -> el usuario decide si cambia a la siguiente IA.
                var cambiar = await ConfirmarCambioAsync(proveedor.Nombre, proveedores[i + 1].Nombre);
                if (!cambiar)
                {
                    throw new IAException(
                        $"{proveedor.Nombre} está saturada. Espera unos minutos e intenta de nuevo.", true);
                }
                // Sí -> el bucle continúa con la siguiente IA.
            }
            catch (Exception ex)
            {
                // Otro error (sin conexión, respuesta inválida...): prueba la siguiente IA.
                errores.Add($"{proveedor.Nombre}: {ex.Message}");
            }
        }

        throw new IAException("Ninguna IA pudo responder:\n" + string.Join("\n", errores));
    }

    /// <summary>
    /// Pregunta al usuario si quiere cambiar a la IA alternativa.
    /// Si no hay ventana activa devuelve false (nunca cambia sola).
    /// </summary>
    static async Task<bool> ConfirmarCambioAsync(string iaSaturada, string iaAlternativa)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null)
            return false;

        var nombreCorto = iaAlternativa.Split(' ')[0];
        return await page.DisplayAlertAsync(
            "IA saturada",
            $"⚠️ {iaSaturada} está saturada o sin cuota disponible.\n\n" +
            $"¿Quieres cambiar a {iaAlternativa}?",
            $"Sí, usar {nombreCorto}",
            "No, esperar");
    }
}
