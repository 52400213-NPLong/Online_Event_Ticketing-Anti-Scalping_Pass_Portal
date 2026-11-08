using EventTicketing.Core.Constants;
using EventTicketing.Core.Domain.Entities;
using EventTicketing.Core.Domain.Enums;
using EventTicketing.Core.Interfaces;
using EventTicketing.Infrastructure.Data;

namespace EventTicketing.Infrastructure.Services;

public class TicketBookingService : ITicketBookingService
{
    private readonly ApplicationDbContext _db;

    public TicketBookingService(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<BookingResult> ReserveSeatAsync(string userId, Guid seatId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Task.FromResult(BookingResult.Failed("UserId is required."));
        }

        var seat = _db.Seats.FirstOrDefault(s => s.Id == seatId);
        if (seat is null)
        {
            return Task.FromResult(BookingResult.Failed("Seat not found."));
        }

        if (seat.Status != SeatStatus.Available)
        {
            return Task.FromResult(BookingResult.Failed("Seat is not available for holding."));
        }

        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(AntiScalpingConfig.HOLD_DURATION_MINUTES);
        var holdId = Guid.NewGuid();

        seat.Status = SeatStatus.Held;
        seat.HeldByUserId = userId;
        seat.HoldExpiresAt = expiresAt;

        _db.SeatHolds[holdId] = new SeatHoldInfo(holdId, seat.Id, userId, expiresAt);

        return Task.FromResult(BookingResult.Success(holdId));
    }

    public Task<SeatHoldInfo?> GetHoldByIdAsync(Guid holdId, CancellationToken cancellationToken = default)
    {
        _db.SeatHolds.TryGetValue(holdId, out var hold);
        return Task.FromResult(hold);
    }

    public Task<bool> ConfirmSeatHoldAsync(Guid holdId, CancellationToken cancellationToken = default)
    {
        if (!_db.SeatHolds.TryGetValue(holdId, out var hold))
        {
            return Task.FromResult(false);
        }

        var seat = _db.Seats.FirstOrDefault(s => s.Id == hold.SeatId);
        if (seat is null)
        {
            return Task.FromResult(false);
        }

        seat.Status = SeatStatus.Sold;
        seat.HeldByUserId = null;
        seat.HoldExpiresAt = null;
        _db.SeatHolds.Remove(holdId);

        _db.Tickets.Add(new Ticket
        {
            Id = Guid.NewGuid(),
            UserId = hold.UserId,
            SeatId = hold.SeatId,
            Status = TicketStatus.Issued,
            CreatedAt = DateTime.UtcNow
        });

        return Task.FromResult(true);
    }
}
