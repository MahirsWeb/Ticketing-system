using System.Text;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using NPOI.XWPF.UserModel;
using TicketSystemTech.Application.Common.Interfaces;
using UglyToad.PdfPig;

namespace TicketSystemTech.Infrastructure.Services;

/// <summary>Extracts plain text from Word (.docx), Excel (.xls/.xlsx), and PDF files so they
/// can be chunked and embedded into the knowledge base alongside ticket history.
/// Legacy binary .doc is NOT supported — the NPOI build available on NuGet no longer ships the
/// HWPF (old Word format) extractor; a .doc file must be re-saved as .docx first.</summary>
public class DocumentTextExtractor : IDocumentTextExtractor
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".xls", ".xlsx", ".pdf"
    };

    public bool IsSupported(string fileName) => SupportedExtensions.Contains(Path.GetExtension(fileName));

    public Task<string?> ExtractTextAsync(string fileName, Stream content, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        try
        {
            string? text = extension switch
            {
                ".docx" => ExtractDocx(content),
                ".xlsx" => ExtractWorkbook(new XSSFWorkbook(content)),
                ".xls" => ExtractWorkbook(new HSSFWorkbook(content)),
                ".pdf" => ExtractPdf(content),
                _ => null
            };
            return Task.FromResult(text);
        }
        catch
        {
            // Corrupt/unreadable file (e.g. a stray Word lock file) — skip rather than fail the batch.
            return Task.FromResult<string?>(null);
        }
    }

    private static string ExtractDocx(Stream stream)
    {
        var doc = new XWPFDocument(stream);
        var sb = new StringBuilder();
        foreach (var p in doc.Paragraphs)
            sb.AppendLine(p.Text);
        foreach (var table in doc.Tables)
            foreach (var row in table.Rows)
                foreach (var cell in row.GetTableCells())
                    sb.AppendLine(cell.GetText());
        return sb.ToString();
    }

    private static string ExtractWorkbook(IWorkbook workbook)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < workbook.NumberOfSheets; i++)
        {
            var sheet = workbook.GetSheetAt(i);
            foreach (IRow row in sheet)
            {
                var cells = new List<string>();
                foreach (NPOI.SS.UserModel.ICell cell in row)
                    cells.Add(CellToString(cell));
                if (cells.Any(c => !string.IsNullOrWhiteSpace(c)))
                    sb.AppendLine(string.Join(" | ", cells));
            }
        }
        return sb.ToString();
    }

    private static string CellToString(NPOI.SS.UserModel.ICell cell)
    {
        try { return cell.ToString() ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static string ExtractPdf(Stream stream)
    {
        using var pdf = PdfDocument.Open(stream);
        var sb = new StringBuilder();
        foreach (var page in pdf.GetPages())
            sb.AppendLine(page.Text);
        return sb.ToString();
    }
}
