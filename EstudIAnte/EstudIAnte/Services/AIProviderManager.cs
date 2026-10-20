namespace EstudIAnte.Services;

/// <summary>
/// Administra las IAs disponibles. Recorre la lista <see cref="Proveedores"/>
/// y usa la primera que responda; si una está saturada (cuota agotada) o falla,
/// pasa automáticamente a la siguiente.
///
/// ---- CÓMO AGREGAR MÁS IAs ----
/// 1. Si es Gemini: agrega otra línea en <see cref="Proveedores"/> con otra API key
///    (crea claves gratis en https://aistudio.google.com/app/apikey).
/// 2. Si es otro servicio (Groq, OpenRouter, etc.): crea una clase que implemente
///    <see cref="IAIProvider"/> y añádela a la lista.
/// El orden de la lista es la prioridad de uso.
/// </summary>
public static class AIProviderManager
{
    /// <summary>IA que se usó en la última petición exitosa.</summary>
    public static string UltimoProveedor { get; private set; } = "(ninguna todavía)";

    public static List<IAIProvider> Proveedores()
    {
        var lista = new List<IAIProvider>();

        // Gemini con las claves que tengas configuradas.
        if (ApiKeys.GeminiConfigured)
        {
            lista.Add(new GeminiProvider(ApiKeys.GeminiKey, "gemini-3.8-flash"));
            // Ejemplo de segunda clave de respaldo (descomenta y pega otra clave):
            // lista.Add(new GeminiProvider("TU-OTRA-KEY", "gemini-3.8-flash"));
        }

        return lista;
    }

    public static bool HayAlgunProveedor => Proveedores().Any(p => p.Configurado);

    /// <summary>
    /// Genera texto probando cada proveedor en orden. Devuelve también el nombre
    /// de la IA usada para poder mostrarlo al usuario.
    /// </summary>
    public static async Task<(string Texto, string Proveedor)> GenerarAsync(string prompt, CancellationToken ct = default)
    {
        var proveedores = Proveedores().Where(p => p.Configurado).ToList();
        if (proveedores.Count == 0)
            throw new IAException("No hay ninguna IA configurada. Agrega una API key en ApiKeys.cs.");

        var errores = new List<string>();

        foreach (var proveedor in proveedores)
        {
            try
            {
                var texto = await proveedor.GenerarAsync(prompt, ct);
                UltimoProveedor = proveedor.Nombre;
                return (texto, proveedor.Nombre);
            }
            catch (IAException ex) when (ex.CuotaAgotada)
            {
                errores.Add($"{proveedor.Nombre}: saturada ({ex.Message})");
                // Saturada -> prueba la siguiente automáticamente.
            }
            catch (Exception ex)
            {
                errores.Add($"{proveedor.Nombre}: {ex.Message}");
            }
        }

        throw new IAException("Ninguna IA pudo responder:\n" + string.Join("\n", errores));
    }
}
