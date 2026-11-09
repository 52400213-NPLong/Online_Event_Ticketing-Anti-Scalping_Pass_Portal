using System;
using System.Linq;
using System.Threading.Tasks;
using EventTicketing.Core.Constants;
using EventTicketing.Core.Domain.Entities;
using EventTicketing.Core.Domain.Enums;
using EventTicketing.Infrastructure.Data;
using EventTicketing.Infrastructure.Services;
using Xunit;

namespace EventTicketing.Tests.UnitTests;

public class TicketBookingTests
{
    [Fact]
    public async Task ReserveSeatAsync_WhenSeatIsAvailable_ReturnsSuccess()
    {
        using var db = new ApplicationDbContext();
        var seat = new Seat
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            SeatLabel = "A10",
            Price = 99m,
            Status = SeatStatus.Available
        };
        db.Seats.Add(seat);
        db.SaveChanges();

        var service = new TicketBookingService(db);

        var result = await service.ReserveSeatAsync("USER-42", seat.Id);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.HoldId);

        var updatedSeat = db.Seats.First(s => s.Id == seat.Id);
        Assert.Equal(SeatStatus.Held, updatedSeat.Status);
        Assert.Equal("USER-42", updatedSeat.HeldByUserId);
        Assert.NotNull(updatedSeat.HoldExpiresAt);
    }

    [Fact]
    public async Task ReserveSeatAsync_WhenSeatIsHeld_ReturnsFailure()
    {
        using var db = new ApplicationDbContext();
        var seat = new Seat
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            SeatLabel = "B2",
            Price = 140m,
            Status = SeatStatus.Held,
            HoldExpiresAt = DateTime.UtcNow.AddMinutes(5)
        };
        db.Seats.Add(seat);
        db.SaveChanges();

        var service = new TicketBookingService(db);

        var result = await service.ReserveSeatAsync("USER-99", seat.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("not available", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReserveSeatAsync_WhenUserExceedsAntiScalpingLimit_ReturnsFailure()
    {
        using var db = new ApplicationDbContext();
        var userId = "SCALPER_USER_01";

        // Tạo sẵn 4 vé cho người dùng này (đạt ngưỡng MAX_TICKETS_PER_USER = 4)
        for (int i = 0; i < AntiScalpingConfig.MAX_TICKETS_PER_USER; i++)
        {
            db.Tickets.Add(new Ticket
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SeatId = Guid.NewGuid(),
                Status = TicketStatus.Issued
            });
        }

        var newSeat = new Seat
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            SeatLabel = "C1",
            Price = 200m,
            Status = SeatStatus.Available
        };
        db.Seats.Add(newSeat);
        db.SaveChanges();

        var service = new TicketBookingService(db);
        var result = await service.ReserveSeatAsync(userId, newSeat.Id);

        // Quy tắc chống đầu cơ phải kích hoạt chặn đơn hàng thứ 5
        Assert.False(result.IsSuccess);
        Assert.Contains("Anti-Scalping violation", result.ErrorMessage);
    }

    [Fact]
    public async Task ConfirmSeatHoldAsync_ValidHold_IssuesTicketAndMarksSeatSold()
    {
        using var db = new ApplicationDbContext();
        var userId = "USER-LEGIT";
        var seat = new Seat
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            SeatLabel = "VIP-1",
            Price = 500m,
            Status = SeatStatus.Available
        };
        db.Seats.Add(seat);
        db.SaveChanges();

        var service = new TicketBookingService(db);
        var reserveResult = await service.ReserveSeatAsync(userId, seat.Id);
        Assert.True(reserveResult.IsSuccess);
        var holdId = reserveResult.HoldId!.Value;

        // Xác nhận đặt vé và thanh toán
        var ticket = await service.ConfirmAndIssueTicketAsync(holdId);

        Assert.NotNull(ticket);
        Assert.Equal(userId, ticket.UserId);
        Assert.Equal(seat.Id, ticket.SeatId);
        Assert.Equal(TicketStatus.Issued, ticket.Status);

        var updatedSeat = db.Seats.First(s => s.Id == seat.Id);
        Assert.Equal(SeatStatus.Sold, updatedSeat.Status);
        Assert.False(db.SeatHolds.Any(h => h.Id == holdId));
    }

    [Fact]
    public async Task ReleaseExpiredHoldsAsync_WhenHoldExpired_RevertsSeatToAvailable()
    {
        using var db = new ApplicationDbContext();
        var seat = new Seat
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            SeatLabel = "EXPIRED-SEAT",
            Price = 100m,
            Status = SeatStatus.Held,
            HeldByUserId = "USER-TIMEOUT",
            HoldExpiresAt = DateTime.UtcNow.AddMinutes(-2) // Đã hết hạn 2 phút trước
        };
        db.Seats.Add(seat);

        var holdId = Guid.NewGuid();
        db.SeatHolds.Add(new SeatHold
        {
            Id = holdId,
            SeatId = seat.Id,
            UserId = "USER-TIMEOUT",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-2)
        });
        db.SaveChanges();

        var service = new TicketBookingService(db);
        int releasedCount = await service.ReleaseExpiredHoldsAsync();

        Assert.True(releasedCount >= 1);
        var updatedSeat = db.Seats.First(s => s.Id == seat.Id);
        Assert.Equal(SeatStatus.Available, updatedSeat.Status);
        Assert.Null(updatedSeat.HeldByUserId);
        Assert.Null(updatedSeat.HoldExpiresAt);
        Assert.False(db.SeatHolds.Any(h => h.Id == holdId));
    }

    [Fact]
    public async Task ReserveSeatsAsync_BatchFourSeats_SucceedsAndHoldsAllFour()
    {
        using var db = new ApplicationDbContext();
        var eventId = Guid.NewGuid();
        var seats = Enumerable.Range(1, 4).Select(i => new Seat
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            SeatLabel = $"A{i}",
            Price = 1200000m,
            Status = SeatStatus.Available
        }).ToList();

        db.Seats.AddRange(seats);
        db.SaveChanges();

        var service = new TicketBookingService(db);
        var result = await service.ReserveSeatsAsync("USER-BATCH-4", seats.Select(s => s.Id).ToList());

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.HoldIds.Count);
        Assert.All(db.Seats.ToList(), s => Assert.Equal(SeatStatus.Held, s.Status));
    }

    [Fact]
    public async Task ReserveSeatsAsync_BatchFiveSeats_FailsAntiScalpingLimit()
    {
        using var db = new ApplicationDbContext();
        var eventId = Guid.NewGuid();
        var seats = Enumerable.Range(1, 5).Select(i => new Seat
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            SeatLabel = $"B{i}",
            Price = 890000m,
            Status = SeatStatus.Available
        }).ToList();

        db.Seats.AddRange(seats);
        db.SaveChanges();

        var service = new TicketBookingService(db);
        var result = await service.ReserveSeatsAsync("USER-SCALPER-5", seats.Select(s => s.Id).ToList());

        Assert.False(result.IsSuccess);
        Assert.Contains("Anti-Scalping violation", result.ErrorMessage);
        Assert.All(db.Seats.ToList(), s => Assert.Equal(SeatStatus.Available, s.Status));
    }

    [Fact]
    public async Task ConfirmAndIssueBatchTicketsAsync_FourHolds_IssuesFourTicketsAndMarksSold()
    {
        using var db = new ApplicationDbContext();
        var eventId = Guid.NewGuid();
        var seats = Enumerable.Range(1, 4).Select(i => new Seat
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            SeatLabel = $"C{i}",
            Price = 650000m,
            Status = SeatStatus.Available
        }).ToList();

        db.Seats.AddRange(seats);
        db.SaveChanges();

        var service = new TicketBookingService(db);
        var holdResult = await service.ReserveSeatsAsync("USER-PAY-BATCH", seats.Select(s => s.Id).ToList());
        Assert.True(holdResult.IsSuccess);

        var issuedTickets = await service.ConfirmAndIssueBatchTicketsAsync(holdResult.HoldIds);

        Assert.Equal(4, issuedTickets.Count);
        Assert.All(db.Seats.ToList(), s => Assert.Equal(SeatStatus.Sold, s.Status));
        Assert.Empty(db.SeatHolds.ToList());
    }

    [Fact]
    public async Task CancelBatchSeatHoldsAsync_ValidOwner_RestoresSeatsToAvailable()
    {
        using var db = new ApplicationDbContext();
        var seat1 = new Seat { Id = Guid.NewGuid(), EventId = Guid.NewGuid(), SeatLabel = "D1", Price = 650000m, Status = SeatStatus.Available };
        var seat2 = new Seat { Id = Guid.NewGuid(), EventId = Guid.NewGuid(), SeatLabel = "D2", Price = 650000m, Status = SeatStatus.Available };
        db.Seats.AddRange(seat1, seat2);
        db.SaveChanges();

        var service = new TicketBookingService(db);
        var holdResult = await service.ReserveSeatsAsync("USER-CANCEL", new[] { seat1.Id, seat2.Id });
        Assert.True(holdResult.IsSuccess);

        var cancelled = await service.CancelBatchSeatHoldsAsync(holdResult.HoldIds, "USER-CANCEL");
        Assert.True(cancelled);
        Assert.All(db.Seats.ToList(), s => Assert.Equal(SeatStatus.Available, s.Status));
    }
}
