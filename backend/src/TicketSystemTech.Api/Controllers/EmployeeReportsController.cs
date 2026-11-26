using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Filters;
using TicketSystemTech.Api.Reports;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Api.Controllers;

/// <summary>
/// The reports about people (the "Employees" tab). Open to Admin and Employee, but about a person: an employee only gets themself, and asking for another
/// person is refused (see <see cref="IEmployeeReportService"/>). The dependency of companies on one person is for Admin only.
/// </summary>
[ApiController]
[Route("api/reports/employees")]
[Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
[RejectReversedDateRange]
public class EmployeeReportsController : ControllerBase
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly IEmployeeReportService _reports;

    public EmployeeReportsController(IEmployeeReportService reports) => _reports = reports;

    /// <summary>Open tickets and tasks of each person, with the tickets that are overdue. Whatever the period.</summary>
    [HttpGet("workload")]
    public async Task<ActionResult<WorkloadDto>> Workload(Guid? departmentId, CancellationToken ct = default) =>
        Ok(await _reports.WorkloadAsync(departmentId, ct));

    /// <summary>The recorded hours of each person in the period (an employee: only their own).</summary>
    [HttpGet("hours")]
    public async Task<ActionResult<HoursPeopleDto>> Hours(DateTime? from, DateTime? to, Guid? departmentId, CancellationToken ct = default) =>
        Ok(await _reports.HoursAsync(from, to, departmentId, ct));

    /// <summary>One person's hours by day and by week, and the working days with nothing recorded.</summary>
    [HttpGet("hours/{userId:guid}")]
    public async Task<ActionResult<HoursDetailDto>> HoursOf(Guid userId, DateTime? from, DateTime? to, CancellationToken ct = default) =>
        this.ToActionResult(await _reports.HoursOfAsync(userId, from, to, ct));

    /// <summary>The top three topics, categories and companies of a person (Admin: say whom with userId).</summary>
    [HttpGet("specialization")]
    public async Task<ActionResult<SpecializationDto>> Specialization(Guid? userId, DateTime? from, DateTime? to, CancellationToken ct = default) =>
        this.ToActionResult(await _reports.SpecializationAsync(userId, from, to, ct));

    /// <summary>Companies where one person closed most of the tickets of the last 12 months. Admin only.</summary>
    [HttpGet("dependency")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<DependencyDto>> Dependency(int minClosed = EmployeeReportService.DefaultMinClosed, double minShare = EmployeeReportService.DefaultMinSharePercent, CancellationToken ct = default) =>
        Ok(await _reports.DependencyAsync(minClosed, minShare, ct));

    /// <summary>The monthly worksheet of a person (xlsx). Admin for anybody, an employee for themself. The language is the one asked for, or that of the person who asks.</summary>
    [HttpGet("timesheet")]
    public async Task<IActionResult> Timesheet(Guid userId, int year, int month, string? language, CancellationToken ct = default)
    {
        var outcome = await _reports.TimesheetAsync(userId, year, month, language, ct);
        if (!outcome.Succeeded) return this.ToResult(outcome);
        return File(outcome.Value!.Content, Xlsx, outcome.Value.FileName);
    }
}
