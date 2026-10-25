using System.Text.Json.Nodes;
using EstudIAnte.Models;

namespace EstudIAnte.Services;

/// <summary>
/// Genera diagramas con IA a partir del texto de un documento analizado o de la
/// conversación de un tema. Soporta varios tipos de Mermaid y el cuadro comparativo
/// (tabla HTML). Los guarda en la base local (y en Supabase si hay conexión).
/// </summary>
public static class DiagramaGenerator
{
    /// <summary>Instrucción de prompt por tipo de diagrama.</summary>
    public static readonly Dictionary<string, string> Instrucciones = new()
    {
        ["flujo"] = "un diagrama de flujo (flowchart de Mermaid) con los pasos, procesos y decisiones clave. " +
                    "Debe empezar con 'flowchart TD' y usar nodos con etiquetas cortas (máximo 5-8 palabras)",
        ["mapa"] = "un mapa mental (mindmap de Mermaid) que organice los conceptos principales y sus ramas. " +
                   "Debe empezar con 'mindmap' y usar indentación de 2 espacios por nivel",
        ["comparativo"] = "un cuadro comparativo en formato de tabla HTML. " +
                          "Responde SOLO con código HTML: una etiqueta <table> con <thead> (columnas: Criterio y los elementos comparados) " +
                          "y <tbody> con de 3 a 8 filas de criterios comparados. Sin <html>, <head> ni <body>, solo la tabla",
        ["timeline"] = "una línea de tiempo (timeline de Mermaid) que ordene eventos, etapas o períodos. " +
                       "Debe empezar con 'timeline' y usar secciones por período con 2-4 eventos cada una",
        ["secuencia"] = "un diagrama de secuencia (sequenceDiagram de Mermaid) con los actores/participantes y sus " +
                        "interacciones paso a paso. Debe empezar con 'sequenceDiagram' y usar ->> para las flechas",
        ["clases"] = "un diagrama de clases (classDiagram de Mermaid) con los conceptos como clases, sus atributos y " +
                     "relaciones. Debe empezar con 'classDiagram'",
        ["er"] = "un diagrama entidad-relación (erDiagram de Mermaid) con las entidades, sus atributos y las " +
                 "relaciones. Debe empezar con 'erDiagram'",
        ["organigrama"] = "un organigrama jerárquico (flowchart de Mermaid) con los niveles de arriba hacia abajo. " +
                          "Debe empezar con 'flowchart TD' y representar la jerarquía con conexiones padre-hijo",
        ["gantt"] = "un cronograma (gantt de Mermaid) con tareas y duraciones. Debe empezar con 'gantt', incluir " +
                    "'dateFormat YYYY-MM-DD' y definir secciones con tareas y duraciones (ej. :tarea1, 2026-01-01, 7d)",
        ["pastel"] = "una gráfica de pastel (pie de Mermaid) con las proporciones más relevantes del material. " +
                     "Debe empezar con 'pie' y tener la línea 'title' seguida de categorías con valores",
    };

    /// <summary>
    /// Genera un diagrama del tipo indicado. Devuelve el diagrama guardado y la IA usada.
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

        if (!Instrucciones.ContainsKey(tipo))
            tipo = "flujo";

        var prompt = ConstruirPrompt(titulo, material, tipo);
        var (respuesta, proveedor) = await AIProviderManager.GenerarAsync(prompt);

        var codigo = tipo == "comparativo"
            ? ExtraerTabla(respuesta)
            : ExtraerMermaid(respuesta);

        if (codigo is null)
            throw new IAException("La IA no devolvió un diagrama válido. Intenta de nuevo.");

        var diagrama = new Diagrama
        {
            UsuarioId = usuarioId,
            DocumentoId = documentoId,
            Titulo = titulo,
            Codigo = codigo,
            Tipo = tipo,
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
        var instruccion = Instrucciones[tipo];
        var regla = tipo == "comparativo"
            ? "- Responde SOLO con la tabla HTML, sin explicaciones ni texto adicional.\n"
            : "- Responde SOLO con el código Mermaid dentro de un bloque ```mermaid```, sin explicaciones.\n" +
              "- NO uses paréntesis, comillas ni caracteres especiales dentro de las etiquetas de los nodos.\n";

        return
            "Eres un experto en visualización de información académica.\n" +
            $"Genera {instruccion}, a partir del material, sobre \"{titulo}\".\n\n" +
            "REGLAS ESTRICTAS:\n" +
            regla +
            "- Todo el texto en español.\n" +
            "- Máximo 15 nodos/filas para que sea legible.\n\n" +
            "Material de estudio:\n" +
            DocumentoAnalysisService.Recortar(material, 12000);
    }

    /// <summary>
    /// Extrae el código Mermaid de la respuesta de la IA.
    /// Acepta bloques ```mermaid ... ``` o código suelto que empiece con un tipo soportado.
    /// </summary>
    internal static string? ExtraerMermaid(string respuesta)
    {
        if (string.IsNullOrWhiteSpace(respuesta))
            return null;

        var marcador = "```";
        var indice = respuesta.IndexOf(marcador, StringComparison.Ordinal);
        while (indice >= 0)
        {
            var fin = respuesta.IndexOf(marcador, indice + 3, StringComparison.Ordinal);
            if (fin < 0)
                break;

            var bloque = respuesta[(indice + 3)..fin].Trim();
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

    /// <summary>Extrae una tabla HTML (<table>...</table>) para el cuadro comparativo.</summary>
    internal static string? ExtraerTabla(string respuesta)
    {
        if (string.IsNullOrWhiteSpace(respuesta))
            return null;

        var inicio = respuesta.IndexOf("<table", StringComparison.OrdinalIgnoreCase);
        var fin = respuesta.LastIndexOf("</table>", StringComparison.OrdinalIgnoreCase);

        if (inicio >= 0 && fin > inicio)
            return respuesta[inicio..(fin + 8)].Trim();

        // Algunas IAs responden en markdown: convierte filas | a | en una tabla básica.
        var lineas = respuesta.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith('|') && l.EndsWith('|'))
            .Where(l => !l.Replace("|", "").Replace("-", "").Replace(":", "").Trim().Equals(""))
            .ToList();

        if (lineas.Count < 2)
            return null;

        var sb = new System.Text.StringBuilder("<table><thead><tr>");
        var encabezado = lineas[0].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var celda in encabezado)
            sb.Append("<th>").Append(celda).Append("</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var linea in lineas.Skip(1))
        {
            sb.Append("<tr>");
            foreach (var celda in linea.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                sb.Append("<td>").Append(celda).Append("</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
        return sb.ToString();
    }

    static bool EsMermaidValido(string? codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            return false;

        string[] inicios =
        {
            "flowchart", "graph", "mindmap", "sequenceDiagram", "classDiagram",
            "stateDiagram", "erDiagram", "gantt", "pie", "timeline", "journey",
            "quadrantChart", "gitGraph", "block-beta", "C4"
        };

        return inicios.Any(i => codigo.StartsWith(i, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Serializa el diagrama como JSON seguro para pasarlo a JavaScript.</summary>
    internal static string AJson(string codigo, string tipo)
    {
        var nodo = new JsonObject { ["codigo"] = codigo, ["tipo"] = tipo };
        return nodo.ToJsonString();
    }
}
