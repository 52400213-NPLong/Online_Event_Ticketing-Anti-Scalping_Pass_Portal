using System;
using EventTicketing.Core.Domain.Enums;

namespace EventTicketing.Core.Domain.Entities;

public class Ticket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public Guid SeatId { get; set; }
    public string DynamicTokenSecret { get; set; } = string.Empty;
    public TicketStatus Status { get; set; } = TicketStatus.Issued;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
