using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using EstudIAnte.Models;

namespace EstudIAnte.Services;

/// <summary>
/// Convierte un documento a HTML fiel: respeta estructura (títulos, listas,
/// tablas, hojas de cálculo, diapositivas) e incrusta las imágenes en base64,
/// para que el archivo se muestre "tal como es" en el visor de la app.
/// </summary>
public static class DocumentoHtmlService
{
    static readonly XNamespace W =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    static readonly XNamespace A =
        "http://schemas.openxmlformats.org/drawingml/2006/main";
    static readonly XNamespace R =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    static readonly XNamespace Sx =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    static readonly XNamespace Px =
        "http://schemas.openxmlformats.org/presentationml/2006/main";

    static readonly string Estilo = """
        <style>
          body { background:#0B1030; color:#E2E8F0; font-family:'Segoe UI',sans-serif;
                 margin:0; padding:26px 30px; line-height:1.55; }
          h1,h2,h3,h4 { color:#22D3EE; margin:18px 0 8px; font-weight:700; }
          h2.sheet-title { color:#F472B6; border-bottom:2px solid #2A3A6A; padding-bottom:6px; }
          p { margin:8px 0; }
          table { border-collapse:collapse; margin:14px 0; }
          th, td { border:1px solid #2A3A6A; padding:6px 11px; font-size:14px; }
          th { background:#14204A; color:#7DD3FC; text-align:left; }
          tr:nth-child(even) td { background:#10173D; }
          td:first-child { color:#7DD3FC; }
          ul { margin:8px 0; padding-left:26px; }
          li { margin:4px 0; }
          img { max-width:100%; border-radius:10px; margin:12px 0; display:block;
                border:1px solid #2A3A6A; background:#fff; }
          pre { background:#10173D; border:1px solid #2A3A6A; border-radius:12px;
                padding:16px; white-space:pre-wrap; word-wrap:break-word; font-size:13px;
                color:#94E7C9; }
          code { font-family:Consolas,monospace; }
          .deco { color:#64748B; font-size:12px; margin-top:30px; border-top:1px solid #2A3A6A; padding-top:10px; }
          b, strong { color:#FFFFFF; }
        </style>
        """;

    /// <summary>Tipos que se pueden renderizar con estructura e imágenes.</summary>
    public static bool EsVisualizable(string tipo) =>
        tipo is "docx" or "xlsx" or "pptx" or "csv" or "md" or "txt" or "log" or "json" or "pdf";

    /// <summary>Devuelve el HTML completo del documento, o string.Empty si no se puede mostrar fielmente.</summary>
    public static string GenerarHtml(Documento doc, byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
            return string.Empty;

        try
        {
            var cuerpo = doc.Tipo switch
            {
                "docx" => HtmlDocx(bytes),
                "xlsx" => HtmlXlsx(bytes),
                "pptx" => HtmlPptx(bytes),
                "pdf"  => HtmlPdf(bytes),
                "csv"  => HtmlTablaDelimitada(Codificar(bytes), ',', ';'),
                "md"   => HtmlMarkdown(Codificar(bytes)),
                _      => $"<pre>{Escapar(Codificar(bytes))}</pre>", // txt, log, json
            };

            if (string.IsNullOrWhiteSpace(cuerpo))
                return string.Empty;

            return "<!DOCTYPE html><html><head><meta charset='utf-8'>" + Estilo +
                   "</head><body>" + cuerpo +
                   $"<p class='deco'>📄 {Escapar(doc.Nombre)} · visualizado con EstudIAnte</p>" +
                   "</body></html>";
        }
        catch
        {
            return string.Empty;
        }
    }

    // ---------------- DOCX ----------------

    static string HtmlDocx(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        var entry = zip.GetEntry("word/document.xml");
        if (entry is null)
            return string.Empty;

        XDocument doc;
        using (var s = entry.Open())
            doc = XDocument.Load(s);

        var imagenes = CargarImagenes(zip, "word/_rels/document.xml.rels", "word/");

        var body = doc.Root?.Element(W + "body");
        if (body is null)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var elemento in body.Elements())
        {
            if (elemento.Name == W + "p")
            {
                var html = HtmlParrafoDocx(elemento, imagenes);
                if (html.Length == 0)
                    continue;

                var estilo = elemento.Elements(W + "pPr").Elements(W + "pStyle")
                    .Select(e => e.Attribute(W + "val")?.Value).FirstOrDefault();

                var nivelLetra = estilo is not null && estilo.StartsWith("Heading",
                    StringComparison.OrdinalIgnoreCase) && estilo.Length > 0 ? estilo[^1] : '\0';

                if (char.IsDigit(nivelLetra))
                {
                    var nivel = Math.Max(1, Math.Min(4, nivelLetra - '0'));
                    sb.Append($"<h{nivel}>{html}</h{nivel}>");
                }
                else if (elemento.Elements(W + "pPr").Elements(W + "numPr").Any() ||
                         estilo == "ListParagraph")
                {
                    sb.Append($"<li>{html}</li>");
                }
                else
                {
                    sb.Append($"<p>{html}</p>");
                }
            }
            else if (elemento.Name == W + "tbl")
            {
                sb.Append(HtmlTablaWord(elemento));
            }
        }

        return EnvolverListas(sb.ToString());
    }

    static string HtmlParrafoDocx(XElement parrafo, Dictionary<string, string> imagenes)
    {
        var sb = new StringBuilder();
        foreach (var run in parrafo.Elements(W + "r"))
        {
            var props = run.Element(W + "rPr");
            var negrita = props?.Elements(W + "b").Any() == true;
            var italica = props?.Elements(W + "i").Any() == true;

            foreach (var br in run.Elements(W + "br"))
                sb.Append("<br>");

            foreach (var t in run.Elements(W + "t"))
            {
                if (t.Value.Length == 0)
                    continue;
                var x = Escapar(t.Value);
                if (negrita) x = $"<b>{x}</b>";
                if (italica) x = $"<i>{x}</i>";
                sb.Append(x);
            }

            foreach (var blip in run.Descendants(A + "blip"))
            {
                var rel = blip.Attribute(R + "embed")?.Value;
                if (rel is not null && imagenes.TryGetValue(rel, out var uri))
                    sb.Append($"<img src='{uri}' />");
            }
        }
        return sb.ToString();
    }

    static string HtmlTablaWord(XElement tabla)
    {
        var sb = new StringBuilder("<table>");
        foreach (var fila in tabla.Elements(W + "tr"))
        {
            var celdas = fila.Elements(W + "tc").ToList();
            var esCabecera = celdas.All(c =>
                c.Elements(W + "p").Elements(W + "r").Elements(W + "rPr").Elements(W + "b").Any());

            sb.Append(esCabecera ? "<tr>" : "<tr>");
            foreach (var celda in celdas)
            {
                var textos = celda.Elements(W + "p")
                    .Select(p => p.Value.Trim())
                    .Where(t => t.Length > 0);
                sb.Append(is_cabecera(esCabecera, textos));
            }
            sb.Append("</tr>");
        }
        return sb.Append("</table>").ToString();
    }

    static string is_cabecera(bool cab, IEnumerable<string> textos) =>
        $"<{(cab ? "th" : "td")}>{Escapar(string.Join("<br>", textos))}</{(cab ? "th" : "td")}>";

    /// <summary>Envuelve grupos de <li> consecutivos dentro de <ul>...</ul>.</summary>
    static string EnvolverListas(string html)
    {
        if (!html.Contains("<li>"))
            return html;

        var sb = new StringBuilder();
        var enLista = false;
        var posicion = 0;

        while (posicion < html.Length)
        {
            var apertura = html.IndexOf("<li>", posicion, StringComparison.Ordinal);
            var cierre = html.IndexOf("</li>", Math.Max(apertura, posicion), StringComparison.Ordinal);

            if (apertura == -1 || cierre == -1)
                break;

            if (!enLista && apertura > posicion)
                sb.Append(html[posicion..apertura]);

            if (!enLista)
                sb.Append("<ul>");
            enLista = true;

            sb.Append("<li>").Append(html[(apertura + 4)..cierre]).Append("</li>");
            posicion = cierre + 5;
        }

        if (enLista)
            sb.Append("</ul>");
        sb.Append(html[Math.Min(posicion, html.Length)..]);

        return sb.ToString();
    }

    // ---------------- RELACIONES E IMÁGENES ----------------

    /// <summary>Carga las imágenes del paquete y las devuelve como data-URIs base64 (relId → uri).</summary>
    static Dictionary<string, string> CargarImagenes(ZipArchive zip, string rutaRels, string carpetaBase)
    {
        var resultado = new Dictionary<string, string>();
        var entryRels = zip.GetEntry(rutaRels);
        if (entryRels is null)
            return resultado;

        XDocument rels;
        using (var s = entryRels.Open())
            rels = XDocument.Load(s);

        foreach (var rel in rels.Root!.Elements())
        {
            var id = rel.Attribute("Id")?.Value;
            var destino = rel.Attribute("Target")?.Value;
            if (id is null || destino is null)
                continue;

            var extension = Path.GetExtension(destino).TrimStart('.').ToLowerInvariant();
            var mime = extension switch
            {
                "png" => "image/png",
                "jpg" or "jpeg" => "image/jpeg",
                "gif" => "image/gif",
                "bmp" => "image/bmp",
                "webp" => "image/webp",
                _ => string.Empty,
            };
            if (mime.Length == 0)
                continue; // svg u otros no soportados

            var ruta = destino.StartsWith("/word/") ? destino.TrimStart('/') : carpetaBase + destino;
            var entry = zip.GetEntry(ruta.Replace("\\", "/"));
            if (entry is null)
                continue;

            using var ms = new MemoryStream();
            using (var s = entry.Open())
                s.CopyTo(ms);

            resultado[id] = $"data:{mime};base64,{Convert.ToBase64String(ms.ToArray())}";
        }
        return resultado;
    }

    // ---------------- XLSX ----------------

    static string HtmlXlsx(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        var compartidas = new List<string>();
        var entryCompartidas = zip.GetEntry("xl/sharedStrings.xml");
        if (entryCompartidas is not null)
        {
            using var s = entryCompartidas.Open();
            var doc = XDocument.Load(s);
            compartidas = doc.Descendants(Sx + "si")
                .Select(si => string.Concat(si.Descendants(Sx + "t").Select(t => t.Value)))
                .ToList();
        }

        var nombresHojas = new List<string>();
        var entryWorkbook = zip.GetEntry("xl/workbook.xml");
        if (entryWorkbook is not null)
        {
            using var s = entryWorkbook.Open();
            var wb = XDocument.Load(s);
            nombresHojas = wb.Descendants(Sx + "sheet")
                .Select(h => h.Attribute("name")?.Value ?? "Hoja").ToList();
        }

        var entradas = zip.Entries
            .Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase).ToList();

        var sb = new StringBuilder();
        for (var i = 0; i < entradas.Count; i++)
        {
            var titulo = i < nombresHojas.Count ? nombresHojas[i] : $"Hoja {i + 1}";
            sb.Append($"<h2 class='sheet-title'>{Escapar(titulo)}</h2><table>");

            using var s = entradas[i].Open();
            var hoja = XDocument.Load(s);

            var primera = true;
            foreach (var fila in hoja.Descendants(Sx + "row"))
            {
                var etiqueta = primera ? "<thead><tr>" : "<tr>";
                sb.Append(etiqueta);
                foreach (var celda in fila.Elements(Sx + "c"))
                {
                    var valor = celda.Element(Sx + "is") is { } inline
                        ? string.Concat(inline.Descendants(Sx + "t").Select(t => t.Value))
                        : celda.Element(Sx + "v")?.Value ?? string.Empty;

                    if (celda.Attribute("t")?.Value == "s" &&
                        int.TryParse(valor, out var indice) && indice >= 0 && indice < compartidas.Count)
                        valor = compartidas[indice];

                    if (primera && valor.Length > 0)
                        sb.Append($"<th>{Escapar(valor)}</th>");
                    else
                        sb.Append($"<td>{Escapar(valor)}</td>");
                }
                sb.Append("</tr>");
                if (primera && hoja.Descendants(Sx + "row").Any())
                    sb.Append("</thead><tbody>");
                primera = false;
            }

            sb.Append("</table>");
        }
        return sb.ToString();
    }

    // ---------------- PPTX ----------------

    static string HtmlPptx(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        var diapositivas = zip.Entries
            .Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase) &&
                        e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .Select((entry, indice) => (entry, indice: indice + 1))
            .ToList();

        var sb = new StringBuilder();
        foreach (var (entry, numero) in diapositivas)
        {
            XDocument doc;
            using (var s = entry.Open())
                doc = XDocument.Load(s);

            // Imágenes de esta diapositiva (rels propio)
            var rels = $"ppt/slides/_rels/{Path.GetFileName(entry.FullName)}.rels";
            var imagenes = CargarImagenes(zip, rels, "ppt/");

            sb.Append($"<h2 class='sheet-title'>Diapositiva {numero}</h2>");

            // Texto: párrafos de cada caja de texto
            foreach (var parrafo in doc.Descendants(A + "p"))
            {
                var texto = string.Concat(parrafo.Descendants(A + "t").Select(t => t.Value)).Trim();
                if (texto.Length > 0)
                    sb.Append($"<p>{Escapar(texto)}</p>");
            }

            // Imágenes en el orden del XML de la diapositiva
            foreach (var blip in doc.Descendants(A + "blip"))
            {
                var rel = blip.Attribute(R + "embed")?.Value;
                if (rel is not null && imagenes.TryGetValue(rel, out var uri))
                    sb.Append($"<img src='{uri}' />");
            }
        }
        return sb.ToString();
    }

    // ---------------- PDF ----------------

    /// <summary>
    /// Los PDFs se muestran con el reproductor propio de WebView2: mantiene fotos,
    /// diseño y paginación. Si el navegador no puede, el visor usa el texto extraído.
    /// </summary>
    static string HtmlPdf(byte[] bytes)
    {
        var data = "data:application/pdf;base64," + Convert.ToBase64String(bytes);
        return $"<object data='{data}' type='application/pdf' width='100%' height='100%' style='min-height:1100px'>" +
               "<p>Tu visor no pudo mostrar el PDF aquí; usa la sección Mis Archivos para descargarlo.</p>" +
               "</object>";
    }

    // ---------------- CSV ----------------

    static string HtmlTablaDelimitada(string texto, params char[] posibles)
    {
        var lineas = texto.Split('\n');
        if (lineas.Length == 0)
            return string.Empty;

        // Detecta el separador: gana el que más tokens genere.
        var separador = posibles.OrderByDescending(
            s => lineas.Take(20).Count(l => l.Split(s).Length > 1)).First();

        var filas = lineas.Select(l => l.TrimEnd('\r')).Where(x => x.Length > 0).ToList();

        var sb = new StringBuilder("<table>");
        for (var i = 0; i < filas.Count; i++)
        {
            var etiqueta = i == 0 ? "th" : "td";
            sb.Append("<tr>");
            foreach (var campo in filas[i].Split(separador))
                sb.Append($"<{etiqueta}>{Escapar(campo.Trim())}</{etiqueta}>");
            sb.Append("</tr>");
        }
        return sb.Append("</table>").ToString();
    }

    // ---------------- MARKDOWN (naive) ----------------

    static string HtmlMarkdown(string md)
    {
        var sb = new StringBuilder();
        bool enCodigo = false;

        foreach (var lineaCruda in md.Split('\n'))
        {
            var linea = lineaCruda.TrimEnd('\r');

            if (linea.StartsWith("```"))
            {
                sb.Append(enCodigo ? "</code></pre>" : "<pre><code>");
                enCodigo = !enCodigo;
                continue;
            }

            if (enCodigo)
            {
                sb.Append(Escapar(linea)).Append('\n');
                continue;
            }

            if (linea.StartsWith("# "))
                sb.Append($"<h1>{Enriqueser(Escapar(linea[2..]))}</h1>");
            else if (linea.StartsWith("## "))
                sb.Append($"<h2>{Enriqueser(Escapar(linea[3..]))}</h2>");
            else if (linea.StartsWith("### ") || linea.StartsWith("#### "))
                sb.Append($"<h3>{Enriqueser(Escapar(linea[4..]))}</h3>");
            else if (linea.StartsWith("- ") || linea.StartsWith("* "))
                sb.Append($"<li>{Enriqueser(Escapar(linea[2..]))}</li>");
            else if (linea.Length == 0)
                sb.Append("<br>");
            else
                sb.Append($"<p>{Enriqueser(Escapar(linea))}</p>");
        }

        if (enCodigo)
            sb.Append("</code></pre>");

        return EnvolverListas(sb.ToString());
    }

    /// <summary>Convierte pares **…** en negritas.</summary>
    static string Enriqueser(string texto)
    {
        var partes = texto.Split("**");
        var sb = new StringBuilder();
        for (var i = 0; i < partes.Length; i++)
            sb.Append(i % 2 == 1 ? $"<b>{partes[i]}</b>" : partes[i]);
        return sb.ToString();
    }

    // ---------------- UTILIDADES ----------------

    static string Codificar(byte[] bytes)
    {
        try
        {
            var estricto = Encoding.GetEncoding("utf-8",
                new EncoderExceptionFallback(), new DecoderExceptionFallback());
            return estricto.GetString(bytes);
        }
        catch
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    static string Escapar(string texto) =>
        texto.Replace("&", "&amp;")
             .Replace("<", "&lt;")
             .Replace(">", "&gt;")
             .Replace("\"", "&quot;");
}
