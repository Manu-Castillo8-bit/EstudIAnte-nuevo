using System.Text.Json;
using System.Text.Json.Nodes;
using EstudIAnte.Models;

namespace EstudIAnte.Services;

/// <summary>
/// Genera cuestionarios de opción múltiple con IA a partir de un tema de
/// conversación o del texto de un documento analizado.
/// </summary>
public static class QuizGenerator
{
    /// <summary>
    /// Genera un cuestionario con las preguntas ya guardadas en la base local.
    /// Devuelve la IA usada.
    /// </summary>
    public static async Task<(Cuestionario Quiz, string IA)> GenerarAsync(
        string usuarioId,
        string titulo,
        string fuente,
        string material,
        string? documentoId = null,
        int numeroPreguntas = 5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(material))
            throw new IAException("No hay material para generar el cuestionario.");

        var prompt = ConstruirPrompt(titulo, material, numeroPreguntas);

        var (respuesta, proveedor) = await AIProviderManager.GenerarAsync(prompt, ct);
        ct.ThrowIfCancellationRequested();
        var preguntas = Parsear(respuesta, numeroPreguntas);

        if (preguntas.Count == 0)
            throw new IAException("La IA no devolvió preguntas válidas. Intenta de nuevo.");

        var quiz = new Cuestionario
        {
            UsuarioId = usuarioId,
            DocumentoId = documentoId,
            Titulo = $"Cuestionario: {titulo}",
            Nivel = "Medio",
            TotalPreguntas = preguntas.Count,
            TiempoLimite = Math.Max(5, preguntas.Count * 2)
        };

        var conn = await LocalDatabase.GetAsync();
        await conn.InsertAsync(quiz);

        foreach (var p in preguntas)
        {
            p.CuestionarioId = quiz.Id;
            p.Fuente = fuente;
        }
        await conn.InsertAllAsync(preguntas);

        var client = await SupabaseService.GetClientAsync();
        if (client is not null)
        {
            try
            {
                await client.From<Cuestionario>().Insert(quiz);
                foreach (var p in preguntas)
                    await client.From<Pregunta>().Insert(p);
            }
            catch { /* se sincroniza después */ }
        }

        return (quiz, proveedor);
    }

    static string ConstruirPrompt(string titulo, string material, int numeroPreguntas)
    {
        var formato = "[{\"enunciado\":\"...\",\"a\":\"...\",\"b\":\"...\",\"c\":\"...\",\"d\":\"...\",\"correcta\":\"A\",\"tip\":\"...\"}]";
        return
            $"Eres un profesor que crea exámenes de opción múltiple.\n" +
            $"Genera EXACTAMENTE {numeroPreguntas} preguntas en español sobre \"{titulo}\".\n" +
            "Cada pregunta debe tener 4 opciones (A, B, C, D), una sola correcta, y un \"tip\" breve que explique la respuesta.\n\n" +
            "Responde SOLO con un arreglo JSON válido, sin texto adicional, con este formato exacto:\n" +
            formato + "\n\n" +
            "Material de estudio:\n" +
            DocumentoAnalysisService.Recortar(material, 12000);
    }

    static List<Pregunta> Parsear(string respuesta, int max)
    {
        var lista = new List<Pregunta>();

        var json = ExtraerArreglo(respuesta);
        if (json is null)
            return lista;

        try
        {
            var nodo = JsonNode.Parse(json)?.AsArray();
            if (nodo is null)
                return lista;

            foreach (var item in nodo)
            {
                if (lista.Count >= max)
                    break;

                var enunciado = item?["enunciado"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(enunciado))
                    continue;

                var correcta = (item?["correcta"]?.GetValue<string>() ?? "A").Trim().ToUpperInvariant();

                lista.Add(new Pregunta
                {
                    Enunciado = enunciado!,
                    OpcionA = item?["a"]?.GetValue<string>() ?? "-",
                    OpcionB = item?["b"]?.GetValue<string>() ?? "-",
                    OpcionC = item?["c"]?.GetValue<string>() ?? "-",
                    OpcionD = item?["d"]?.GetValue<string>() ?? "-",
                    RespuestaCorrecta = correcta.Length == 1 && "ABCD".Contains(correcta) ? correcta : "A",
                    Tip = item?["tip"]?.GetValue<string>() ?? string.Empty
                });
            }
        }
        catch
        {
            // respuesta mal formada
        }

        return lista;
    }

    static string? ExtraerArreglo(string texto)
    {
        var inicio = texto.IndexOf('[');
        var fin = texto.LastIndexOf(']');
        return inicio >= 0 && fin > inicio ? texto.Substring(inicio, fin - inicio + 1) : null;
    }
}
