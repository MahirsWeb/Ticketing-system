using TicketSystemTech.Api.Security;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Identity;

namespace TicketSystemTech.Tests;

/// <summary>T-27: an employee sees the clients of their own branch and the staff of their own branch, nobody else.</summary>
public class ClientVisibilityTests
{
    private static readonly Guid BranchA = Guid.NewGuid();
    private static readonly Guid BranchB = Guid.NewGuid();

    private static ApplicationUser Client() => new() { Id = Guid.NewGuid(), Role = UserRole.Client, FirstName = "c" };
    private static ApplicationUser Staff(Guid branch) => new() { Id = Guid.NewGuid(), Role = UserRole.Employee, DepartmentId = branch, FirstName = "s" };

    private sealed class Me : ICurrentUserService
    {
        public Me(UserRole role, Guid? department) { Role = role; DepartmentId = department; }
        public Guid? UserId { get; } = Guid.NewGuid();
        public UserRole? Role { get; }
        public Guid? CompanyId => null;
        public Guid? DepartmentId { get; }
        public bool IsAuthenticated => true;
    }

    [Fact]
    public void Admin_sees_everyone_whatever_the_branch_set_says()
    {
        var all = new[] { Client(), Client(), Staff(BranchA), Staff(BranchB) }.AsQueryable();

        var seen = all.VisibleTo(new Me(UserRole.Admin, null), Array.Empty<Guid>()).ToList();

        Assert.Equal(4, seen.Count);
    }

    [Fact]
    public void An_employee_sees_only_clients_of_their_branch_and_staff_of_their_branch()
    {
        var mine = Client();
        var other = Client();
        var colleague = Staff(BranchA);
        var stranger = Staff(BranchB);
        var all = new[] { mine, other, colleague, stranger }.AsQueryable();

        var seen = all.VisibleTo(new Me(UserRole.Employee, BranchA), new[] { mine.Id }).Select(u => u.Id).ToHashSet();

        Assert.Equal(new[] { mine.Id, colleague.Id }.ToHashSet(), seen);
    }

    [Fact]
    public void A_branch_without_tickets_has_no_clients_to_show()
    {
        var all = new[] { Client(), Client(), Staff(BranchA) }.AsQueryable();

        var seen = all.VisibleTo(new Me(UserRole.Employee, BranchA), Array.Empty<Guid>()).ToList();

        Assert.Single(seen);
        Assert.All(seen, u => Assert.Equal(UserRole.Employee, u.Role));
    }

    [Fact]
    public void An_id_in_the_branch_set_never_reveals_a_staff_account_of_another_branch()
    {
        // The set holds client ids only, but even if a staff id ended up in it, the role check must keep it hidden.
        var stranger = Staff(BranchB);
        var all = new[] { stranger }.AsQueryable();

        var seen = all.VisibleTo(new Me(UserRole.Employee, BranchA), new[] { stranger.Id }).ToList();

        Assert.Empty(seen);
    }
}
