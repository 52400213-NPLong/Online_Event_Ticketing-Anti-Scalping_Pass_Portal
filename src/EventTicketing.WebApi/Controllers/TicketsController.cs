using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using EventTicketing.Core.Constants;
using EventTicketing.Core.Interfaces;

namespace EventTicketing.WebApi.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class TicketsController : ControllerBase
    {
        private readonly IDynamicQrService _qrService;
        private readonly ITicketBookingService _bookingService;

        public TicketsController(IDynamicQrService qrService, ITicketBookingService bookingService)
        {
            _qrService = qrService;
            _bookingService = bookingService;
        }

        [HttpPost("hold")]
        public async Task<IActionResult> HoldSeat([FromBody] HoldSeatRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.UserId) || request.SeatId == Guid.Empty)
            {
                return BadRequest(new { message = "UserId and SeatId are required." });
            }

            var result = await _bookingService.ReserveSeatAsync(request.UserId, request.SeatId, cancellationToken);
            if (!result.IsSuccess)
            {
                return Conflict(new { message = result.ErrorMessage });
            }

            return Ok(new
            {
                holdId = result.HoldId,
                message = $"Seat held successfully for {AntiScalpingConfig.HOLD_DURATION_MINUTES} minutes."
            });
        }

        [HttpGet("dynamic-qr")]
        public IActionResult GetDynamicQr([FromQuery] Guid ticketId, [FromQuery] string userId)
        {
            if (ticketId == Guid.Empty || string.IsNullOrWhiteSpace(userId))
            {
                return BadRequest(new { message = "ticketId and userId are required." });
            }

            var token = _qrService.GenerateDynamicToken(ticketId, userId);
            return Ok(new
            {
                ticketId,
                userId,
                dynamicToken = token,
                expiresInSeconds = AntiScalpingConfig.DYNAMIC_QR_VALIDITY_SECONDS
            });
        }
    }

    public record HoldSeatRequest(string UserId, Guid SeatId);
}