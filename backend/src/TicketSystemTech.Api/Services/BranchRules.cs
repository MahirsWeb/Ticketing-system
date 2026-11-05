using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Services;

/// <summary>Rules about branches that tickets and people share.</summary>
public interface IBranchRules
{
    /// <summary>A branch with sub-branches requires picking exactly one of them; a branch without any must not receive one. Returns the problem as text, or null.</summary>
    Task<string?> ValidateSubBranchAsync(Guid departmentId, Guid? subBranchId);
}

public sealed class BranchRules : IBranchRules
{
    private readonly AppDbContext _db;

    public BranchRules(AppDbContext db) => _db = db;

    /// <summary>A branch with sub-branches requires picking exactly one of them; a branch without any must not receive one.</summary>
    public async Task<string?> ValidateSubBranchAsync(Guid departmentId, Guid? subBranchId)
    {
        var activeSubBranches = await _db.SubBranches.Where(s => s.DepartmentId == departmentId && s.IsActive).ToListAsync();
        if (activeSubBranches.Count == 0)
            return subBranchId.HasValue ? "This branch has no sub-branches." : null;

        if (subBranchId is null) return "A sub-branch must be selected for this branch.";
        if (!activeSubBranches.Any(s => s.Id == subBranchId.Value)) return "Sub-branch not found for this branch.";
        return null;
    }
}
