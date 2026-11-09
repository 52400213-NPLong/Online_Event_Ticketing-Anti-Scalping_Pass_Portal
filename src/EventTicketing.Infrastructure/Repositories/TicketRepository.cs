using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using EventTicketing.Core.Domain.Entities;
using EventTicketing.Core.Interfaces;
using EventTicketing.Infrastructure.Data;

namespace EventTicketing.Infrastructure.Repositories;

public class TicketRepository : ITicketRepository
{
    private readonly ApplicationDbContext _db;

    public TicketRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<Ticket?> GetByIdAsync(Guid ticketId, CancellationToken cancellationToken = default)
        => _db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId, cancellationToken);

    public async Task<IReadOnlyList<Ticket>> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _db.Tickets
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Ticket> AddAsync(Ticket ticket, CancellationToken cancellationToken = default)
    {
        _db.Tickets.Add(ticket);
        await _db.SaveChangesAsync(cancellationToken);
        return ticket;
    }

    public async Task UpdateAsync(Ticket ticket, CancellationToken cancellationToken = default)
    {
        _db.Tickets.Update(ticket);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
