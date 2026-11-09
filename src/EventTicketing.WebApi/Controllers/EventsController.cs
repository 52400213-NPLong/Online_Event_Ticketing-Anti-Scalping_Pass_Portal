using System;
using System.Linq;
using EventTicketing.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;

namespace EventTicketing.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class EventsController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public EventsController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public IActionResult GetEvents()
    {
        lock (_db)
        {
            return Ok(_db.Events.Select(e => new
            {
                e.Id,
                e.Title,
                e.Venue,
                e.StartTime,
                e.CreatedAt
            }).ToList());
        }
    }

    [HttpGet("search")]
    public IActionResult SearchEvents([FromQuery] string? q, [FromQuery] string? venue)
    {
        lock (_db)
        {
            var query = _db.Events.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(e => e.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                         e.Venue.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(venue) && !string.Equals(venue, "all", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(e => e.Venue.Contains(venue, StringComparison.OrdinalIgnoreCase));
            }

            return Ok(query.Select(e => new
            {
                e.Id,
                e.Title,
                e.Venue,
                e.StartTime,
                e.CreatedAt
            }).ToList());
        }
    }

    [HttpGet("{eventId:guid}")]
    public IActionResult GetEventById(Guid eventId)
    {
        lock (_db)
        {
            var ev = _db.Events.FirstOrDefault(e => e.Id == eventId);
            if (ev is null)
            {
                return NotFound(new { message = "Event not found" });
            }

            return Ok(new
            {
                ev.Id,
                ev.Title,
                ev.Venue,
                ev.StartTime,
                ev.CreatedAt
            });
        }
    }

    [HttpGet("{eventId:guid}/seats")]
    public IActionResult GetSeats(Guid eventId)
    {
        lock (_db)
        {
            var rawSeats = _db.Seats
                .Where(s => s.EventId == eventId)
                .Select(s => new
                {
                    s.Id,
                    s.SeatLabel,
                    s.Price,
                    Status = s.Status.ToString(),
                    s.HoldExpiresAt
                })
                .ToList();

            // Sắp xếp chuẩn xác theo Hàng (A -> B -> C -> D) và Cột (1 -> 2 -> ... -> 8)
            var seats = rawSeats
                .OrderBy(s => s.SeatLabel.Length > 0 ? s.SeatLabel[0] : 'Z')
                .ThenBy(s => int.TryParse(s.SeatLabel.Substring(1), out var col) ? col : 0)
                .ToList();

            return Ok(seats);
        }
    }
}
