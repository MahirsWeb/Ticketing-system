using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Identity;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Security;

/// <summary>
/// Which accounts a signed-in person may list or open. Branches are isolated, and that includes the people on the other
/// side of the tickets: an employee sees the clients who have a ticket in their own branch, not the whole customer base
/// (about 2,900 names, emails and phone numbers, most of whom never wrote to this branch). Admin sees everyone.
/// Staff accounts stay limited to the employee's own branch, as before.
/// </summary>
public static class ClientVisibility
{
    /// <summary>
    /// Narrows an account query to what <paramref name="me"/> may see. <paramref name="branchClientIds"/> comes from
    /// <see cref="ClientsOfBranchAsync"/>.
    ///
    /// The set of the branch's clients is fetched FIRST and handed to the query as a plain list (a few hundred ids). Putting
    /// it into the query as a subquery ("... IN (SELECT DISTINCT ClientId FROM Tickets WHERE DepartmentId = @b)") looks the
    /// same but let SQL Server pick a plan that, for the big branch (19,000 tickets), took anywhere from 150 ms to more than
    /// 12 seconds depending on the extra filters (name search, ordering). A list parameter always plans well.
    /// </summary>
    public static IQueryable<ApplicationUser> VisibleTo(this IQueryable<ApplicationUser> users, ICurrentUserService me, IReadOnlyCollection<Guid> branchClientIds)
    {
        if (me.Role == UserRole.Admin) return users;

        var branch = me.DepartmentId;
        var ids = branchClientIds.ToList(); // a concrete List<Guid> translates to a single JSON parameter
        return users.Where(u => (u.Role == UserRole.Client && ids.Contains(u.Id))
                                || (u.Role != UserRole.Client && u.DepartmentId == branch));
    }

    /// <summary>The ids of every client with a ticket in the employee's branch (empty for Admin, who is not filtered).</summary>
    public static async Task<List<Guid>> ClientsOfBranchAsync(AppDbContext db, ICurrentUserService me, CancellationToken ct)
    {
        if (me.Role == UserRole.Admin) return new List<Guid>();

        var branch = me.DepartmentId;
        return await db.Tickets.AsNoTracking()
            .Where(t => t.DepartmentId == branch)
            .Select(t => t.ClientId)
            .Distinct()
            .ToListAsync(ct);
    }

    /// <summary>True when this one client has a ticket in the employee's branch (Admin: always).</summary>
    public static bool CanSeeClient(AppDbContext db, ICurrentUserService me, Guid clientId) =>
        me.Role == UserRole.Admin || db.Tickets.Any(t => t.ClientId == clientId && t.DepartmentId == me.DepartmentId);
}
