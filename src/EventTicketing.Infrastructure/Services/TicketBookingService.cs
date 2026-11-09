using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using EventTicketing.Core.Constants;
using EventTicketing.Core.Domain.Entities;
using EventTicketing.Core.Domain.Enums;
using EventTicketing.Core.Interfaces;
using EventTicketing.Infrastructure.Data;

namespace EventTicketing.Infrastructure.Services;

public class TicketBookingService : ITicketBookingService
{
    private readonly ApplicationDbContext _db;
    private static readonly SemaphoreSlim BookingGate = new(1, 1);

    public TicketBookingService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<BookingResult> ReserveSeatAsync(string userId, Guid seatId, CancellationToken cancellationToken = default)
    {
        var batchResult = await ReserveSeatsAsync(userId, new[] { seatId }, cancellationToken);
        if (!batchResult.IsSuccess)
        {
            return BookingResult.Failed(batchResult.ErrorMessage ?? "Reserve seat failed.");
        }

        return BookingResult.Success(batchResult.HoldIds[0]);
    }

    /// <summary>
    /// Giữ chỗ đồng thời bằng Transaction ACID + khóa dòng SQL Server WITH (UPDLOCK, ROWLOCK) kết hợp RowVersion (FR-14, NFR-12)
    /// </summary>
    public async Task<BatchBookingResult> ReserveSeatsAsync(string userId, IReadOnlyList<Guid> seatIds, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return BatchBookingResult.Failed("UserId is required.");
        }

        if (seatIds == null || seatIds.Count == 0)
        {
            return BatchBookingResult.Failed("At least one seat must be selected.");
        }

        if (seatIds.Any(id => id == Guid.Empty))
        {
            return BatchBookingResult.Failed("Invalid SeatId detected.");
        }

        if (seatIds.Distinct().Count() != seatIds.Count)
        {
            return BatchBookingResult.Failed("Duplicate seats are not allowed in the same reservation.");
        }

        await BookingGate.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            try
            {
                var now = DateTime.UtcNow;

                // 1. Chống đầu cơ vé (Anti-Scalping Rule - FR-18): Giới hạn tối đa 4 vé/tài khoản
                int activeTickets = await _db.Tickets.CountAsync(
                    t => t.UserId == userId && t.Status != TicketStatus.Cancelled, cancellationToken);
                int activeHolds = await _db.SeatHolds.CountAsync(
                    h => h.UserId == userId && h.ExpiresAt > now, cancellationToken);
                int totalDesired = activeTickets + activeHolds + seatIds.Count;

                if (totalDesired > AntiScalpingConfig.MAX_TICKETS_PER_USER)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    int remainingAllowed = Math.Max(0, AntiScalpingConfig.MAX_TICKETS_PER_USER - (activeTickets + activeHolds));
                    return BatchBookingResult.Failed(
                        $"Anti-Scalping violation: Maximum {AntiScalpingConfig.MAX_TICKETS_PER_USER} tickets/holds allowed per user. " +
                        $"You already have {activeTickets + activeHolds} tickets/holds, so you can only choose up to {remainingAllowed} more.");
                }

                // 2. Sắp xếp SeatId tăng dần để chống Deadlock khi khóa nhiều dòng cùng lúc bằng WITH (UPDLOCK, ROWLOCK)
                bool isSqlServer = _db.Database.IsSqlServer();
                var orderedSeatIds = seatIds.OrderBy(id => id).ToList();
                var lockedSeatsById = new Dictionary<Guid, Seat>();

                foreach (var seatId in orderedSeatIds)
                {
                    Seat? seat;
                    if (isSqlServer)
                    {
                        // Khóa dòng trực tiếp trên SQL Server theo đúng Sequence Diagram 2.1.1 trong đề cương (FR-14)
                        seat = await _db.Seats
                            .FromSqlInterpolated($"SELECT * FROM [Seats] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {seatId}")
                            .FirstOrDefaultAsync(cancellationToken);
                    }
                    else
                    {
                        seat = await _db.Seats.FirstOrDefaultAsync(s => s.Id == seatId, cancellationToken);
                    }

                    if (seat is null)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return BatchBookingResult.Failed($"Seat with ID {seatId} was not found.");
                    }

                    if (seat.Status == SeatStatus.Sold)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return BatchBookingResult.Failed($"Ghế {seat.SeatLabel} đã được bán. Vui lòng chọn ghế khác.");
                    }

                    // Nếu ghế đang bị giữ chỗ, kiểm tra xem đã quá hạn 10 phút chưa (FR-16)
                    if (seat.Status == SeatStatus.Held)
                    {
                        if (seat.HoldExpiresAt.HasValue && seat.HoldExpiresAt.Value <= now)
                        {
                            var expiredHold = await _db.SeatHolds.FirstOrDefaultAsync(h => h.SeatId == seatId, cancellationToken);
                            if (expiredHold is not null)
                            {
                                _db.SeatHolds.Remove(expiredHold);
                            }
                            seat.Status = SeatStatus.Available;
                            seat.HeldByUserId = null;
                            seat.HoldExpiresAt = null;
                        }
                        else
                        {
                            await transaction.RollbackAsync(cancellationToken);
                            return BatchBookingResult.Failed(
                                $"Seat {seat.SeatLabel} is not available for holding (currently held by another user).");
                        }
                    }

                    lockedSeatsById[seatId] = seat;
                }

                // 3. Thực hiện khóa giữ chỗ theo đúng thứ tự ghế người dùng chọn và Commit Transaction ACID
                var expiresAt = now.AddMinutes(AntiScalpingConfig.HOLD_DURATION_MINUTES);
                var generatedHoldIds = new List<Guid>();

                foreach (var seatId in seatIds)
                {
                    var seat = lockedSeatsById[seatId];
                    var holdId = Guid.NewGuid();

                    seat.Status = SeatStatus.Held;
                    seat.HeldByUserId = userId;
                    seat.HoldExpiresAt = expiresAt;

                    _db.SeatHolds.Add(new SeatHold
                    {
                        Id = holdId,
                        SeatId = seat.Id,
                        UserId = userId,
                        ExpiresAt = expiresAt,
                        CreatedAt = now
                    });

                    generatedHoldIds.Add(holdId);
                }

                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return BatchBookingResult.Success(generatedHoldIds);
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync(cancellationToken);
                return BatchBookingResult.Failed("Concurrency conflict detected (RowVersion changed). Seat is no longer available.");
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        finally
        {
            BookingGate.Release();
        }
    }

    public async Task<SeatHoldInfo?> GetHoldByIdAsync(Guid holdId, CancellationToken cancellationToken = default)
    {
        var hold = await _db.SeatHolds.AsNoTracking().FirstOrDefaultAsync(h => h.Id == holdId, cancellationToken);
        if (hold is null) return null;

        return new SeatHoldInfo(hold.Id, hold.SeatId, hold.UserId, hold.ExpiresAt);
    }

    public async Task<bool> ConfirmSeatHoldAsync(Guid holdId, CancellationToken cancellationToken = default)
    {
        var ticket = await ConfirmAndIssueTicketAsync(holdId, cancellationToken);
        return ticket is not null;
    }

    public async Task<Ticket?> ConfirmAndIssueTicketAsync(Guid holdId, CancellationToken cancellationToken = default)
    {
        var tickets = await ConfirmAndIssueBatchTicketsAsync(new[] { holdId }, cancellationToken);
        return tickets.FirstOrDefault();
    }

    /// <summary>
    /// Xác nhận thanh toán và phát hành vé điện tử trong cùng một Transaction ACID (FR-27, FR-31, NFR-12)
    /// </summary>
    public async Task<IReadOnlyList<Ticket>> ConfirmAndIssueBatchTicketsAsync(IReadOnlyList<Guid> holdIds, CancellationToken cancellationToken = default)
    {
        if (holdIds == null || holdIds.Count == 0)
        {
            return Array.Empty<Ticket>();
        }

        await BookingGate.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            try
            {
                var issuedTickets = new List<Ticket>();
                var now = DateTime.UtcNow;

                foreach (var holdId in holdIds)
                {
                    var hold = await _db.SeatHolds.FirstOrDefaultAsync(h => h.Id == holdId, cancellationToken);
                    if (hold is null)
                    {
                        continue;
                    }

                    if (hold.ExpiresAt <= now)
                    {
                        _db.SeatHolds.Remove(hold);
                        var expiredSeat = await _db.Seats.FirstOrDefaultAsync(s => s.Id == hold.SeatId, cancellationToken);
                        if (expiredSeat is not null && expiredSeat.Status == SeatStatus.Held)
                        {
                            expiredSeat.Status = SeatStatus.Available;
                            expiredSeat.HeldByUserId = null;
                            expiredSeat.HoldExpiresAt = null;
                        }
                        continue;
                    }

                    var seat = await _db.Seats.FirstOrDefaultAsync(s => s.Id == hold.SeatId, cancellationToken);
                    if (seat is null || seat.Status != SeatStatus.Held)
                    {
                        continue;
                    }

                    seat.Status = SeatStatus.Sold;
                    seat.HeldByUserId = null;
                    seat.HoldExpiresAt = null;
                    _db.SeatHolds.Remove(hold);

                    var ticket = new Ticket
                    {
                        Id = Guid.NewGuid(),
                        UserId = hold.UserId,
                        SeatId = hold.SeatId,
                        DynamicTokenSecret = Guid.NewGuid().ToString("N"),
                        Status = TicketStatus.Issued,
                        CreatedAt = now
                    };

                    _db.Tickets.Add(ticket);
                    issuedTickets.Add(ticket);
                }

                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return issuedTickets;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        finally
        {
            BookingGate.Release();
        }
    }

    public Task<bool> CancelSeatHoldAsync(Guid holdId, string userId, CancellationToken cancellationToken = default)
    {
        return CancelBatchSeatHoldsAsync(new[] { holdId }, userId, cancellationToken);
    }

    public async Task<bool> CancelBatchSeatHoldsAsync(IReadOnlyList<Guid> holdIds, string userId, CancellationToken cancellationToken = default)
    {
        await BookingGate.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            bool anyCancelled = false;

            foreach (var holdId in holdIds)
            {
                var hold = await _db.SeatHolds.FirstOrDefaultAsync(h => h.Id == holdId, cancellationToken);
                if (hold is not null && string.Equals(hold.UserId, userId, StringComparison.OrdinalIgnoreCase))
                {
                    var seat = await _db.Seats.FirstOrDefaultAsync(s => s.Id == hold.SeatId, cancellationToken);
                    if (seat is not null && seat.Status == SeatStatus.Held)
                    {
                        seat.Status = SeatStatus.Available;
                        seat.HeldByUserId = null;
                        seat.HoldExpiresAt = null;
                    }

                    _db.SeatHolds.Remove(hold);
                    anyCancelled = true;
                }
            }

            if (anyCancelled)
            {
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return anyCancelled;
        }
        finally
        {
            BookingGate.Release();
        }
    }

    public async Task<int> ReleaseExpiredHoldsAsync(CancellationToken cancellationToken = default)
    {
        await BookingGate.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            var now = DateTime.UtcNow;

            var expiredHolds = await _db.SeatHolds
                .Where(h => h.ExpiresAt <= now)
                .ToListAsync(cancellationToken);

            int releasedCount = 0;
            foreach (var hold in expiredHolds)
            {
                var seat = await _db.Seats.FirstOrDefaultAsync(s => s.Id == hold.SeatId, cancellationToken);
                if (seat is not null && seat.Status == SeatStatus.Held)
                {
                    seat.Status = SeatStatus.Available;
                    seat.HeldByUserId = null;
                    seat.HoldExpiresAt = null;
                    releasedCount++;
                }
                _db.SeatHolds.Remove(hold);
            }

            var danglingSeats = await _db.Seats
                .Where(s => s.Status == SeatStatus.Held && s.HoldExpiresAt.HasValue && s.HoldExpiresAt.Value <= now)
                .ToListAsync(cancellationToken);

            foreach (var seat in danglingSeats)
            {
                seat.Status = SeatStatus.Available;
                seat.HeldByUserId = null;
                seat.HoldExpiresAt = null;
                releasedCount++;
            }

            if (releasedCount > 0 || expiredHolds.Count > 0)
            {
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return releasedCount;
        }
        finally
        {
            BookingGate.Release();
        }
    }

    public async Task<IReadOnlyList<Ticket>> GetUserTicketsAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _db.Tickets
            .AsNoTracking()
            .Where(t => t.UserId.ToLower() == userId.ToLower())
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
