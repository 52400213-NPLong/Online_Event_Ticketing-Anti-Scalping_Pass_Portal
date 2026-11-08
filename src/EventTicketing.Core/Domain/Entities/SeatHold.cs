using System;

namespace EventTicketing.Core.Domain.Entities;

public record SeatHoldInfo(Guid HoldId, Guid SeatId, string UserId, DateTime ExpiresAt);

public record BookingResult(bool IsSuccess, string? ErrorMessage, Guid? HoldId)
{
    public static BookingResult Success(Guid holdId) => new(true, null, holdId);
    public static BookingResult Failed(string error) => new(false, error, null);
}