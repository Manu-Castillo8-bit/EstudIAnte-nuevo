using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace EstudIAnte.Services;

/// <summary>
/// Proveedores de IA vía la Edge Function "gemini" de Supabase.
///
/// ¿Por qué? Para que la IA funcione en cualquier clon sin pegar una clave:
/// las API keys de Gemini y Groq viven como SECRETOS en el servidor de Supabase,
/// y la app solo usa la AnonKey pública del proyecto (que no es secreta).
///
/// La app decide qué IA usar en cada petición. Si el servidor reporta que esa
/// IA está saturada (respuesta con "saturada": true), el gestor
/// <see cref="AIProviderManager"/> pregunta al usuario si quiere cambiar a la
/// siguiente IA. El servidor nunca cambia de proveedor por su cuenta.
/// </summary>
public abstract class SupabaseIAProvider : IAIProvider
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    /// <summary>Proveedor que esta instancia pide al servidor: "gemini" o "groq".</summary>
    readonly string _proveedor;

    protected SupabaseIAProvider(string proveedor) => _proveedor = proveedor;

    public string Nombre => _proveedor switch
    {
        "groq" => "Groq (IA alternativa, servidor)",
        "openrouter" => "OpenRouter (IA alternativa 2, servidor)",
        _ => "Google Gemini (servidor)"
    };

    public bool Configurado => SupabaseConfig.IsConfigured;

    public async Task<string> GenerarAsync(
        string prompt, CancellationToken ct = default, bool buscarEnWeb = false)
    {
        var url = $"{SupabaseConfig.Url}/functions/v1/gemini";

        using var peticion = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new { prompt, proveedor = _proveedor, buscarEnWeb })
        };
        peticion.Headers.Add("apikey", SupabaseConfig.AnonKey);
        peticion.Headers.Add("Authorization", $"Bearer {SupabaseConfig.AnonKey}");

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await Http.SendAsync(peticion, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new IAException("No se pudo conectar con el servicio de IA: " + ex.Message);
        }

        var json = await respuesta.Content.ReadAsStringAsync(ct);
        var (error, saturada) = LeerError(json);

        if (!respuesta.IsSuccessStatusCode)
        {
            var cuota = saturada
                        || respuesta.StatusCode == HttpStatusCode.TooManyRequests
                        || respuesta.StatusCode == HttpStatusCode.Forbidden
                        || json.Contains("quota", StringComparison.OrdinalIgnoreCase)
                        || json.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase);

            throw new IAException(
                error ?? $"El servicio de IA respondió {(int)respuesta.StatusCode}. {Recortar(json)}",
                cuota);
        }

        var texto = LeerTexto(json);
        if (string.IsNullOrWhiteSpace(texto))
            throw new IAException("El servicio de IA devolvió una respuesta vacía.");

        return texto;
    }

    static string? LeerTexto(string json)
    {
        try
        {
            return JsonNode.Parse(json)?["texto"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Extrae {error, saturada} del cuerpo de la respuesta del servidor.</summary>
    static (string? Error, bool Saturada) LeerError(string json)
    {
        try
        {
            var nodo = JsonNode.Parse(json);
            var error = nodo?["error"]?.GetValue<string>();
            var saturada = nodo?["saturada"]?.GetValue<bool>() ?? false;
            return (string.IsNullOrEmpty(error) ? null : error, saturada);
        }
        catch
        {
            return (null, false);
        }
    }

    static string Recortar(string texto) => texto.Length <= 200 ? texto : texto[..200];
}

/// <summary>Google Gemini vía Supabase: la primera opción, siempre se intenta primero.</summary>
public class SupabaseGeminiProvider : SupabaseIAProvider
{
    public SupabaseGeminiProvider() : base("gemini") { }
}
