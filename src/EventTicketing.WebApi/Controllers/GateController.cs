using System;
using Microsoft.AspNetCore.Mvc;
using EventTicketing.Core.Interfaces;

namespace EventTicketing.WebApi.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class GateController : ControllerBase
    {
        private readonly IDynamicQrService _qrService;

        public GateController(IDynamicQrService qrService)
        {
            _qrService = qrService;
        }

        [HttpPost("checkin")]
        public IActionResult VerifyCheckIn([FromBody] CheckInRequest request)
        {
            if (request.TicketId == Guid.Empty || string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.DynamicToken))
            {
                return BadRequest(new { status = "REJECTED", message = "TicketId, UserId and DynamicToken are required." });
            }

            var isValid = _qrService.ValidateToken(request.TicketId, request.UserId, request.DynamicToken);
            if (!isValid)
            {
                return BadRequest(new { status = "REJECTED", message = "Invalid or expired QR code." });
            }

            return Ok(new { status = "SUCCESS", message = "Check-in confirmed." });
        }
    }

    public record CheckInRequest(Guid TicketId, string UserId, string DynamicToken);
}