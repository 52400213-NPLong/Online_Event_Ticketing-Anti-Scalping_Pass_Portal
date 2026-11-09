using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventTicketing.Core.Domain.Entities;

namespace EventTicketing.Core.Interfaces;

public interface ITicketBookingService
{
    Task<BookingResult> ReserveSeatAsync(string userId, Guid seatId, CancellationToken cancellationToken = default);
    Task<BatchBookingResult> ReserveSeatsAsync(string userId, IReadOnlyList<Guid> seatIds, CancellationToken cancellationToken = default);
    Task<SeatHoldInfo?> GetHoldByIdAsync(Guid holdId, CancellationToken cancellationToken = default);
    Task<bool> ConfirmSeatHoldAsync(Guid holdId, CancellationToken cancellationToken = default);
    Task<Ticket?> ConfirmAndIssueTicketAsync(Guid holdId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Ticket>> ConfirmAndIssueBatchTicketsAsync(IReadOnlyList<Guid> holdIds, CancellationToken cancellationToken = default);
    Task<bool> CancelSeatHoldAsync(Guid holdId, string userId, CancellationToken cancellationToken = default);
    Task<bool> CancelBatchSeatHoldsAsync(IReadOnlyList<Guid> holdIds, string userId, CancellationToken cancellationToken = default);
    Task<int> ReleaseExpiredHoldsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Ticket>> GetUserTicketsAsync(string userId, CancellationToken cancellationToken = default);
}
