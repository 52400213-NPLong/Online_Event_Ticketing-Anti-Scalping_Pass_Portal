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
        return Ok(_db.Events.Select(e => new
        {
            e.Id,
            e.Title,
            e.Venue,
            e.StartTime,
            e.CreatedAt
        }));
    }

    [HttpGet("{eventId:guid}/seats")]
    public IActionResult GetSeats(Guid eventId)
    {
        var seats = _db.Seats
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

        return Ok(seats);
    }
}
