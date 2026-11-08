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
        var db = new ApplicationDbContext();
        var seat = new Seat
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            SeatLabel = "A10",
            Price = 99m,
            Status = SeatStatus.Available
        };
        db.Seats.Add(seat);

        var service = new TicketBookingService(db);

        var result = await service.ReserveSeatAsync("USER-42", seat.Id);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.HoldId);
        Assert.Equal(SeatStatus.Held, seat.Status);
    }

    [Fact]
    public async Task ReserveSeatAsync_WhenSeatIsHeld_ReturnsFailure()
    {
        var db = new ApplicationDbContext();
        var seat = new Seat
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            SeatLabel = "B2",
            Price = 140m,
            Status = SeatStatus.Held
        };
        db.Seats.Add(seat);

        var service = new TicketBookingService(db);

        var result = await service.ReserveSeatAsync("USER-99", seat.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("not available", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
