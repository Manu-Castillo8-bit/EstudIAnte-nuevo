using System.Text.Json.Nodes;
using EstudIAnte.Models;

namespace EstudIAnte.Services;

/// <summary>
/// Genera diagramas Mermaid con IA a partir del texto de un documento
/// analizado o de la conversación de un tema. Los guarda en la base
/// local (y en Supabase si hay conexión) para poder volver a verlos.
/// </summary>
public static class DiagramaGenerator
{
    /// <summary>
    /// Genera un diagrama. <paramref name="tipo"/>: "flujo" (flowchart) o "mapa" (mindmap).
    /// Devuelve el diagrama guardado y el nombre de la IA usada.
    /// </summary>
    public static async Task<(Diagrama Diagrama, string IA)> GenerarAsync(
        string usuarioId,
        string titulo,
        string fuente,
        string material,
        string? documentoId = null,
        string tipo = "flujo")
    {
        if (string.IsNullOrWhiteSpace(material))
            throw new IAException("No hay material para generar el diagrama.");

        var prompt = ConstruirPrompt(titulo, material, tipo);
        var (respuesta, proveedor) = await AIProviderManager.GenerarAsync(prompt);

        var codigo = ExtraerMermaid(respuesta);
        if (codigo is null)
            throw new IAException("La IA no devolvió un diagrama válido. Intenta de nuevo.");

        var diagrama = new Diagrama
        {
            UsuarioId = usuarioId,
            DocumentoId = documentoId,
            Titulo = titulo,
            Codigo = codigo,
            Tipo = tipo == "mapa" ? "mapa" : "flujo",
            Fuente = fuente
        };

        var conn = await LocalDatabase.GetAsync();
        await conn.InsertAsync(diagrama);

        var client = await SupabaseService.GetClientAsync();
        if (client is not null)
        {
            try { await client.From<Diagrama>().Insert(diagrama); }
            catch { /* se sincroniza después */ }
        }

        return (diagrama, proveedor);
    }

    static string ConstruirPrompt(string titulo, string material, string tipo)
    {
        var instruccion = tipo == "mapa"
            ? "un mapa mental (mindmap de Mermaid) que organice los conceptos principales y sus ramas"
            : "un diagrama de flujo (flowchart de Mermaid) con los pasos, procesos y decisiones clave";

        return
            "Eres un experto en visualización de información académica.\n" +
            $"Genera {instruccion} sobre \"{titulo}\".\n\n" +
            "REGLAS ESTRICTAS:\n" +
            "- Responde SOLO con el código Mermaid dentro de un bloque ```mermaid```, sin explicaciones.\n" +
            "- Si es flowchart, empieza con 'flowchart TD' y usa nodos con etiquetas cortas (máximo 5-8 palabras).\n" +
            "- Si es mindmap, empieza con 'mindmap' y usa indentación de 2 espacios por nivel.\n" +
            "- NO uses paréntesis, comillas ni caracteres especiales dentro de las etiquetas de los nodos.\n" +
            "- Máximo 15 nodos para que el diagrama sea legible.\n" +
            "- Todo el texto de los nodos en español.\n\n" +
            "Material de estudio:\n" +
            DocumentoAnalysisService.Recortar(material, 12000);
    }

    /// <summary>
    /// Extrae el código Mermaid de la respuesta de la IA.
    /// Acepta bloques ```mermaid ...``` o código suelto que empiece con flowchart/mindmap/graph.
    /// </summary>
    internal static string? ExtraerMermaid(string respuesta)
    {
        if (string.IsNullOrWhiteSpace(respuesta))
            return null;

        // Busca bloques cercados: ```mermaid ... ``` o ``` ... ```
        var marcador = "```";
        var indice = respuesta.IndexOf(marcador, StringComparison.Ordinal);
        while (indice >= 0)
        {
            var fin = respuesta.IndexOf(marcador, indice + 3, StringComparison.Ordinal);
            if (fin < 0)
                break;

            var bloque = respuesta[(indice + 3)..fin].Trim();
            // Quita la palabra "mermaid" inicial si el bloque la incluye
            if (bloque.StartsWith("mermaid", StringComparison.OrdinalIgnoreCase))
                bloque = bloque[7..].Trim();

            if (EsMermaidValido(bloque))
                return bloque;

            indice = respuesta.IndexOf(marcador, fin + 3, StringComparison.Ordinal);
        }

        // Sin bloque cercado: usa la respuesta completa si parece código Mermaid
        var candidato = respuesta.Trim();
        if (candidato.StartsWith("```mermaid", StringComparison.OrdinalIgnoreCase))
        {
            candidato = candidato[10..].Trim();
            var cierre = candidato.LastIndexOf("```", StringComparison.Ordinal);
            if (cierre >= 0)
                candidato = candidato[..cierre].Trim();
        }

        return EsMermaidValido(candidato) ? candidato : null;
    }

    static bool EsMermaidValido(string? codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            return false;

        return codigo.StartsWith("flowchart", StringComparison.OrdinalIgnoreCase)
            || codigo.StartsWith("graph", StringComparison.OrdinalIgnoreCase)
            || codigo.StartsWith("mindmap", StringComparison.OrdinalIgnoreCase)
            || codigo.StartsWith("sequenceDiagram", StringComparison.OrdinalIgnoreCase)
            || codigo.StartsWith("classDiagram", StringComparison.OrdinalIgnoreCase)
            || codigo.StartsWith("stateDiagram", StringComparison.OrdinalIgnoreCase)
            || codigo.StartsWith("erDiagram", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Prepara el código Mermaid para insertarlo dentro del HTML del WebView
    /// sin romper el documento (escapa backticks, barra invertida y cierre de script).
    /// </summary>
    internal static string EscaparParaHtml(string codigo) =>
        codigo
            .Replace("\\", "\\\\")
            .Replace("`", "\\`")
            .Replace("</", "<\\/")
            .Replace("$", "\\$");

    /// <summary>Serializa el diagrama como JSON seguro para pasarlo a JavaScript.</summary>
    internal static string AJson(string codigo)
    {
        var nodo = new JsonObject { ["codigo"] = codigo };
        return nodo.ToJsonString();
    }
}
