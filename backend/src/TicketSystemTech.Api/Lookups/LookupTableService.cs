using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Emails;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Lookups;

/// <summary>The small lists tickets are classified by: help topics, categories and SLA plans.</summary>
public interface ILookupTableService
{
    Task<object> ListHelpTopicsAsync(bool includeInactive, CancellationToken ct);
    Task<LookupItemFull> CreateHelpTopicAsync(NameOnlyRequest request);
    Task<Outcome<LookupItemFull>> UpdateHelpTopicAsync(Guid id, UpdateNameRequest request);
    Task<Outcome<Done>> DeleteHelpTopicAsync(Guid id);
    Task<object> ListTicketCategoriesAsync(bool includeInactive, CancellationToken ct);
    Task<LookupItemFull> CreateTicketCategoryAsync(NameOnlyRequest request);
    Task<Outcome<LookupItemFull>> UpdateTicketCategoryAsync(Guid id, UpdateNameRequest request);
    Task<Outcome<Done>> DeleteTicketCategoryAsync(Guid id);
    Task<object> ListSlaPlansAsync(bool includeInactive, CancellationToken ct);
    Task<SlaPlanItemFull> CreateSlaPlanAsync(CreateSlaPlanRequest request);
    Task<Outcome<SlaPlanItemFull>> UpdateSlaPlanAsync(Guid id, UpdateSlaPlanRequest request);
    Task<Outcome<Done>> DeleteSlaPlanAsync(Guid id);
}

public sealed class LookupTableService : ILookupTableService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public LookupTableService(AppDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<object> ListHelpTopicsAsync(bool includeInactive, CancellationToken ct)
    {
        var isAdmin = _currentUser.Role == UserRole.Admin;
        var query = _db.HelpTopics.AsQueryable();
        if (!includeInactive || !isAdmin) query = query.Where(h => h.IsActive);

        if (isAdmin)
        {
            var full = await query.OrderBy(h => h.Name).Select(h => new LookupItemFull(h.Id, h.Name, h.IsActive)).ToListAsync(ct);
            return full;
        }
        var items = await query.OrderBy(h => h.Name).Select(h => new LookupItem(h.Id, h.Name)).ToListAsync(ct);
        return items;
    }

    public async Task<LookupItemFull> CreateHelpTopicAsync(NameOnlyRequest request)
    {
        var entity = new HelpTopic { Name = request.Name };
        _db.HelpTopics.Add(entity);
        await _db.SaveChangesAsync();
        return new LookupItemFull(entity.Id, entity.Name, entity.IsActive);
    }

    public async Task<Outcome<LookupItemFull>> UpdateHelpTopicAsync(Guid id, UpdateNameRequest request)
    {
        var entity = await _db.HelpTopics.FindAsync(id);
        if (entity is null) return Outcome<LookupItemFull>.NotFound();
        entity.Name = request.Name;
        entity.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        return Outcome<LookupItemFull>.Ok(new LookupItemFull(entity.Id, entity.Name, entity.IsActive));
    }

    public async Task<Outcome<Done>> DeleteHelpTopicAsync(Guid id)
    {
        var entity = await _db.HelpTopics.FindAsync(id);
        if (entity is null) return Outcome<Done>.NotFound();

        var inUse = await _db.Tickets.CountAsync(t => t.HelpTopicId == id);
        if (inUse > 0) return Outcome<Done>.Invalid(InUseMessage("help topic", inUse));
        _db.HelpTopics.Remove(entity);
        await _db.SaveChangesAsync();
        return Outcome<Done>.Ok(Done.Value);
    }

    public async Task<object> ListTicketCategoriesAsync(bool includeInactive, CancellationToken ct)
    {
        var isAdmin = _currentUser.Role == UserRole.Admin;
        var query = _db.TicketCategories.AsQueryable();
        if (!includeInactive || !isAdmin) query = query.Where(c => c.IsActive);

        if (isAdmin)
        {
            var full = await query.OrderBy(c => c.Name).Select(c => new LookupItemFull(c.Id, c.Name, c.IsActive)).ToListAsync(ct);
            return full;
        }
        var items = await query.OrderBy(c => c.Name).Select(c => new LookupItem(c.Id, c.Name)).ToListAsync(ct);
        return items;
    }

    public async Task<LookupItemFull> CreateTicketCategoryAsync(NameOnlyRequest request)
    {
        var entity = new TicketCategory { Name = request.Name };
        _db.TicketCategories.Add(entity);
        await _db.SaveChangesAsync();
        return new LookupItemFull(entity.Id, entity.Name, entity.IsActive);
    }

    public async Task<Outcome<LookupItemFull>> UpdateTicketCategoryAsync(Guid id, UpdateNameRequest request)
    {
        var entity = await _db.TicketCategories.FindAsync(id);
        if (entity is null) return Outcome<LookupItemFull>.NotFound();
        entity.Name = request.Name;
        entity.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        return Outcome<LookupItemFull>.Ok(new LookupItemFull(entity.Id, entity.Name, entity.IsActive));
    }

    public async Task<Outcome<Done>> DeleteTicketCategoryAsync(Guid id)
    {
        var entity = await _db.TicketCategories.FindAsync(id);
        if (entity is null) return Outcome<Done>.NotFound();

        var inUse = await _db.Tickets.CountAsync(t => t.CategoryId == id);
        if (inUse > 0) return Outcome<Done>.Invalid(InUseMessage("category", inUse));
        _db.TicketCategories.Remove(entity);
        await _db.SaveChangesAsync();
        return Outcome<Done>.Ok(Done.Value);
    }

    public async Task<object> ListSlaPlansAsync(bool includeInactive, CancellationToken ct)
    {
        var isAdmin = _currentUser.Role == UserRole.Admin;
        var query = _db.SlaPlans.AsQueryable();
        if (!includeInactive || !isAdmin) query = query.Where(s => s.IsActive);

        if (isAdmin)
        {
            var full = await query.OrderBy(s => s.ResolutionTimeHours)
                .Select(s => new SlaPlanItemFull(s.Id, s.Name, s.ResolutionTimeHours, s.IsActive)).ToListAsync(ct);
            return full;
        }
        var items = await query.OrderBy(s => s.ResolutionTimeHours)
            .Select(s => new SlaPlanItem(s.Id, s.Name, s.ResolutionTimeHours)).ToListAsync(ct);
        return items;
    }

    public async Task<SlaPlanItemFull> CreateSlaPlanAsync(CreateSlaPlanRequest request)
    {
        var entity = new SlaPlan { Name = request.Name, ResolutionTimeHours = request.ResolutionTimeHours };
        _db.SlaPlans.Add(entity);
        await _db.SaveChangesAsync();
        return new SlaPlanItemFull(entity.Id, entity.Name, entity.ResolutionTimeHours, entity.IsActive);
    }

    public async Task<Outcome<SlaPlanItemFull>> UpdateSlaPlanAsync(Guid id, UpdateSlaPlanRequest request)
    {
        var entity = await _db.SlaPlans.FindAsync(id);
        if (entity is null) return Outcome<SlaPlanItemFull>.NotFound();
        entity.Name = request.Name;
        entity.ResolutionTimeHours = request.ResolutionTimeHours;
        entity.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        return Outcome<SlaPlanItemFull>.Ok(new SlaPlanItemFull(entity.Id, entity.Name, entity.ResolutionTimeHours, entity.IsActive));
    }

    public async Task<Outcome<Done>> DeleteSlaPlanAsync(Guid id)
    {
        var entity = await _db.SlaPlans.FindAsync(id);
        if (entity is null) return Outcome<Done>.NotFound();

        var inUse = await _db.Tickets.CountAsync(t => t.SlaPlanId == id);
        if (inUse > 0) return Outcome<Done>.Invalid(InUseMessage("SLA plan", inUse));

        _db.SlaPlans.Remove(entity);
        await _db.SaveChangesAsync();
        return Outcome<Done>.Ok(Done.Value);
    }

    private static string InUseMessage(string what, int tickets) =>
        $"This {what} is used by {tickets} ticket(s) and cannot be deleted, because those tickets would lose it from their history. " +
        "Deactivate it instead: it then disappears from the pickers, and everything that already uses it stays as it is.";
}
