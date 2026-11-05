using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Lookups;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Api.Controllers;

/// <summary>The HTTP face of the lists the application is configured with: companies, branches, sub-branches, help topics, categories and SLA plans.</summary>
[ApiController]
[Route("api")]
[Authorize]
public class LookupsController : ControllerBase
{
    private readonly ICompanyService _companies;
    private readonly IBranchService _branches;
    private readonly ILookupTableService _lookups;

    public LookupsController(ICompanyService companies, IBranchService branches, ILookupTableService lookups)
    {
        _companies = companies;
        _branches = branches;
        _lookups = lookups;
    }


    // ---------------- Companies ----------------

    [HttpGet("companies")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<List<CompanyItemFull>>> GetCompanies(CancellationToken ct, [FromQuery] bool includeInactive = false) =>
        Ok(await _companies.ListAsync(includeInactive, ct));

    [HttpGet("companies/{id:guid}")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<CompanyItemFull>> GetCompany(Guid id, CancellationToken ct) =>
        this.ToActionResult(await _companies.GetAsync(id, ct));

    [HttpPost("companies")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<LookupItem>> CreateCompany(CreateCompanyRequest request) =>
        Ok(await _companies.CreateAsync(request));

    [HttpPut("companies/{id:guid}")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<CompanyItemFull>> UpdateCompany(Guid id, UpdateCompanyRequest request) =>
        this.ToActionResult(await _companies.UpdateAsync(id, request));


    // ---------------- Departments ----------------

    [HttpGet("departments")]
    public async Task<ActionResult<List<object>>> GetDepartments(CancellationToken ct, [FromQuery] bool includeInactive = false) =>
        Ok(await _branches.ListAsync(includeInactive, ct));

    [HttpPost("departments")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<DepartmentItemFull>> CreateDepartment(CreateDepartmentRequest request) =>
        this.ToActionResult(await _branches.CreateAsync(request));

    [HttpPut("departments/{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<DepartmentItemFull>> UpdateDepartment(Guid id, UpdateDepartmentRequest request) =>
        this.ToActionResult(await _branches.UpdateAsync(id, request));

    /// <summary>
    /// Permanently removes a branch. Tickets, staff and sub-branches referencing it are not deleted —
    /// their branch/sub-branch reference is cleared to NULL by the database. Blocked if any tasks (which
    /// require a branch to exist) still belong to it.
    /// </summary>
    [HttpDelete("departments/{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> DeleteDepartment(Guid id)
    {
        var deleted = await _branches.DeleteAsync(id);
        return deleted.Succeeded ? NoContent() : this.ToResult(deleted);
    }


    // ---------------- Sub-branches ----------------

    /// <summary>Every authenticated user may list a branch's sub-branches — needed to populate the required sub-branch picker when opening tickets or assigning employees.</summary>
    [HttpGet("departments/{departmentId:guid}/sub-branches")]
    public async Task<ActionResult<List<SubBranchItemFull>>> GetSubBranches(Guid departmentId, CancellationToken ct, [FromQuery] bool includeInactive = false) =>
        Ok(await _branches.ListSubBranchesAsync(departmentId, includeInactive, ct));

    [HttpPost("departments/{departmentId:guid}/sub-branches")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<SubBranchItemFull>> CreateSubBranch(Guid departmentId, CreateSubBranchRequest request) =>
        this.ToActionResult(await _branches.CreateSubBranchAsync(departmentId, request));

    [HttpPut("sub-branches/{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<SubBranchItemFull>> UpdateSubBranch(Guid id, UpdateSubBranchRequest request) =>
        this.ToActionResult(await _branches.UpdateSubBranchAsync(id, request));


    // ---------------- Help Topics ----------------

    [HttpGet("help-topics")]
    public async Task<ActionResult<List<object>>> GetHelpTopics(CancellationToken ct, [FromQuery] bool includeInactive = false) =>
        Ok(await _lookups.ListHelpTopicsAsync(includeInactive, ct));

    [HttpPost("help-topics")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<LookupItemFull>> CreateHelpTopic(NameOnlyRequest request) =>
        Ok(await _lookups.CreateHelpTopicAsync(request));

    [HttpPut("help-topics/{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<LookupItemFull>> UpdateHelpTopic(Guid id, UpdateNameRequest request) =>
        this.ToActionResult(await _lookups.UpdateHelpTopicAsync(id, request));

    /// <summary>Refused while any ticket uses the help topic: deleting it would erase it from those tickets' history. Deactivate it instead.</summary>
    [HttpDelete("help-topics/{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> DeleteHelpTopic(Guid id)
    {
        var deleted = await _lookups.DeleteHelpTopicAsync(id);
        return deleted.Succeeded ? NoContent() : this.ToResult(deleted);
    }


    // ---------------- Ticket Categories ----------------
    // Internal-only classification staff pick when opening a ticket — clients never see this list.

    [HttpGet("ticket-categories")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<List<object>>> GetTicketCategories(CancellationToken ct, [FromQuery] bool includeInactive = false) =>
        Ok(await _lookups.ListTicketCategoriesAsync(includeInactive, ct));

    [HttpPost("ticket-categories")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<LookupItemFull>> CreateTicketCategory(NameOnlyRequest request) =>
        Ok(await _lookups.CreateTicketCategoryAsync(request));

    [HttpPut("ticket-categories/{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<LookupItemFull>> UpdateTicketCategory(Guid id, UpdateNameRequest request) =>
        this.ToActionResult(await _lookups.UpdateTicketCategoryAsync(id, request));

    /// <summary>Refused while any ticket uses the category: deleting it would erase it from those tickets' history. Deactivate it instead.</summary>
    [HttpDelete("ticket-categories/{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> DeleteTicketCategory(Guid id)
    {
        var deleted = await _lookups.DeleteTicketCategoryAsync(id);
        return deleted.Succeeded ? NoContent() : this.ToResult(deleted);
    }


    // ---------------- SLA Plans ----------------

    [HttpGet("sla-plans")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<List<object>>> GetSlaPlans(CancellationToken ct, [FromQuery] bool includeInactive = false) =>
        Ok(await _lookups.ListSlaPlansAsync(includeInactive, ct));

    [HttpPost("sla-plans")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<SlaPlanItemFull>> CreateSlaPlan(CreateSlaPlanRequest request) =>
        Ok(await _lookups.CreateSlaPlanAsync(request));

    [HttpPut("sla-plans/{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<SlaPlanItemFull>> UpdateSlaPlan(Guid id, UpdateSlaPlanRequest request) =>
        this.ToActionResult(await _lookups.UpdateSlaPlanAsync(id, request));

    /// <summary>Refused while any ticket uses the SLA plan: deleting it would erase it from those tickets' history. Deactivate it instead.</summary>
    [HttpDelete("sla-plans/{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> DeleteSlaPlan(Guid id)
    {
        var deleted = await _lookups.DeleteSlaPlanAsync(id);
        return deleted.Succeeded ? NoContent() : this.ToResult(deleted);
    }

}
