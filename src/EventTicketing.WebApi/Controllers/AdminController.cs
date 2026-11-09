using System;
using System.Linq;
using System.Security.Claims;
using EventTicketing.Core.Domain.Entities;
using EventTicketing.Core.Domain.Enums;
using EventTicketing.Infrastructure.Data;
using EventTicketing.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;

namespace EventTicketing.WebApi.Controllers;

/// <summary>
/// API Quản trị Sự kiện & Báo cáo Doanh thu dành riêng cho vai trò ADMIN / Event Manager (UR-11 -> UR-16, FR-43 -> FR-51)
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class AdminController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly JwtTokenGenerator _jwtValidator;

    public AdminController(ApplicationDbContext db, JwtTokenGenerator jwtValidator)
    {
        _db = db;
        _jwtValidator = jwtValidator;
    }

    private bool IsAdminAuthorized(out IActionResult? errorResult)
    {
        var authHeader = HttpContext?.Request?.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            errorResult = Unauthorized(new { message = "Vui lòng đăng nhập tài khoản Quản trị viên (ADMIN)." });
            return false;
        }

        var token = authHeader["Bearer ".Length..].Trim();
        var principal = _jwtValidator.ValidateToken(token);
        var role = principal?.FindFirst(ClaimTypes.Role)?.Value;

        if (principal is null || role != "ADMIN")
        {
            errorResult = StatusCode(403, new { message = "Truy cập bị từ chối: Chức năng này chỉ dành cho Ban tổ chức / Quản trị viên (ADMIN)." });
            return false;
        }

        errorResult = null;
        return true;
    }

    /// <summary>
    /// Lấy số liệu thống kê tổng quan & báo cáo theo từng sự kiện (FR-48 -> FR-51)
    /// </summary>
    [HttpGet("stats")]
    public IActionResult GetDashboardStats()
    {
        if (!IsAdminAuthorized(out var err)) return err!;

        var events = _db.Events.OrderBy(e => e.StartTime).ToList();
        var seats = _db.Seats.ToList();
        var tickets = _db.Tickets.Where(t => t.Status != TicketStatus.Cancelled).ToList();
        var usersCount = _db.Users.Count();

        var seatPriceMap = seats.ToDictionary(s => s.Id, s => s.Price);
        decimal totalRevenue = tickets.Sum(t => seatPriceMap.TryGetValue(t.SeatId, out var price) ? price : 0m);
        int totalSeats = seats.Count;
        int soldSeats = seats.Count(s => s.Status == SeatStatus.Sold);
        int heldSeats = seats.Count(s => s.Status == SeatStatus.Held);
        int checkedInTickets = tickets.Count(t => t.Status == TicketStatus.Used);

        double occupancyRate = totalSeats > 0 ? Math.Round((double)soldSeats / totalSeats * 100, 1) : 0;
        double checkInRate = tickets.Count > 0 ? Math.Round((double)checkedInTickets / tickets.Count * 100, 1) : 0;

        var eventBreakdown = events.Select(ev =>
        {
            var evSeats = seats.Where(s => s.EventId == ev.Id).ToList();
            var evSeatIds = evSeats.Select(s => s.Id).ToHashSet();
            var evTickets = tickets.Where(t => evSeatIds.Contains(t.SeatId)).ToList();

            int evTotal = evSeats.Count;
            int evSold = evSeats.Count(s => s.Status == SeatStatus.Sold);
            int evHeld = evSeats.Count(s => s.Status == SeatStatus.Held);
            int evCheckedIn = evTickets.Count(t => t.Status == TicketStatus.Used);
            decimal evRevenue = evTickets.Sum(t => seatPriceMap.TryGetValue(t.SeatId, out var p) ? p : 0m);

            return new
            {
                eventId = ev.Id,
                title = ev.Title,
                venue = ev.Venue,
                startTime = ev.StartTime,
                totalSeats = evTotal,
                soldSeats = evSold,
                heldSeats = evHeld,
                availableSeats = evTotal - evSold - evHeld,
                checkedInCount = evCheckedIn,
                revenue = evRevenue,
                occupancyPercent = evTotal > 0 ? Math.Round((double)evSold / evTotal * 100, 1) : 0
            };
        }).ToList();

        return Ok(new
        {
            totalUsers = usersCount,
            totalEvents = events.Count,
            totalTicketsIssued = tickets.Count,
            totalRevenue,
            occupancyRate,
            checkInRate,
            checkedInTickets,
            heldSeats,
            events = eventBreakdown
        });
    }

    /// <summary>
    /// Tạo sự kiện mới kèm tự động khởi tạo sơ đồ 32 ghế theo 3 hạng giá (FR-43, FR-45, FR-46)
    /// </summary>
    [HttpPost("events")]
    public IActionResult CreateEvent([FromBody] CreateEventRequest request)
    {
        if (!IsAdminAuthorized(out var err)) return err!;

        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Venue))
        {
            return BadRequest(new { message = "Tên sự kiện và Địa điểm không được để trống." });
        }

        using var transaction = _db.Database.BeginTransaction();

        var newEvent = new Event
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Venue = request.Venue.Trim(),
            StartTime = request.StartTime ?? DateTime.UtcNow.AddDays(30),
            CreatedAt = DateTime.UtcNow
        };
        _db.Events.Add(newEvent);

        decimal vipPrice = request.VipPrice > 0 ? request.VipPrice : 1200000m;
        decimal stdPrice = request.StandardPrice > 0 ? request.StandardPrice : 890000m;
        decimal ecoPrice = request.EconomyPrice > 0 ? request.EconomyPrice : 650000m;

        var rows = new[] { "A", "B", "C", "D" };
        foreach (var row in rows)
        {
            decimal price = row == "A" ? vipPrice : row == "B" ? stdPrice : ecoPrice;
            for (int col = 1; col <= 8; col++)
            {
                _db.Seats.Add(new Seat
                {
                    Id = Guid.NewGuid(),
                    EventId = newEvent.Id,
                    SeatLabel = $"{row}{col}",
                    Price = price,
                    Status = SeatStatus.Available
                });
            }
        }

        _db.SaveChanges();
        transaction.Commit();

        return Ok(new
        {
            eventId = newEvent.Id,
            title = newEvent.Title,
            venue = newEvent.Venue,
            seatsCreated = 32,
            message = $"Đã tạo sự kiện '{newEvent.Title}' kèm sơ đồ 32 ghế thành công!"
        });
    }

    /// <summary>
    /// Cấp tài khoản nội bộ (GATE_STAFF / ADMIN) — Chỉ Quản trị viên đã xác thực mới có quyền tạo
    /// </summary>
    [HttpPost("users")]
    public IActionResult CreateInternalUser([FromBody] CreateInternalUserRequest request)
    {
        if (!IsAdminAuthorized(out var err)) return err!;

        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.FullName))
        {
            return BadRequest(new { message = "Vui lòng nhập đầy đủ Họ tên, Email và Mật khẩu." });
        }

        if (request.Password.Length < 8)
        {
            return BadRequest(new { message = "Mật khẩu phải có tối thiểu 8 ký tự." });
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        if (_db.Users.Any(u => u.Email.ToLower() == normalizedEmail))
        {
            return Conflict(new { message = "Email này đã tồn tại trong hệ thống." });
        }

        var normalizedRole = (request.Role ?? "GATE_STAFF").Trim().ToUpperInvariant();
        if (normalizedRole != "GATE_STAFF" && normalizedRole != "ADMIN")
        {
            return BadRequest(new { message = "Vai trò nội bộ chỉ chấp nhận GATE_STAFF hoặc ADMIN." });
        }

        var staffUser = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            FullName = request.FullName.Trim(),
            PasswordHash = PasswordHasher.Hash(request.Password),
            Role = normalizedRole,
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(staffUser);
        _db.SaveChanges();

        return Ok(new
        {
            userId = staffUser.Id,
            email = staffUser.Email,
            fullName = staffUser.FullName,
            role = staffUser.Role,
            message = $"Đã cấp tài khoản {staffUser.Role} cho '{staffUser.FullName}' ({staffUser.Email})!"
        });
    }
}

public record CreateEventRequest(
    string Title,
    string Venue,
    DateTime? StartTime,
    decimal VipPrice = 1200000m,
    decimal StandardPrice = 890000m,
    decimal EconomyPrice = 650000m);

public record CreateInternalUserRequest(
    string Email,
    string Password,
    string FullName,
    string Role = "GATE_STAFF");
