using System;

namespace EventTicketing.Core.Domain.Entities;

public class CheckInLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TicketId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string StaffId { get; set; } = "GATE_STAFF_1";
    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "SUCCESS";
    public string? Reason { get; set; }
}
