using EventTicketing.Core.Domain.Entities;

namespace EventTicketing.Core.Interfaces;

public interface ITicketRepository
{
    Task<Ticket?> GetByIdAsync(Guid ticketId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Ticket>> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<Ticket> AddAsync(Ticket ticket, CancellationToken cancellationToken = default);
    Task UpdateAsync(Ticket ticket, CancellationToken cancellationToken = default);
}
