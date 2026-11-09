using System;
using System.Collections.Generic;

namespace EventTicketing.Core.Domain.Entities;

public class SeatHold
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SeatId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public record SeatHoldInfo(Guid HoldId, Guid SeatId, string UserId, DateTime ExpiresAt);

public record BookingResult(bool IsSuccess, string? ErrorMessage, Guid? HoldId)
{
    public static BookingResult Success(Guid holdId) => new(true, null, holdId);
    public static BookingResult Failed(string error) => new(false, error, null);
}

public record BatchBookingResult(bool IsSuccess, string? ErrorMessage, IReadOnlyList<Guid> HoldIds)
{
    public static BatchBookingResult Success(IReadOnlyList<Guid> holdIds) => new(true, null, holdIds);
    public static BatchBookingResult Failed(string error) => new(false, error, Array.Empty<Guid>());
}
