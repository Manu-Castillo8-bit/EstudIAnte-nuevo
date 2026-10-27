using EstudIAnte.Models;

namespace EstudIAnte.Services;

/// <summary>
/// Analiza un documento con IA: genera un resumen y guarda el texto extraído
/// en la tabla documentos para poder crear cuestionarios después.
/// </summary>
public static class DocumentoAnalysisService
{
    /// <summary>
    /// Marca el documento como analizado. Devuelve la IA usada.
    /// Lanza excepción con mensaje claro si no hay IA configurada o el archivo no tiene texto.
    /// </summary>
    public static async Task<(string Resumen, string IA)> AnalizarAsync(
        Documento doc, string tamano = "Medio", CancellationToken ct = default)
    {
        var texto = doc.Texto;

        if (string.IsNullOrWhiteSpace(texto))
            throw new IAException("No se pudo leer texto del documento. Si es un PDF escaneado (imagen), la extracción automática no es posible.");

        var (maxPalabras, instruccion) = tamano switch
        {
            "Corto" => (60, "Resume MUY brevemente en unas pocas frases (máximo 60 palabras)"),
            "Detallado" => (350, "Haz un resumen DETALLADO y completo (máximo 350 palabras), desarrollando los puntos importantes"),
            "Extenso" => (600, "Haz un resumen EXTENSO (máximo 600 palabras), explicando con detalle cada tema del material"),
            _ => (120, "Resume en un párrafo claro y conciso (máximo 120 palabras)"),
        };

        var prompt =
            $"Eres un tutor educativo. {instruccion} " +
            "el siguiente material de estudio:\n\n" + Recortar(texto, 6000 * (tamano == "Corto" ? 3 : 6));

        var (resumen, proveedor) = await AIProviderManager.GenerarAsync(prompt, ct);
        ct.ThrowIfCancellationRequested();

        doc.Resumen = resumen.Trim();
        doc.Estado = "listo";

        var conn = await LocalDatabase.GetAsync();
        await conn.UpdateAsync(doc);

        var client = await SupabaseService.GetClientAsync();
        if (client is not null)
        {
            try { await client.From<Documento>().Update(doc); }
            catch { /* se sincroniza después */ }
        }

        return (doc.Resumen, proveedor);
    }

    internal static string Recortar(string texto, int max) =>
        texto.Length <= max ? texto : texto[..max];
}
