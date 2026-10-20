using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace EstudIAnte.Services;

/// <summary>
/// Proveedor de IA que llama a la Edge Function "gemini" de Supabase.
///
/// ¿Por qué? Para que la IA funcione en cualquier clon sin pegar una clave:
/// la API key de Gemini vive como SECRETO en el servidor de Supabase, y la app
/// solo usa la AnonKey pública del proyecto (que no es secreta y GitHub no bloquea).
///
/// Si el servidor no está disponible, el gestor pasa al siguiente proveedor
/// (por ejemplo, un GeminiProvider con clave local como respaldo).
/// </summary>
public class SupabaseGeminiProvider : IAIProvider
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    public string Nombre => "EstudIAnte IA (Supabase)";

    public bool Configurado => SupabaseConfig.IsConfigured;

    public async Task<string> GenerarAsync(string prompt, CancellationToken ct = default)
    {
        var url = $"{SupabaseConfig.Url}/functions/v1/gemini";

        using var peticion = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new { prompt })
        };
        peticion.Headers.Add("apikey", SupabaseConfig.AnonKey);
        peticion.Headers.Add("Authorization", $"Bearer {SupabaseConfig.AnonKey}");

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await Http.SendAsync(peticion, ct);
        }
        catch (Exception ex)
        {
            throw new IAException("No se pudo conectar con el servicio de IA: " + ex.Message);
        }

        var json = await respuesta.Content.ReadAsStringAsync(ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            var cuota = respuesta.StatusCode == HttpStatusCode.TooManyRequests
                        || respuesta.StatusCode == HttpStatusCode.Forbidden
                        || json.Contains("quota", StringComparison.OrdinalIgnoreCase)
                        || json.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase);
            throw new IAException($"El servicio de IA respondió {(int)respuesta.StatusCode}. {Recortar(json)}", cuota);
        }

        var texto = ExtraerTexto(json);
        if (string.IsNullOrWhiteSpace(texto))
            throw new IAException("El servicio de IA devolvió una respuesta vacía.");

        return texto;
    }

    static string ExtraerTexto(string json)
    {
        try
        {
            var nodo = JsonNode.Parse(json);
            var texto = nodo?["texto"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(texto))
                return texto;

            var error = nodo?["error"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(error))
                throw new IAException(error);

            return string.Empty;
        }
        catch (IAException)
        {
            throw;
        }
        catch
        {
            return string.Empty;
        }
    }

    static string Recortar(string texto) =>
        texto.Length <= 200 ? texto : texto[..200];
}
