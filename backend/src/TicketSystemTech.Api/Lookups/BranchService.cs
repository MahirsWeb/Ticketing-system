using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Notifications;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Lookups;

/// <summary>Branches and their sub-branches. Everybody may see them; Admin makes and changes them.</summary>
public interface IBranchService
{
    Task<List<DepartmentItemFull>> ListAsync(bool includeInactive, CancellationToken ct);
    Task<Outcome<DepartmentItemFull>> CreateAsync(CreateDepartmentRequest request);
    Task<Outcome<DepartmentItemFull>> UpdateAsync(Guid id, UpdateDepartmentRequest request);
    Task<Outcome<Done>> DeleteAsync(Guid id);
    Task<List<SubBranchItemFull>> ListSubBranchesAsync(Guid departmentId, bool includeInactive, CancellationToken ct);
    Task<Outcome<SubBranchItemFull>> CreateSubBranchAsync(Guid departmentId, CreateSubBranchRequest request);
    Task<Outcome<SubBranchItemFull>> UpdateSubBranchAsync(Guid id, UpdateSubBranchRequest request);
}

public sealed class BranchService : IBranchService
{
    private readonly AppDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly ICurrentUserService _currentUser;

    public BranchService(AppDbContext db, IEmailSender emailSender, ICurrentUserService currentUser)
    {
        _db = db;
        _emailSender = emailSender;
        _currentUser = currentUser;
    }

    public async Task<List<DepartmentItemFull>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var isAdmin = _currentUser.Role == UserRole.Admin;
        var query = _db.Departments.AsQueryable();
        if (!includeInactive || !isAdmin) query = query.Where(d => d.IsActive);

        // Every authenticated user may see branch names + emails (needed to display "your branch", pick a transfer target, etc.).
        var full = await query.OrderBy(d => d.Name).Select(d => new DepartmentItemFull(d.Id, d.Name, d.Email, d.IsActive)).ToListAsync(ct);
        return full;
    }

    public async Task<Outcome<DepartmentItemFull>> CreateAsync(CreateDepartmentRequest request)
    {
        var emailTaken = await _db.Departments.AnyAsync(d => d.Email == request.Email);
        if (emailTaken) return Outcome<DepartmentItemFull>.Conflict("A branch with this email already exists.");

        var entity = new Department { Name = request.Name, Email = request.Email };
        _db.Departments.Add(entity);
        _db.Audit(_currentUser.UserId, "DepartmentCreated", nameof(Department), entity.Id, $"Branch '{entity.Name}'");
        await _db.SaveChangesAsync();

        var creatorEmail = await _db.Users.Where(u => u.Id == _currentUser.UserId).Select(u => u.Email).FirstOrDefaultAsync();
        var welcome = AccountMessages.BranchWelcome(entity.Name, creatorEmail ?? "your administrator");
        await _emailSender.SendAsync(entity.Email, welcome.Subject, welcome.Html);

        return Outcome<DepartmentItemFull>.Ok(new DepartmentItemFull(entity.Id, entity.Name, entity.Email, entity.IsActive));
    }

    public async Task<Outcome<DepartmentItemFull>> UpdateAsync(Guid id, UpdateDepartmentRequest request)
    {
        var entity = await _db.Departments.FindAsync(id);
        if (entity is null) return Outcome<DepartmentItemFull>.NotFound();

        var emailTaken = await _db.Departments.AnyAsync(d => d.Email == request.Email && d.Id != id);
        if (emailTaken) return Outcome<DepartmentItemFull>.Conflict("A branch with this email already exists.");

        _db.Audit(_currentUser.UserId, "DepartmentUpdated", nameof(Department), entity.Id,
            $"Active: {entity.IsActive} -> {request.IsActive}; name changed: {entity.Name != request.Name}; e-mail changed: {entity.Email != request.Email}");
        entity.Name = request.Name;
        entity.Email = request.Email;
        entity.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        return Outcome<DepartmentItemFull>.Ok(new DepartmentItemFull(entity.Id, entity.Name, entity.Email, entity.IsActive));
    }

    public async Task<Outcome<Done>> DeleteAsync(Guid id)
    {
        var entity = await _db.Departments.FindAsync(id);
        if (entity is null) return Outcome<Done>.NotFound();

        var taskCount = await _db.WorkTasks.CountAsync(t => t.DepartmentId == id);
        if (taskCount > 0)
            return Outcome<Done>.Invalid($"This branch still has {taskCount} task(s) assigned to it. Reassign or complete them first.");

        var subBranchCount = await _db.SubBranches.CountAsync(s => s.DepartmentId == id);
        if (subBranchCount > 0)
            return Outcome<Done>.Invalid($"This branch still has {subBranchCount} sub-branch(es). Remove them first.");

        // A connected mailbox holds OAuth tokens for the branch; deleting the branch would strand them (and the database
        // now refuses it with a foreign key), so ask for the mailbox to be disconnected first.
        if (await _db.EmailConnections.AnyAsync(c => c.DepartmentId == id))
            return Outcome<Done>.Invalid("This branch still has a connected mailbox. Disconnect it first.");

        // A ticket without a branch is treated as "unrouted" and is visible to every employee, and a staff member without a
        // branch is lost to every list. Both used to be what the database did when the branch was deleted (ON DELETE SET NULL);
        // now it refuses (RESTRICT), and these checks say why in words.
        var staffCount = await _db.Users.CountAsync(u => u.DepartmentId == id);
        if (staffCount > 0)
            return Outcome<Done>.Invalid($"This branch still has {staffCount} staff member(s). Move or remove them first.");

        var ticketCount = await _db.Tickets.CountAsync(t => t.DepartmentId == id);
        if (ticketCount > 0)
            return Outcome<Done>.Invalid($"This branch still owns {ticketCount} ticket(s). Transfer them to another branch first.");

        _db.Departments.Remove(entity);
        _db.Audit(_currentUser.UserId, "DepartmentDeleted", nameof(Department), entity.Id, $"Branch '{entity.Name}'");
        await _db.SaveChangesAsync();
        return Outcome<Done>.Ok(Done.Value);
    }

    public async Task<List<SubBranchItemFull>> ListSubBranchesAsync(Guid departmentId, bool includeInactive, CancellationToken ct)
    {
        var isAdmin = _currentUser.Role == UserRole.Admin;
        var query = _db.SubBranches.Where(s => s.DepartmentId == departmentId);
        if (!includeInactive || !isAdmin) query = query.Where(s => s.IsActive);

        var items = await query.OrderBy(s => s.Name)
            .Select(s => new SubBranchItemFull(s.Id, s.Name, s.IsActive, s.DepartmentId)).ToListAsync(ct);
        return items;
    }

    public async Task<Outcome<SubBranchItemFull>> CreateSubBranchAsync(Guid departmentId, CreateSubBranchRequest request)
    {
        var departmentExists = await _db.Departments.AnyAsync(d => d.Id == departmentId);
        if (!departmentExists) return Outcome<SubBranchItemFull>.NotFound("Branch not found.");

        var nameTaken = await _db.SubBranches.AnyAsync(s => s.DepartmentId == departmentId && s.Name == request.Name);
        if (nameTaken) return Outcome<SubBranchItemFull>.Conflict("This branch already has a sub-branch with that name.");

        var entity = new SubBranch { DepartmentId = departmentId, Name = request.Name };
        _db.SubBranches.Add(entity);
        await _db.SaveChangesAsync();
        return Outcome<SubBranchItemFull>.Ok(new SubBranchItemFull(entity.Id, entity.Name, entity.IsActive, entity.DepartmentId));
    }

    public async Task<Outcome<SubBranchItemFull>> UpdateSubBranchAsync(Guid id, UpdateSubBranchRequest request)
    {
        var entity = await _db.SubBranches.FindAsync(id);
        if (entity is null) return Outcome<SubBranchItemFull>.NotFound();

        var nameTaken = await _db.SubBranches.AnyAsync(s => s.DepartmentId == entity.DepartmentId && s.Name == request.Name && s.Id != id);
        if (nameTaken) return Outcome<SubBranchItemFull>.Conflict("This branch already has a sub-branch with that name.");

        entity.Name = request.Name;
        entity.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        return Outcome<SubBranchItemFull>.Ok(new SubBranchItemFull(entity.Id, entity.Name, entity.IsActive, entity.DepartmentId));
    }
}
