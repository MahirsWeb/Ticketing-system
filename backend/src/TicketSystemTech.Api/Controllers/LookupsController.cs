using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Emails;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class LookupsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly ICurrentUserService _currentUser;

    public LookupsController(AppDbContext db, IEmailSender emailSender, ICurrentUserService currentUser)
    {
        _db = db;
        _emailSender = emailSender;
        _currentUser = currentUser;
    }

    // ---------------- Companies ----------------

    [HttpGet("companies")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<List<CompanyItemFull>>> GetCompanies([FromQuery] bool includeInactive = false)
    {
        var isAdmin = User.IsInRole(nameof(UserRole.Admin));
        var query = _db.Companies.AsQueryable();
        if (!includeInactive || !isAdmin) query = query.Where(c => c.IsActive);

        var items = await query.OrderBy(c => c.Name)
            .Select(c => new CompanyItemFull(c.Id, c.Name, c.Address, c.ContactInfo, c.IsActive)).ToListAsync();
        return Ok(items);
    }

    [HttpGet("companies/{id:guid}")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<CompanyItemFull>> GetCompany(Guid id)
    {
        var c = await _db.Companies.FindAsync(id);
        if (c is null) return NotFound();
        return Ok(new CompanyItemFull(c.Id, c.Name, c.Address, c.ContactInfo, c.IsActive));
    }

    [HttpPost("companies")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<LookupItem>> CreateCompany(CreateCompanyRequest request)
    {
        var company = new Company { Name = request.Name, Address = request.Address, ContactInfo = request.ContactInfo };
        _db.Companies.Add(company);
        await _db.SaveChangesAsync();
        return Ok(new LookupItem(company.Id, company.Name));
    }

    [HttpPut("companies/{id:guid}")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<CompanyItemFull>> UpdateCompany(Guid id, UpdateCompanyRequest request)
    {
        var c = await _db.Companies.FindAsync(id);
        if (c is null) return NotFound();

        c.Name = request.Name;
        c.Address = request.Address;
        c.ContactInfo = request.ContactInfo;
        c.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        return Ok(new CompanyItemFull(c.Id, c.Name, c.Address, c.ContactInfo, c.IsActive));
    }

    // ---------------- Departments ----------------

    [HttpGet("departments")]
    public async Task<ActionResult<List<object>>> GetDepartments([FromQuery] bool includeInactive = false)
    {
        var isAdmin = User.IsInRole(nameof(UserRole.Admin));
        var query = _db.Departments.AsQueryable();
        if (!includeInactive || !isAdmin) query = query.Where(d => d.IsActive);

        // Every authenticated user may see branch names + emails (needed to display "your branch", pick a transfer target, etc.).
        var full = await query.OrderBy(d => d.Name).Select(d => new DepartmentItemFull(d.Id, d.Name, d.Email, d.IsActive)).ToListAsync();
        return Ok(full);
    }

    [HttpPost("departments")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<DepartmentItemFull>> CreateDepartment(CreateDepartmentRequest request)
    {
        var emailTaken = await _db.Departments.AnyAsync(d => d.Email == request.Email);
        if (emailTaken) return Conflict(new { message = "A branch with this email already exists." });

        var entity = new Department { Name = request.Name, Email = request.Email };
        _db.Departments.Add(entity);
        await _db.SaveChangesAsync();

        var creatorEmail = await _db.Users.Where(u => u.Id == _currentUser.UserId).Select(u => u.Email).FirstOrDefaultAsync();
        await _emailSender.SendAsync(entity.Email, "Welcome to Ticket System Tech",
            EmailTemplates.BranchWelcome(entity.Name, creatorEmail ?? "your administrator"));

        return Ok(new DepartmentItemFull(entity.Id, entity.Name, entity.Email, entity.IsActive));
    }

    [HttpPut("departments/{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<DepartmentItemFull>> UpdateDepartment(Guid id, UpdateDepartmentRequest request)
    {
        var entity = await _db.Departments.FindAsync(id);
        if (entity is null) return NotFound();

        var emailTaken = await _db.Departments.AnyAsync(d => d.Email == request.Email && d.Id != id);
        if (emailTaken) return Conflict(new { message = "A branch with this email already exists." });

        entity.Name = request.Name;
        entity.Email = request.Email;
        entity.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        return Ok(new DepartmentItemFull(entity.Id, entity.Name, entity.Email, entity.IsActive));
    }

    /// <summary>
    /// Permanently removes a branch. Tickets, staff and sub-branches referencing it are not deleted —
    /// their branch/sub-branch reference is cleared to NULL by the database. Blocked if any tasks (which
    /// require a branch to exist) still belong to it.
    /// </summary>
    [HttpDelete("departments/{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> DeleteDepartment(Guid id)
    {
        var entity = await _db.Departments.FindAsync(id);
        if (entity is null) return NotFound();

        var taskCount = await _db.WorkTasks.CountAsync(t => t.DepartmentId == id);
        if (taskCount > 0)
            return BadRequest(new { message = $"This branch still has {taskCount} task(s) assigned to it. Reassign or complete them first." });

        var subBranchCount = await _db.SubBranches.CountAsync(s => s.DepartmentId == id);
        if (subBranchCount > 0)
            return BadRequest(new { message = $"This branch still has {subBranchCount} sub-branch(es). Remove them first." });

        _db.Departments.Remove(entity);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ---------------- Sub-branches ----------------

    /// <summary>Every authenticated user may list a branch's sub-branches — needed to populate the required sub-branch picker when opening tickets or assigning employees.</summary>
    [HttpGet("departments/{departmentId:guid}/sub-branches")]
    public async Task<ActionResult<List<SubBranchItemFull>>> GetSubBranches(Guid departmentId, [FromQuery] bool includeInactive = false)
    {
        var isAdmin = User.IsInRole(nameof(UserRole.Admin));
        var query = _db.SubBranches.Where(s => s.DepartmentId == departmentId);
        if (!includeInactive || !isAdmin) query = query.Where(s => s.IsActive);

        var items = await query.OrderBy(s => s.Name)
            .Select(s => new SubBranchItemFull(s.Id, s.Name, s.IsActive, s.DepartmentId)).ToListAsync();
        return Ok(items);
    }

    [HttpPost("departments/{departmentId:guid}/sub-branches")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<SubBranchItemFull>> CreateSubBranch(Guid departmentId, CreateSubBranchRequest request)
    {
        var departmentExists = await _db.Departments.AnyAsync(d => d.Id == departmentId);
        if (!departmentExists) return NotFound(new { message = "Branch not found." });

        var nameTaken = await _db.SubBranches.AnyAsync(s => s.DepartmentId == departmentId && s.Name == request.Name);
        if (nameTaken) return Conflict(new { message = "This branch already has a sub-branch with that name." });

        var entity = new SubBranch { DepartmentId = departmentId, Name = request.Name };
        _db.SubBranches.Add(entity);
        await _db.SaveChangesAsync();
        return Ok(new SubBranchItemFull(entity.Id, entity.Name, entity.IsActive, entity.DepartmentId));
    }

    [HttpPut("sub-branches/{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<SubBranchItemFull>> UpdateSubBranch(Guid id, UpdateSubBranchRequest request)
    {
        var entity = await _db.SubBranches.FindAsync(id);
        if (entity is null) return NotFound();

        var nameTaken = await _db.SubBranches.AnyAsync(s => s.DepartmentId == entity.DepartmentId && s.Name == request.Name && s.Id != id);
        if (nameTaken) return Conflict(new { message = "This branch already has a sub-branch with that name." });

        entity.Name = request.Name;
        entity.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        return Ok(new SubBranchItemFull(entity.Id, entity.Name, entity.IsActive, entity.DepartmentId));
    }

    // ---------------- Help Topics ----------------

    [HttpGet("help-topics")]
    public async Task<ActionResult<List<object>>> GetHelpTopics([FromQuery] bool includeInactive = false)
    {
        var isAdmin = User.IsInRole(nameof(UserRole.Admin));
        var query = _db.HelpTopics.AsQueryable();
        if (!includeInactive || !isAdmin) query = query.Where(h => h.IsActive);

        if (isAdmin)
        {
            var full = await query.OrderBy(h => h.Name).Select(h => new LookupItemFull(h.Id, h.Name, h.IsActive)).ToListAsync();
            return Ok(full);
        }
        var items = await query.OrderBy(h => h.Name).Select(h => new LookupItem(h.Id, h.Name)).ToListAsync();
        return Ok(items);
    }

    [HttpPost("help-topics")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<LookupItemFull>> CreateHelpTopic(NameOnlyRequest request)
    {
        var entity = new HelpTopic { Name = request.Name };
        _db.HelpTopics.Add(entity);
        await _db.SaveChangesAsync();
        return Ok(new LookupItemFull(entity.Id, entity.Name, entity.IsActive));
    }

    [HttpPut("help-topics/{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<LookupItemFull>> UpdateHelpTopic(Guid id, UpdateNameRequest request)
    {
        var entity = await _db.HelpTopics.FindAsync(id);
        if (entity is null) return NotFound();
        entity.Name = request.Name;
        entity.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        return Ok(new LookupItemFull(entity.Id, entity.Name, entity.IsActive));
    }

    /// <summary>Tickets referencing this help topic have it cleared to NULL by the database, not deleted.</summary>
    [HttpDelete("help-topics/{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> DeleteHelpTopic(Guid id)
    {
        var entity = await _db.HelpTopics.FindAsync(id);
        if (entity is null) return NotFound();
        _db.HelpTopics.Remove(entity);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ---------------- Ticket Categories ----------------
    // Internal-only classification staff pick when opening a ticket — clients never see this list.

    [HttpGet("ticket-categories")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<List<object>>> GetTicketCategories([FromQuery] bool includeInactive = false)
    {
        var isAdmin = User.IsInRole(nameof(UserRole.Admin));
        var query = _db.TicketCategories.AsQueryable();
        if (!includeInactive || !isAdmin) query = query.Where(c => c.IsActive);

        if (isAdmin)
        {
            var full = await query.OrderBy(c => c.Name).Select(c => new LookupItemFull(c.Id, c.Name, c.IsActive)).ToListAsync();
            return Ok(full);
        }
        var items = await query.OrderBy(c => c.Name).Select(c => new LookupItem(c.Id, c.Name)).ToListAsync();
        return Ok(items);
    }

    [HttpPost("ticket-categories")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<LookupItemFull>> CreateTicketCategory(NameOnlyRequest request)
    {
        var entity = new TicketCategory { Name = request.Name };
        _db.TicketCategories.Add(entity);
        await _db.SaveChangesAsync();
        return Ok(new LookupItemFull(entity.Id, entity.Name, entity.IsActive));
    }

    [HttpPut("ticket-categories/{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<LookupItemFull>> UpdateTicketCategory(Guid id, UpdateNameRequest request)
    {
        var entity = await _db.TicketCategories.FindAsync(id);
        if (entity is null) return NotFound();
        entity.Name = request.Name;
        entity.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        return Ok(new LookupItemFull(entity.Id, entity.Name, entity.IsActive));
    }

    /// <summary>Tickets referencing this category have it cleared to NULL by the database, not deleted.</summary>
    [HttpDelete("ticket-categories/{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> DeleteTicketCategory(Guid id)
    {
        var entity = await _db.TicketCategories.FindAsync(id);
        if (entity is null) return NotFound();
        _db.TicketCategories.Remove(entity);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ---------------- SLA Plans ----------------

    [HttpGet("sla-plans")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<List<object>>> GetSlaPlans([FromQuery] bool includeInactive = false)
    {
        var isAdmin = User.IsInRole(nameof(UserRole.Admin));
        var query = _db.SlaPlans.AsQueryable();
        if (!includeInactive || !isAdmin) query = query.Where(s => s.IsActive);

        if (isAdmin)
        {
            var full = await query.OrderBy(s => s.ResolutionTimeHours)
                .Select(s => new SlaPlanItemFull(s.Id, s.Name, s.ResolutionTimeHours, s.IsActive)).ToListAsync();
            return Ok(full);
        }
        var items = await query.OrderBy(s => s.ResolutionTimeHours)
            .Select(s => new SlaPlanItem(s.Id, s.Name, s.ResolutionTimeHours)).ToListAsync();
        return Ok(items);
    }

    [HttpPost("sla-plans")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<SlaPlanItemFull>> CreateSlaPlan(CreateSlaPlanRequest request)
    {
        var entity = new SlaPlan { Name = request.Name, ResolutionTimeHours = request.ResolutionTimeHours };
        _db.SlaPlans.Add(entity);
        await _db.SaveChangesAsync();
        return Ok(new SlaPlanItemFull(entity.Id, entity.Name, entity.ResolutionTimeHours, entity.IsActive));
    }

    [HttpPut("sla-plans/{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<SlaPlanItemFull>> UpdateSlaPlan(Guid id, UpdateSlaPlanRequest request)
    {
        var entity = await _db.SlaPlans.FindAsync(id);
        if (entity is null) return NotFound();
        entity.Name = request.Name;
        entity.ResolutionTimeHours = request.ResolutionTimeHours;
        entity.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        return Ok(new SlaPlanItemFull(entity.Id, entity.Name, entity.ResolutionTimeHours, entity.IsActive));
    }

    /// <summary>Tickets referencing this SLA plan have it cleared to NULL by the database, not deleted.</summary>
    [HttpDelete("sla-plans/{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> DeleteSlaPlan(Guid id)
    {
        var entity = await _db.SlaPlans.FindAsync(id);
        if (entity is null) return NotFound();
        _db.SlaPlans.Remove(entity);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
