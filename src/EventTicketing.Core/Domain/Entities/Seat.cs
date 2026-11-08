using System;
using EventTicketing.Core.Domain.Enums;

namespace EventTicketing.Core.Domain.Entities;

public class Seat
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    public string SeatLabel { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public SeatStatus Status { get; set; } = SeatStatus.Available;
    public string? HeldByUserId { get; set; }
    public DateTime? HoldExpiresAt { get; set; }
}
