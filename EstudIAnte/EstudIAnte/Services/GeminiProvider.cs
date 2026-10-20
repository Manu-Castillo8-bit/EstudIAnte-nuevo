using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EstudIAnte.Services;

/// <summary>
/// Proveedor de Google Gemini (API gratuita de Google AI Studio).
/// https://aistudio.google.com/app/apikey
/// </summary>
public class GeminiProvider : IAIProvider
{
    const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public string Modelo { get; }
    public string ApiKey { get; }

    public GeminiProvider(string apiKey, string modelo)
    {
        ApiKey = apiKey;
        Modelo = modelo;
    }

    public string Nombre => $"Google Gemini ({Modelo})";

    public bool Configurado => !string.IsNullOrWhiteSpace(ApiKey) && !ApiKey.StartsWith("PEGA-AQUI");

    public async Task<string> GenerarAsync(string prompt, CancellationToken ct = default)
    {
        var cuerpo = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            }
        };

        // El servidor de Gemini a veces responde 503 (saturado). Reintentamos.
        HttpResponseMessage? respuesta = null;
        string detalle = string.Empty;
        for (var intento = 1; intento <= 3; intento++)
        {
            var url = $"{BaseUrl}/{Modelo}:generateContent?key={ApiKey}";
            respuesta = await Http.PostAsJsonAsync(url, cuerpo, ct);

            if (respuesta.IsSuccessStatusCode)
                break;

            detalle = await respuesta.Content.ReadAsStringAsync(ct);

            var saturado = respuesta.StatusCode == HttpStatusCode.ServiceUnavailable
                           || respuesta.StatusCode == HttpStatusCode.InternalServerError
                           || respuesta.StatusCode == (HttpStatusCode)529;

            if (!saturado || intento == 3)
            {
                var cuota = respuesta.StatusCode == HttpStatusCode.TooManyRequests
                            || respuesta.StatusCode == HttpStatusCode.Forbidden
                            || detalle.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase)
                            || detalle.Contains("quota", StringComparison.OrdinalIgnoreCase);
                throw new IAException($"Gemini respondió {(int)respuesta.StatusCode}. {PrimerasLineas(detalle)}", cuota || saturado);
            }

            await Task.Delay(TimeSpan.FromSeconds(intento), ct);
        }

        if (respuesta is null || !respuesta.IsSuccessStatusCode)
            throw new IAException("Gemini no respondió tras varios intentos. " + PrimerasLineas(detalle), true);

        var json = await respuesta.Content.ReadAsStringAsync(ct);
        var texto = ExtraerTexto(json);

        if (string.IsNullOrWhiteSpace(texto))
            throw new IAException("Gemini devolvió una respuesta vacía.");

        return texto;
    }

    static string ExtraerTexto(string json)
    {
        try
        {
            var nodo = JsonNode.Parse(json);
            var partes = nodo?["candidates"]?[0]?["content"]?["parts"]?.AsArray();
            if (partes is null)
                return string.Empty;

            var sb = new System.Text.StringBuilder();
            foreach (var parte in partes)
            {
                var t = parte?["text"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(t))
                    sb.Append(t);
            }
            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    static string PrimerasLineas(string texto) =>
        texto.Length <= 200 ? texto : texto[..200];
}
