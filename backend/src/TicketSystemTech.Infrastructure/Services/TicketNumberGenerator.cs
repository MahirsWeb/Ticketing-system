using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Infrastructure.Services;

public class TicketNumberGenerator : ITicketNumberGenerator
{
    private readonly AppDbContext _db;

    public TicketNumberGenerator(AppDbContext db)
    {
        _db = db;
    }

    public async Task<string> NextAsync(CancellationToken ct = default)
    {
        var result = await _db.Database
            .SqlQueryRaw<long>("SELECT nextval('\"TicketNumberSequence\"') AS \"Value\"")
            .ToListAsync(ct);
        return result[0].ToString();
    }
}
