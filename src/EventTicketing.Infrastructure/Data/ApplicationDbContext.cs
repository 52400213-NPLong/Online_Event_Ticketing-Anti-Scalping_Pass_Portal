using EventTicketing.Core.Domain.Entities;
using EventTicketing.Core.Domain.Enums;

namespace EventTicketing.Infrastructure.Data;

public class ApplicationDbContext
{
    public List<User> Users { get; } = new();
    public List<Event> Events { get; } = new();
    public List<Seat> Seats { get; } = new();
    public List<Ticket> Tickets { get; } = new();
    public Dictionary<Guid, SeatHoldInfo> SeatHolds { get; } = new();

    public void SeedSampleData()
    {
        if (Users.Count > 0) return;

        var user = new User
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Email = "buyer@example.com",
            FullName = "Demo Buyer",
            PasswordHash = "demo-hash",
            Role = "CUSTOMER"
        };

        Users.Add(user);

        var eventEntity = new Event
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Title = "Music Festival 2026",
            Venue = "City Arena",
            StartTime = DateTime.UtcNow.AddDays(10)
        };

        Events.Add(eventEntity);

        Seats.AddRange(new[]
        {
            new Seat { Id = Guid.Parse("33333333-3333-3333-3333-333333333333"), EventId = eventEntity.Id, SeatLabel = "A1", Price = 120m, Status = SeatStatus.Available },
            new Seat { Id = Guid.Parse("44444444-4444-4444-4444-444444444444"), EventId = eventEntity.Id, SeatLabel = "A2", Price = 120m, Status = SeatStatus.Available },
            new Seat { Id = Guid.Parse("55555555-5555-5555-5555-555555555555"), EventId = eventEntity.Id, SeatLabel = "A3", Price = 120m, Status = SeatStatus.Available }
        });
    }
}
