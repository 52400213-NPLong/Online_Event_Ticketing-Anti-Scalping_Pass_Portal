using EventTicketing.Core.Domain.Entities;

namespace EventTicketing.Core.Interfaces;

public interface ITicketBookingService
{
    Task<BookingResult> ReserveSeatAsync(string userId, Guid seatId, CancellationToken cancellationToken = default);
    Task<SeatHoldInfo?> GetHoldByIdAsync(Guid holdId, CancellationToken cancellationToken = default);
    Task<bool> ConfirmSeatHoldAsync(Guid holdId, CancellationToken cancellationToken = default);
}
