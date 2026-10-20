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
            "txt" => Encoding.UTF8.GetString(bytes),
            "pdf" => ExtraerPdf(bytes),
            "docx" => ExtraerDocx(bytes),
            _ => string.Empty
        };
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
}
