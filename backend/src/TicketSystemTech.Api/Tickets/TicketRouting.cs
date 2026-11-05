using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Emails;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Domain.Workflow;
using TicketSystemTech.Infrastructure.Persistence;
using TicketSystemTech.Infrastructure.Services;
using static TicketSystemTech.Api.Tickets.TicketHelpers;

namespace TicketSystemTech.Api.Tickets;

/// <summary>The checks that decide where a ticket may go and when it is due. Creating, opening and transferring a ticket all ask the same questions.</summary>
public interface ITicketRouting
{
    /// <summary>The branch, help topic and category must exist and be active; the first thing wrong is returned as text, or null when all is well.</summary>
    Task<string?> ValidateLookupsAsync(Guid helpTopicId, Guid? categoryId, Guid departmentId);

    /// <summary>A branch with sub-branches requires picking exactly one of them; a branch without any must not receive one.</summary>
    Task<string?> ValidateSubBranchAsync(Guid departmentId, Guid? subBranchId);

    /// <summary>The ticket's due date: what staff typed if they typed one, otherwise the SLA plan's resolution time counted from <paramref name="startUtc"/>.</summary>
    Task<(DateTime? DueDateUtc, string? Error)> ResolveDueDateAsync(Guid slaPlanId, DateTime? requested, DateTime startUtc);
}

public sealed class TicketRouting : ITicketRouting
{
    private readonly AppDbContext _db;
    private readonly ISlaCalculator _sla;
    private readonly IBranchRules _branches;

    public TicketRouting(AppDbContext db, ISlaCalculator sla, IBranchRules branches)
    {
        _db = db;
        _sla = sla;
        _branches = branches;
    }

    /// <summary>
    /// The lookups a staff member picks when opening a ticket must exist and still be in use. An unknown id used to reach
    /// the database and come back as a 500 (foreign-key failure); an inactive branch, topic or category was accepted, so a
    /// ticket could be routed to a branch that no longer exists in practice.
    /// </summary>
    public async Task<string?> ValidateLookupsAsync(Guid helpTopicId, Guid? categoryId, Guid departmentId)
    {
        if (!await _db.Departments.AnyAsync(d => d.Id == departmentId && d.IsActive)) return "Branch not found.";
        if (!await _db.HelpTopics.AnyAsync(h => h.Id == helpTopicId && h.IsActive)) return "Help topic not found.";
        if (categoryId.HasValue && !await _db.TicketCategories.AnyAsync(c => c.Id == categoryId.Value && c.IsActive)) return "Category not found.";
        return null;
    }

    public Task<string?> ValidateSubBranchAsync(Guid departmentId, Guid? subBranchId) => _branches.ValidateSubBranchAsync(departmentId, subBranchId);

    /// <summary>The ticket's due date: what staff typed if they typed one, otherwise the SLA plan's resolution time counted from <paramref name="startUtc"/>.</summary>
    public async Task<(DateTime? DueDateUtc, string? Error)> ResolveDueDateAsync(Guid slaPlanId, DateTime? requested, DateTime startUtc)
    {
        var plan = await _db.SlaPlans.AsNoTracking().FirstOrDefaultAsync(s => s.Id == slaPlanId && s.IsActive);
        if (plan is null) return (null, "SLA plan not found.");

        if (requested.HasValue)
        {
            var requestedUtc = AsUtc(requested.Value); // no zone means UTC (the same rule as work sessions and reports), not the host's zone
            if (requestedUtc <= startUtc) return (null, "The due date must be in the future.");
            return (requestedUtc, null);
        }

        return (_sla.ComputeDueDateUtc(startUtc, plan.ResolutionTimeHours), null);
    }
}
