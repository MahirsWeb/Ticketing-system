using TicketSystemTech.Domain.Common;

namespace TicketSystemTech.Domain.Entities;

/// <summary>
/// An optional finer-grained unit within a branch (Department) — e.g. "Consulting" and "Technical Support"
/// both under the "Software" branch, sharing the branch's single email but tracked separately for statistics
/// and ticket assignment. A branch with no sub-branches behaves exactly as before.
/// </summary>
public class SubBranch : BaseEntity
{
    public Guid DepartmentId { get; set; }
    public Department? Department { get; set; }

    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
