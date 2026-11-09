using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using EventTicketing.Core.Constants;
using EventTicketing.Core.Domain.Enums;
using EventTicketing.Core.Interfaces;
using EventTicketing.Infrastructure.Data;
using EventTicketing.Infrastructure.Security;
using EventTicketing.WebApi.Hubs;

namespace EventTicketing.WebApi.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class TicketsController : ControllerBase
    {
        private readonly IDynamicQrService _qrService;
        private readonly ITicketBookingService _bookingService;
        private readonly ApplicationDbContext _db;
        private readonly IHubContext<SeatHub> _seatHub;
        private readonly JwtTokenGenerator _jwtValidator;

        public TicketsController(
            IDynamicQrService qrService,
            ITicketBookingService bookingService,
            ApplicationDbContext db,
            IHubContext<SeatHub> seatHub,
            JwtTokenGenerator? jwtValidator = null)
        {
            _qrService = qrService;
            _bookingService = bookingService;
            _db = db;
            _seatHub = seatHub;
            _jwtValidator = jwtValidator ?? new JwtTokenGenerator();
        }

        private bool IsGateStaffBlocked(out IActionResult? forbiddenResult)
        {
            var authHeader = HttpContext?.Request?.Headers.Authorization.ToString();
            if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var tokenStr = authHeader["Bearer ".Length..].Trim();
                var principal = _jwtValidator.ValidateToken(tokenStr);
                var role = principal?.FindFirst(ClaimTypes.Role)?.Value;
                if (role == "GATE_STAFF")
                {
                    forbiddenResult = StatusCode(403, new
                    {
                        code = "FORBIDDEN_ROLE",
                        message = "Tài khoản Nhân viên soát vé (GATE_STAFF) chỉ dùng để kiểm soát vé tại cổng, không được phép đặt mua vé."
                    });
                    return true;
                }
            }

            forbiddenResult = null;
            return false;
        }

        /// <summary>
        /// 1. Tạm giữ chỗ 1 ghế trong 10 phút
        /// </summary>
        [HttpPost("hold")]
        public async Task<IActionResult> HoldSeat([FromBody] HoldSeatRequest request, CancellationToken cancellationToken)
        {
            if (IsGateStaffBlocked(out var forbidden)) return forbidden!;
            if (string.IsNullOrWhiteSpace(request.UserId) || request.SeatId == Guid.Empty)
            {
                return BadRequest(new { message = "UserId and SeatId are required." });
            }

            var result = await _bookingService.ReserveSeatAsync(request.UserId, request.SeatId, cancellationToken);
            if (!result.IsSuccess)
            {
                return Conflict(new { message = result.ErrorMessage });
            }

            var seat = _db.Seats.FirstOrDefault(s => s.Id == request.SeatId);
            if (seat is not null)
            {
                await _seatHub.Clients.All.SendAsync("SeatUpdated", new
                {
                    seatId = seat.Id,
                    seatLabel = seat.SeatLabel,
                    status = "Held",
                    heldBy = request.UserId
                }, cancellationToken);
            }

            return Ok(new
            {
                holdId = result.HoldId,
                seatId = request.SeatId,
                holdDurationMinutes = AntiScalpingConfig.HOLD_DURATION_MINUTES,
                message = $"Seat held successfully for {AntiScalpingConfig.HOLD_DURATION_MINUTES} minutes."
            });
        }

        /// <summary>
        /// 1b. Tạm giữ chỗ nhiều ghế cùng lúc (Tối đa 4 ghế theo quy tắc chống đầu cơ vé)
        /// </summary>
        [HttpPost("hold-batch")]
        public async Task<IActionResult> HoldSeatsBatch([FromBody] HoldSeatsBatchRequest request, CancellationToken cancellationToken)
        {
            if (IsGateStaffBlocked(out var forbidden)) return forbidden!;
            if (string.IsNullOrWhiteSpace(request.UserId) || request.SeatIds == null || request.SeatIds.Count == 0)
            {
                return BadRequest(new { message = "UserId and at least one SeatId are required." });
            }

            var result = await _bookingService.ReserveSeatsAsync(request.UserId, request.SeatIds, cancellationToken);
            if (!result.IsSuccess)
            {
                return Conflict(new { message = result.ErrorMessage });
            }

            await _seatHub.Clients.All.SendAsync("SeatsRefreshed", cancellationToken);

            return Ok(new
            {
                holdIds = result.HoldIds,
                seatIds = request.SeatIds,
                count = result.HoldIds.Count,
                holdDurationMinutes = AntiScalpingConfig.HOLD_DURATION_MINUTES,
                message = $"Đã giữ thành công {result.HoldIds.Count} ghế trong {AntiScalpingConfig.HOLD_DURATION_MINUTES} phút."
            });
        }

        /// <summary>
        /// 2. Xác nhận đặt vé cho 1 ghế
        /// </summary>
        [HttpPost("confirm")]
        public async Task<IActionResult> ConfirmBooking([FromBody] ConfirmBookingRequest request, CancellationToken cancellationToken)
        {
            if (request.HoldId == Guid.Empty)
            {
                return BadRequest(new { message = "HoldId is required." });
            }

            var ticket = await _bookingService.ConfirmAndIssueTicketAsync(request.HoldId, cancellationToken);
            if (ticket is null)
            {
                return BadRequest(new { message = "Thời gian giữ chỗ đã hết hạn hoặc không hợp lệ. Vui lòng chọn lại ghế." });
            }

            var seat = _db.Seats.FirstOrDefault(s => s.Id == ticket.SeatId);
            if (seat is not null)
            {
                await _seatHub.Clients.All.SendAsync("SeatUpdated", new
                {
                    seatId = seat.Id,
                    seatLabel = seat.SeatLabel,
                    status = "Sold"
                }, cancellationToken);
            }

            return Ok(new
            {
                ticketId = ticket.Id,
                userId = ticket.UserId,
                seatId = ticket.SeatId,
                seatLabel = seat?.SeatLabel ?? "N/A",
                status = ticket.Status.ToString(),
                createdAt = ticket.CreatedAt,
                message = "Vé đã được thanh toán và phát hành thành công!"
            });
        }

        /// <summary>
        /// 2b. Xác nhận đặt vé hàng loạt (Thanh toán nhiều vé đã giữ chỗ)
        /// </summary>
        [HttpPost("confirm-batch")]
        public async Task<IActionResult> ConfirmBookingBatch([FromBody] ConfirmBookingBatchRequest request, CancellationToken cancellationToken)
        {
            if (request.HoldIds == null || request.HoldIds.Count == 0)
            {
                return BadRequest(new { message = "Danh sách HoldIds không được trống." });
            }

            var tickets = await _bookingService.ConfirmAndIssueBatchTicketsAsync(request.HoldIds, cancellationToken);
            if (tickets.Count == 0)
            {
                return BadRequest(new { message = "Tất cả các ghế giữ chỗ đã hết hạn. Vui lòng chọn lại ghế." });
            }

            await _seatHub.Clients.All.SendAsync("SeatsRefreshed", cancellationToken);

            return Ok(new
            {
                count = tickets.Count,
                ticketIds = tickets.Select(t => t.Id).ToList(),
                message = $"Đã thanh toán và phát hành thành công {tickets.Count} vé điện tử!"
            });
        }

        /// <summary>
        /// 3. Hủy giữ chỗ ghế
        /// </summary>
        [HttpPost("cancel-hold")]
        public async Task<IActionResult> CancelHold([FromBody] CancelHoldRequest request, CancellationToken cancellationToken)
        {
            if (request.HoldId == Guid.Empty || string.IsNullOrWhiteSpace(request.UserId))
            {
                return BadRequest(new { message = "HoldId and UserId are required." });
            }

            var success = await _bookingService.CancelSeatHoldAsync(request.HoldId, request.UserId, cancellationToken);
            if (!success)
            {
                return BadRequest(new { message = "Failed to cancel hold or hold does not belong to user." });
            }

            await _seatHub.Clients.All.SendAsync("SeatsRefreshed", cancellationToken);
            return Ok(new { message = "Seat hold cancelled successfully." });
        }

        /// <summary>
        /// 3b. Hủy giữ chỗ nhiều ghế cùng lúc
        /// </summary>
        [HttpPost("cancel-hold-batch")]
        public async Task<IActionResult> CancelHoldBatch([FromBody] CancelHoldBatchRequest request, CancellationToken cancellationToken)
        {
            if (request.HoldIds == null || request.HoldIds.Count == 0 || string.IsNullOrWhiteSpace(request.UserId))
            {
                return BadRequest(new { message = "HoldIds and UserId are required." });
            }

            var success = await _bookingService.CancelBatchSeatHoldsAsync(request.HoldIds, request.UserId, cancellationToken);
            await _seatHub.Clients.All.SendAsync("SeatsRefreshed", cancellationToken);
            return Ok(new { message = "Đã hủy giữ chỗ thành công.", success });
        }

        /// <summary>
        /// 4. Sinh mã QR động (Anti-Scalping Dynamic Token) làm mới mỗi 30 giây
        /// </summary>
        [HttpGet("dynamic-qr")]
        public IActionResult GetDynamicQr([FromQuery] Guid ticketId, [FromQuery] string userId)
        {
            if (ticketId == Guid.Empty || string.IsNullOrWhiteSpace(userId))
            {
                return BadRequest(new { message = "ticketId and userId are required." });
            }

            var ticket = _db.Tickets.FirstOrDefault(t => t.Id == ticketId);
            if (ticket is null)
            {
                return NotFound(new { message = "Ticket not found." });
            }

            if (!string.Equals(ticket.UserId, userId, StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(403, new
                {
                    code = "NOT_TICKET_OWNER",
                    message = "Anti-Scalping violation: Vé định danh không thuộc quyền sở hữu của tài khoản này (Chống sang nhượng vé trái phép)."
                });
            }

            if (ticket.Status == TicketStatus.Used)
            {
                return BadRequest(new { message = "Ticket has already been used.", status = "USED" });
            }

            var token = _qrService.GenerateDynamicToken(ticketId, userId);
            return Ok(new
            {
                ticketId,
                userId,
                dynamicToken = token,
                expiresInSeconds = AntiScalpingConfig.DYNAMIC_QR_VALIDITY_SECONDS,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        /// <summary>
        /// 5. Lấy danh sách vé trong ví của người dùng
        /// </summary>
        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetUserTickets(string userId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return BadRequest(new { message = "UserId is required." });
            }

            var tickets = await _bookingService.GetUserTicketsAsync(userId, cancellationToken);
            var result = tickets.Select(t =>
            {
                var seat = _db.Seats.FirstOrDefault(s => s.Id == t.SeatId);
                var ev = seat is not null ? _db.Events.FirstOrDefault(e => e.Id == seat.EventId) : null;
                return new
                {
                    ticketId = t.Id,
                    userId = t.UserId,
                    seatId = t.SeatId,
                    seatLabel = seat?.SeatLabel ?? "N/A",
                    price = seat?.Price ?? 0m,
                    eventName = ev?.Title ?? "Event",
                    venue = ev?.Venue ?? "Venue",
                    eventDate = ev?.StartTime,
                    status = t.Status.ToString(),
                    createdAt = t.CreatedAt
                };
            });

            return Ok(result);
        }
    }

    public record HoldSeatRequest(string UserId, Guid SeatId);
    public record HoldSeatsBatchRequest(string UserId, List<Guid> SeatIds);
    public record ConfirmBookingRequest(Guid HoldId);
    public record ConfirmBookingBatchRequest(List<Guid> HoldIds);
    public record CancelHoldRequest(Guid HoldId, string UserId);
    public record CancelHoldBatchRequest(List<Guid> HoldIds, string UserId);
}
