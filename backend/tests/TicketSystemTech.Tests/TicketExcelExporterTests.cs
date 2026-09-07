using NPOI.XSSF.UserModel;
using TicketSystemTech.Application.Reporting;

namespace TicketSystemTech.Tests;

public class TicketExcelExporterTests
{
    private static List<TicketExportRow> SampleRows() =>
    [
        new("100001", "Printer not working", "Closed", "Servis", "Acme d.o.o.", "Ana Anić", "Marko Marković",
            new DateTime(2026, 1, 5), new DateTime(2026, 1, 5, 9, 0, 0), new DateTime(2026, 1, 6, 12, 0, 0), 27.0, "Marko Marković"),
        new("100002", "Cannot log in", "Open", "Software", "Beta d.o.o.", "Berina Berić", "Selma Selimović",
            new DateTime(2026, 2, 10), new DateTime(2026, 2, 10, 8, 0, 0), null, null, null),
        new("100003", "Invoice question", "Closed", "Servis", "Acme d.o.o.", "Ana Anić", "Marko Marković",
            new DateTime(2026, 2, 20), new DateTime(2026, 2, 20, 10, 0, 0), new DateTime(2026, 2, 21, 10, 0, 0), 24.0, "Marko Marković"),
    ];

    [Fact]
    public void Build_ProducesNonEmptyWorkbookBytes()
    {
        var bytes = TicketExcelExporter.Build(SampleRows(), new DateTime(2026, 1, 1), new DateTime(2026, 2, 28));

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void Build_OutputIsAValidWorkbookWithBothSheets()
    {
        var bytes = TicketExcelExporter.Build(SampleRows(), new DateTime(2026, 1, 1), new DateTime(2026, 2, 28));

        using var ms = new MemoryStream(bytes);
        using var workbook = new XSSFWorkbook(ms);

        Assert.Equal(2, workbook.NumberOfSheets);

        var ticketsSheet = workbook.GetSheet("Tickets");
        Assert.NotNull(ticketsSheet);
        // Header row + 3 data rows.
        Assert.Equal(3, ticketsSheet.LastRowNum);
        Assert.Equal("Ticket Number", ticketsSheet.GetRow(0).GetCell(0).StringCellValue);
        Assert.Equal("100001", ticketsSheet.GetRow(1).GetCell(0).StringCellValue);

        var summarySheet = workbook.GetSheet("Summary");
        Assert.NotNull(summarySheet);
    }

    [Fact]
    public void Build_WithNoRows_StillProducesAReadableWorkbook()
    {
        var bytes = TicketExcelExporter.Build([], new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));

        using var ms = new MemoryStream(bytes);
        using var workbook = new XSSFWorkbook(ms);

        Assert.Equal(2, workbook.NumberOfSheets);
        var ticketsSheet = workbook.GetSheet("Tickets");
        Assert.Equal(0, ticketsSheet.LastRowNum); // only the header row
    }
}
