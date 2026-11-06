using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Emails;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Lookups;

/// <summary>Companies: the clients' firms. Shared by all staff.</summary>
public interface ICompanyService
{
    Task<List<CompanyItemFull>> ListAsync(bool includeInactive, CancellationToken ct);
    Task<Outcome<CompanyItemFull>> GetAsync(Guid id, CancellationToken ct);
    Task<Outcome<LookupItem>> CreateAsync(CreateCompanyRequest request);
    Task<Outcome<CompanyItemFull>> UpdateAsync(Guid id, UpdateCompanyRequest request);
}

public sealed class CompanyService : ICompanyService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CompanyService(AppDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<List<CompanyItemFull>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var isAdmin = _currentUser.Role == UserRole.Admin;
        var query = _db.Companies.AsQueryable();
        if (!includeInactive || !isAdmin) query = query.Where(c => c.IsActive);

        var items = await query.OrderBy(c => c.Name)
            .Select(c => new CompanyItemFull(c.Id, c.Name, c.Address, c.ContactInfo, c.IsActive)).ToListAsync(ct);
        return items;
    }

    public async Task<Outcome<CompanyItemFull>> GetAsync(Guid id, CancellationToken ct)
    {
        var c = await _db.Companies.FindAsync(new object[] { id }, ct);
        if (c is null) return Outcome<CompanyItemFull>.NotFound();
        return Outcome<CompanyItemFull>.Ok(new CompanyItemFull(c.Id, c.Name, c.Address, c.ContactInfo, c.IsActive));
    }

    // A company name is how staff pick the firm when opening a ticket on someone's behalf and how reports group the work;
    // two firms with the same name would be indistinguishable in every list. Enforced here (there is no unique index, so an
    // older database that already holds duplicates still starts). Compared case-insensitively and across inactive firms too,
    // so a switched-off firm's name cannot quietly be reused. The message is the one the frontend already shows.
    private const string NameTakenMessage = "A company with that name already exists.";

    private Task<bool> NameTakenAsync(string name, Guid? exceptId = null)
    {
        var normalized = (name ?? string.Empty).Trim().ToLower();
        return _db.Companies.AnyAsync(c => c.Name.ToLower() == normalized && (exceptId == null || c.Id != exceptId));
    }

    public async Task<Outcome<LookupItem>> CreateAsync(CreateCompanyRequest request)
    {
        if (await NameTakenAsync(request.Name))
            return Outcome<LookupItem>.Invalid(NameTakenMessage);
        var company = new Company { Name = request.Name, Address = request.Address, ContactInfo = request.ContactInfo };
        _db.Companies.Add(company);
        await _db.SaveChangesAsync();
        return Outcome<LookupItem>.Ok(new LookupItem(company.Id, company.Name));
    }

    public async Task<Outcome<CompanyItemFull>> UpdateAsync(Guid id, UpdateCompanyRequest request)
    {
        var c = await _db.Companies.FindAsync(id);
        if (c is null) return Outcome<CompanyItemFull>.NotFound();
        if (await NameTakenAsync(request.Name, id))
            return Outcome<CompanyItemFull>.Invalid(NameTakenMessage);

        c.Name = request.Name;
        c.Address = request.Address;
        c.ContactInfo = request.ContactInfo;
        c.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        return Outcome<CompanyItemFull>.Ok(new CompanyItemFull(c.Id, c.Name, c.Address, c.ContactInfo, c.IsActive));
    }
}
