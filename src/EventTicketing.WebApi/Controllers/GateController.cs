using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using EventTicketing.Core.Domain.Entities;
using EventTicketing.Core.Domain.Enums;
using EventTicketing.Core.Interfaces;
using EventTicketing.Infrastructure.Data;
using EventTicketing.Infrastructure.Security;

namespace EventTicketing.WebApi.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class GateController : ControllerBase
    {
        private readonly IDynamicQrService _qrService;
        private readonly ApplicationDbContext _db;
        private readonly JwtTokenGenerator _jwtValidator;
        private static readonly object GateLock = new();
        private static PresentedQrDto? _latestPresentedQr;

        public GateController(IDynamicQrService qrService, ApplicationDbContext db, JwtTokenGenerator? jwtValidator = null)
        {
            _qrService = qrService;
            _db = db;
            _jwtValidator = jwtValidator ?? new JwtTokenGenerator();
        }

        /// <summary>
        /// Khách hàng phát tín hiệu xuất trình mã QR lên hệ thống máy chủ (Hỗ trợ quét chéo giữa các tab / thiết bị)
        /// </summary>
        [HttpPost("present")]
        public IActionResult PresentTicket([FromBody] PresentedQrDto request)
        {
            _latestPresentedQr = request;
            return Ok(new { message = "Đã phát tín hiệu xuất trình vé lên hệ thống cổng soát vé." });
        }

        /// <summary>
        /// Nhân viên cổng lấy thông tin vé vừa được khách xuất trình gần nhất
        /// </summary>
        [HttpGet("latest-presented")]
        public IActionResult GetLatestPresented()
        {
            if (_latestPresentedQr is null)
            {
                return NotFound(new { message = "Chưa có khách nào phát tín hiệu xuất trình vé." });
            }
            return Ok(_latestPresentedQr);
        }

        /// <summary>
        /// Quét và xác thực vé tại cổng sự kiện bằng mã QR động (Chỉ dành cho vai trò GATE_STAFF hoặc ADMIN - FR-03)
        /// </summary>
        [HttpPost("checkin")]
        public IActionResult VerifyCheckIn([FromBody] CheckInRequest request)
        {
            // Kiểm tra phân quyền Role nếu request gửi kèm JWT Bearer Token từ trình duyệt
            var authHeader = HttpContext?.Request?.Headers.Authorization.ToString();
            if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var tokenStr = authHeader["Bearer ".Length..].Trim();
                var principal = _jwtValidator.ValidateToken(tokenStr);
                var callerRole = principal?.FindFirst(ClaimTypes.Role)?.Value;
                if (principal is null || (callerRole != "GATE_STAFF" && callerRole != "ADMIN"))
                {
                    return StatusCode(403, new
                    {
                        status = "REJECTED",
                        code = "FORBIDDEN_ROLE",
                        message = "Truy cập bị từ chối: Chỉ Nhân viên soát vé (GATE_STAFF) hoặc Quản trị viên (ADMIN) mới có quyền quét vé tại cổng!"
                    });
                }
            }
            if (request.TicketId == Guid.Empty || string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.DynamicToken))
            {
                return BadRequest(new { status = "REJECTED", message = "TicketId, UserId and DynamicToken are required." });
            }

            lock (GateLock)
            {
                var ticket = _db.Tickets.FirstOrDefault(t => t.Id == request.TicketId);
                if (ticket is null)
                {
                    _db.CheckInLogs.Add(new CheckInLog
                    {
                        TicketId = request.TicketId,
                        UserId = request.UserId,
                        StaffId = request.StaffId ?? "GATE_STAFF",
                        Status = "REJECTED",
                        Reason = "Ticket not found in system."
                    });
                    _db.SaveChanges();

                    return BadRequest(new { status = "REJECTED", message = "Không tìm thấy vé trong hệ thống." });
                }

                // 1. Kiểm tra vé đã dùng chưa (Chống tái sử dụng mã QR nhiều lần)
                if (ticket.Status == TicketStatus.Used)
                {
                    _db.CheckInLogs.Add(new CheckInLog
                    {
                        TicketId = request.TicketId,
                        UserId = request.UserId,
                        StaffId = request.StaffId ?? "GATE_STAFF",
                        Status = "REJECTED",
                        Reason = "Ticket has already been checked in (Replay/Screenshot attack detected)."
                    });
                    _db.SaveChanges();

                    return Conflict(new
                    {
                        status = "REJECTED",
                        code = "ALREADY_USED",
                        message = "Vé này đã được check-in trước đó! Nghi vấn vé bị chia sẻ hoặc chụp lại màn hình trái phép."
                    });
                }

                if (ticket.Status == TicketStatus.Cancelled)
                {
                    return BadRequest(new { status = "REJECTED", message = "Vé đã bị hủy." });
                }

                // 1b. Kiểm tra vé định danh chính chủ (Chống chuyển nhượng vé chợ đen)
                if (!string.Equals(ticket.UserId, request.UserId, StringComparison.OrdinalIgnoreCase))
                {
                    _db.CheckInLogs.Add(new CheckInLog
                    {
                        TicketId = request.TicketId,
                        UserId = request.UserId,
                        StaffId = request.StaffId ?? "GATE_STAFF",
                        Status = "REJECTED",
                        Reason = $"Ticket owner mismatch (Expected {ticket.UserId}, got {request.UserId})."
                    });
                    _db.SaveChanges();

                    return BadRequest(new
                    {
                        status = "REJECTED",
                        code = "NOT_TICKET_OWNER",
                        message = "Vé định danh không khớp với tài khoản người xuất trình (Chống sang nhượng vé chợ đen)."
                    });
                }

                // 2. Xác thực Dynamic Token (HMAC-SHA256 theo window 30 giây)
                var isValid = _qrService.ValidateToken(request.TicketId, request.UserId, request.DynamicToken);
                if (!isValid)
                {
                    _db.CheckInLogs.Add(new CheckInLog
                    {
                        TicketId = request.TicketId,
                        UserId = request.UserId,
                        StaffId = request.StaffId ?? "GATE_STAFF",
                        Status = "REJECTED",
                        Reason = "Invalid or expired QR token."
                    });
                    _db.SaveChanges();

                    return BadRequest(new
                    {
                        status = "REJECTED",
                        code = "TOKEN_EXPIRED_OR_INVALID",
                        message = "Mã QR đã hết hạn (chu kỳ 30s) hoặc không chính chủ. Vui lòng mở lại ví vé để lấy mã mới nhất."
                    });
                }

                // 3. Đánh dấu vé đã check-in hợp lệ và ghi CheckInLog trong cùng 1 Transaction ACID trên SQL Server (Activity Diagram 1.2)
                using var transaction = _db.Database.BeginTransaction();
                ticket.Status = TicketStatus.Used;

                var seat = _db.Seats.FirstOrDefault(s => s.Id == ticket.SeatId);
                var ev = seat is not null ? _db.Events.FirstOrDefault(e => e.Id == seat.EventId) : null;
                User? holder = Guid.TryParse(ticket.UserId, out var holderGuid)
                    ? _db.Users.FirstOrDefault(u => u.Id == holderGuid)
                    : null;

                _db.CheckInLogs.Add(new CheckInLog
                {
                    TicketId = request.TicketId,
                    UserId = request.UserId,
                    StaffId = request.StaffId ?? "GATE_STAFF",
                    Status = "SUCCESS",
                    Reason = "Passed dynamic QR gate verification."
                });
                _db.SaveChanges();
                transaction.Commit();

                return Ok(new
                {
                    status = "SUCCESS",
                    ticketId = ticket.Id,
                    holderName = holder?.FullName ?? ticket.UserId,
                    seatLabel = seat?.SeatLabel ?? "N/A",
                    eventName = ev?.Title ?? "Event",
                    verifiedAt = DateTime.UtcNow,
                    message = "Check-in thành công! Chào mừng quý khách vào khán phòng."
                });
            }
        }

        /// <summary>
        /// Lịch sử quét soát vé tại cổng
        /// </summary>
        [HttpGet("logs")]
        public IActionResult GetLogs()
        {
            var logs = _db.CheckInLogs.OrderByDescending(l => l.ScannedAt).Take(50).ToList();
            return Ok(logs);
        }
    }

    public record CheckInRequest(Guid TicketId, string UserId, string DynamicToken, string? StaffId = "GATE_STAFF");
    public record PresentedQrDto(Guid TicketId, string UserId, string Token, string? SeatLabel, string? EventName, string? HolderName);
}
