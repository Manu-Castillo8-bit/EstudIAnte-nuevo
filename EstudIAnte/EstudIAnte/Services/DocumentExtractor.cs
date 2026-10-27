using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace EstudIAnte.Services;

/// <summary>
/// Extrae el texto de un documento (TXT, PDF o DOCX) para poder analizarlo con IA.
/// </summary>
public static class DocumentExtractor
{
    public static async Task<string> ExtraerAsync(byte[] bytes, string tipo)
    {
        return tipo switch
        {
            "txt" or "md" or "log" or "json" or "xml" => DecodificarTexto(bytes),
            "csv" => DecodificarTexto(bytes),
            "pdf" => ExtraerPdf(bytes),
            "docx" => ExtraerDocx(bytes),
            "xlsx" => ExtraerXlsx(bytes),
            "pptx" => ExtraerPptx(bytes),
            _ => string.Empty
        };
    }

    /// <summary>Decodifica texto plano probando UTF-8 y luego Latin-1 (Windows-1252).</summary>
    static string DecodificarTexto(byte[] bytes)
    {
        try
        {
            var estricto = Encoding.GetEncoding("utf-8", new EncoderExceptionFallback(), new DecoderExceptionFallback());
            return estricto.GetString(bytes);
        }
        catch
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    static string ExtraerPdf(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            using var documento = UglyToad.PdfPig.PdfDocument.Open(ms);
            var sb = new StringBuilder();
            foreach (var pagina in documento.GetPages())
                sb.AppendLine(pagina.Text);
            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    static string ExtraerDocx(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            var entrada = zip.GetEntry("word/document.xml");
            if (entrada is null)
                return string.Empty;

            using var stream = entrada.Open();
            var doc = XDocument.Load(stream);
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

            var sb = new StringBuilder();
            foreach (var parrafo in doc.Descendants(w + "p"))
            {
                var texto = string.Concat(parrafo.Descendants(w + "t").Select(t => t.Value));
                if (!string.IsNullOrWhiteSpace(texto))
                    sb.AppendLine(texto);
            }
            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>Extrae las celdas de un .xlsx leyendo el XML dentro del paquete OPC.</summary>
    static string ExtraerXlsx(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

            // 1) Cadenas compartidas (cuando la celda referencia por índice)
            List<string>? compartidas = null;
            var entryCompartidas = zip.GetEntry("xl/sharedStrings.xml");
            if (entryCompartidas is not null)
            {
                using var s = entryCompartidas.Open();
                var doc = XDocument.Load(s);
                XNamespace sx = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                compartidas = doc.Descendants(sx + "si")
                    .Select(si => string.Concat(si.Descendants(sx + "t").Select(t => t.Value)))
                    .ToList();
            }

            // 2) Nombres de hoja en orden (xl/workbook.xml)
            var nombresHojas = new List<string>();
            var entryWorkbook = zip.GetEntry("xl/workbook.xml");
            if (entryWorkbook is not null)
            {
                using var s = entryWorkbook.Open();
                var wb = XDocument.Load(s);
                var sx = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                nombresHojas = wb.Descendants(sx + "sheet").Select(h => h.Attribute("name")?.Value ?? "Hoja").ToList();
            }

            var entradasHojas = zip.Entries
                .Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sb = new StringBuilder();
            for (var i = 0; i < entradasHojas.Count; i++)
            {
                var titulo = i < nombresHojas.Count ? nombresHojas[i] : $"Hoja {i + 1}";
                sb.AppendLine($"=== {titulo} ===");

                using var s = entradasHojas[i].Open();
                var hoja = XDocument.Load(s);
                XNamespace nsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

                foreach (var fila in hoja.Descendants(nsMain + "row"))
                {
                    var celdas = new List<string>();
                    foreach (var celda in fila.Elements(nsMain + "c"))
                    {
                        var valor = celda.Element(nsMain + "v")?.Value ?? string.Empty;

                        if (celda.Attribute("t")?.Value == "s" &&
                            compartidas is not null &&
                            int.TryParse(valor, out var indice) &&
                            indice >= 0 && indice < compartidas.Count)
                            valor = compartidas[indice];

                        if (!string.IsNullOrWhiteSpace(valor))
                            celdas.Add(valor);
                    }

                    if (celdas.Count > 0)
                        sb.AppendLine(string.Join(" | ", celdas));
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>Extrae el texto de cada diapositiva de un .pptx.</summary>
    static string ExtraerPptx(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";

            var diapositivas = zip.Entries
                .Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase) &&
                            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sb = new StringBuilder();
            for (var i = 0; i < diapositivas.Count; i++)
            {
                using var s = diapositivas[i].Open();
                var doc = XDocument.Load(s);
                var textos = doc.Descendants(a + "t")
                    .Select(t => t.Value.Trim())
                    .Where(t => t.Length > 0)
                    .ToList();

                if (textos.Count == 0)
                    continue;

                sb.AppendLine($"--- Diapositiva {i + 1} ---");
                foreach (var texto in textos)
                    sb.AppendLine(texto);
                sb.AppendLine();
            }

            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }
}
