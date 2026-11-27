using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Reports;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Api.Controllers;

/// <summary>
/// The documents: the card of a ticket and the monthly report of a company, each as figures for the screen (and the page for printing) and as an xlsx. Both are for
/// Admin and Employee; a client is given the "for the client" version of the monthly report by staff, and has no document of their own.
/// </summary>
[ApiController]
[Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
public class DocumentsController : ControllerBase
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly ITicketCardService _cards;
    private readonly ICompanyMonthlyReportService _monthly;

    public DocumentsController(ITicketCardService cards, ICompanyMonthlyReportService monthly)
    {
        _cards = cards;
        _monthly = monthly;
    }

    /// <summary>The whole life of a ticket on one line of time.</summary>
    [HttpGet("api/tickets/{id:guid}/card")]
    public async Task<ActionResult<TicketCardDto>> Card(Guid id, CancellationToken ct) =>
        this.ToActionResult(await _cards.CardAsync(id, ct));

    [HttpGet("api/tickets/{id:guid}/card/export")]
    public async Task<IActionResult> CardExport(Guid id, string? language, CancellationToken ct)
    {
        var outcome = await _cards.ExportAsync(id, language, ct);
        if (!outcome.Succeeded) return this.ToResult(outcome);
        return File(outcome.Value!.Content, Xlsx, outcome.Value.FileName);
    }

    /// <summary>One company, one calendar month. <c>audience</c>: internal (default) or client; <c>includeHours</c> matters for the client version only; <c>limit</c> cuts the list of tickets (at most a thousand), never the figures.</summary>
    [HttpGet("api/reports/company-monthly")]
    public async Task<ActionResult<CompanyMonthlyDto>> Monthly(Guid companyId, int year, int month, string? audience, bool? includeHours, int limit = CompanyMonthlyReportService.MostTickets, CancellationToken ct = default) =>
        this.ToActionResult(await _monthly.ReportAsync(companyId, year, month, audience, includeHours, limit, ct));

    [HttpGet("api/reports/company-monthly/export")]
    public async Task<IActionResult> MonthlyExport(Guid companyId, int year, int month, string? audience, bool? includeHours, string? language, CancellationToken ct)
    {
        var outcome = await _monthly.ExportAsync(companyId, year, month, audience, includeHours, language, ct);
        if (!outcome.Succeeded) return this.ToResult(outcome);
        return File(outcome.Value!.Content, Xlsx, outcome.Value.FileName);
    }
}
