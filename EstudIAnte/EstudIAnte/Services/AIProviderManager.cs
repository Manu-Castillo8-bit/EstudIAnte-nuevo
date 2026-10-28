namespace EstudIAnte.Services;

/// <summary>
/// Administra las IAs disponibles en orden de prioridad:
///   1. Google Gemini (vía Supabase)
///   2. Groq / GPT-OSS (vía Supabase)
///   3. OpenRouter, modelos gratuitos (vía Supabase)
///   4. Exa, búsqueda web con fuentes (vía Supabase)
///   5. Gemini local con clave propia (opcional, solo si hay api-key-gemini.txt)
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
            new SupabaseOpenRouterProvider(),

            // 4) Exa vía Supabase: búsqueda web y respuestas con fuentes.
            new SupabaseExaProvider()
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

    /// <summary>Genera una respuesta usando un proveedor concreto, sin probar otros.</summary>
    public static async Task<(string Texto, string Proveedor)> GenerarConProveedorAsync(
        string prompt, IAIProvider proveedor, CancellationToken ct = default, bool buscarEnWeb = false)
    {
        if (!proveedor.Configurado)
            throw new IAException($"{proveedor.Nombre} no está configurado.");

        await VerificarLimiteIndividualAsync();
        var texto = await proveedor.GenerarAsync(prompt, ct, buscarEnWeb);
        UltimoProveedor = proveedor.Nombre;
        await UsoIAService.RegistrarUsoAsync(AuthService.UsuarioActual?.Id, proveedor.Nombre,
            (prompt.Length + (texto?.Length ?? 0)) / 4);
        return (texto, proveedor.Nombre);
    }

    /// <summary>
    /// Genera texto con la primera IA disponible. Si una IA está saturada, pregunta
    /// al usuario si desea cambiar a la siguiente. Devuelve también el nombre de la
    /// IA usada para poder mostrarlo al usuario.
    /// </summary>
    public static async Task<(string Texto, string Proveedor)> GenerarAsync(
        string prompt, CancellationToken ct = default, bool buscarEnWeb = false)
    {
        var proveedores = Proveedores()
            .Where(p => p.Configurado && (buscarEnWeb || p is not SupabaseExaProvider))
            .ToList();
        if (proveedores.Count == 0)
            throw new IAException("No hay ninguna IA configurada. Agrega una API key en ApiKeys.cs.");

        await VerificarLimiteIndividualAsync();

        if (buscarEnWeb)
            proveedores = proveedores.OrderBy(p => p is SupabaseExaProvider ? 0 : 1).ToList();

        var errores = new List<string>();

        for (var i = 0; i < proveedores.Count; i++)
        {
            var proveedor = proveedores[i];
            if (buscarEnWeb && !EsProveedorWeb(proveedor))
                continue;

            try
            {
                var texto = await proveedor.GenerarAsync(prompt, ct, buscarEnWeb);
                UltimoProveedor = proveedor.Nombre;
                await UsoIAService.RegistrarUsoAsync(AuthService.UsuarioActual?.Id, proveedor.Nombre,
                    (prompt.Length + (texto?.Length ?? 0)) / 4);
                return (texto, proveedor.Nombre);
            }
            catch (IAException ex) when (ex.CuotaAgotada)
            {
                errores.Add($"{proveedor.Nombre}: saturada ({ex.Message})");
                if (buscarEnWeb && proveedor is not SupabaseExaProvider)
                    continue;

                if (buscarEnWeb)
                    throw new IAException(
                        "No se pudo completar la búsqueda web con Exa. Inténtalo de nuevo más tarde.",
                        true);

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
                if (ct.IsCancellationRequested)
                    throw new OperationCanceledException(ct);

                // Otro error (sin conexión, respuesta inválida...): prueba la siguiente IA.
                errores.Add($"{proveedor.Nombre}: {ex.Message}");
            }
        }

        if (buscarEnWeb)
            throw new IAException(
                "No se pudo completar una búsqueda web con Exa. No se generó una respuesta como si estuviera verificada con información actual.\n"
                + string.Join("\n", errores));

        throw new IAException("Ninguna IA pudo responder:\n" + string.Join("\n", errores));
    }

    static bool EsProveedorWeb(IAIProvider proveedor) => proveedor is SupabaseExaProvider;

    static async Task VerificarLimiteIndividualAsync()
    {
        var usuarioId = AuthService.UsuarioActual?.Id;
        if (string.IsNullOrWhiteSpace(usuarioId))
            return;

        var (permitido, usadas, restantes) = await UsoIAService.PuedeUsarAsync(usuarioId);
        if (permitido)
            return;

        throw new IAException(
            $"Tu cuenta alcanzó el límite diario de uso de IA ({UsoIAService.LimiteDiario} llamadas). " +
            $"Usadas hoy: {usadas}. Disponibles mañana.", true);
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
