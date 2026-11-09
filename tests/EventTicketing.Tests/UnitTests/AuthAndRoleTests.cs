using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventTicketing.Core.Domain.Entities;
using EventTicketing.Core.Domain.Enums;
using EventTicketing.Infrastructure.Data;
using EventTicketing.Infrastructure.Repositories;
using EventTicketing.Infrastructure.Security;
using EventTicketing.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EventTicketing.Tests.UnitTests;

public class AuthAndRoleTests
{
    private static ControllerContext CreateAuthContext(string? bearerToken)
    {
        var httpContext = new DefaultHttpContext();
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            httpContext.Request.Headers.Authorization = $"Bearer {bearerToken}";
        }

        return new ControllerContext { HttpContext = httpContext };
    }

    [Fact]
    public async Task PublicRegister_StrictlyAssignsCustomerRole_PreventingPrivilegeEscalation()
    {
        using var db = new ApplicationDbContext();
        var userRepo = new UserRepository(db);
        var jwtGen = new JwtTokenGenerator();
        var authController = new AuthController(userRepo, jwtGen);

        var regResponse = await authController.Register(
            new RegisterRequest("customer_new@example.com", "SecurePass123!", "Khách Hàng Mới"),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(regResponse);

        var loginResponse = await authController.Login(
            new LoginRequest("customer_new@example.com", "SecurePass123!"),
            CancellationToken.None);

        var okLogin = Assert.IsType<OkObjectResult>(loginResponse);
        var tokenProp = okLogin.Value!.GetType().GetProperty("token")?.GetValue(okLogin.Value) as string;
        Assert.False(string.IsNullOrWhiteSpace(tokenProp));

        var principal = jwtGen.ValidateToken(tokenProp);
        Assert.NotNull(principal);
        Assert.Equal("CUSTOMER", principal!.FindFirst(ClaimTypes.Role)?.Value);
    }

    [Fact]
    public async Task SeedSampleData_CreatesThreeDemoRoles_AllCanLoginWithDefaultPassword()
    {
        using var db = new ApplicationDbContext();
        db.SeedSampleData();

        var userRepo = new UserRepository(db);
        var jwtGen = new JwtTokenGenerator();
        var authController = new AuthController(userRepo, jwtGen);

        var buyerLogin = await authController.Login(new LoginRequest("buyer@example.com", "Password123!"), CancellationToken.None);
        var staffLogin = await authController.Login(new LoginRequest("staff@example.com", "Password123!"), CancellationToken.None);
        var adminLogin = await authController.Login(new LoginRequest("admin@example.com", "Password123!"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(buyerLogin);
        Assert.IsType<OkObjectResult>(staffLogin);
        Assert.IsType<OkObjectResult>(adminLogin);
    }

    [Fact]
    public void GateCheckIn_CustomerRoleToken_Returns403Forbidden_WhileGateStaffSucceeds()
    {
        using var db = new ApplicationDbContext();
        var qrService = new DynamicQrService("RoleTestQrSecretKey2026!");
        var jwtGen = new JwtTokenGenerator();

        var eventObj = new Event { Id = Guid.NewGuid(), Title = "Role Test Concert", Venue = "TDTU Hall", StartTime = DateTime.UtcNow.AddDays(5) };
        var seatObj = new Seat { Id = Guid.NewGuid(), EventId = eventObj.Id, SeatLabel = "A1", Price = 1000000m, Status = SeatStatus.Sold };
        var ticketId = Guid.NewGuid();
        var ownerId = Guid.NewGuid().ToString();

        db.Events.Add(eventObj);
        db.Seats.Add(seatObj);
        db.Tickets.Add(new Ticket
        {
            Id = ticketId,
            UserId = ownerId,
            SeatId = seatObj.Id,
            Status = TicketStatus.Issued
        });
        db.SaveChanges();

        var qrToken = qrService.GenerateDynamicToken(ticketId, ownerId);

        // 1. Người mua vé (CUSTOMER) cố tình gọi API quét vé tại cổng -> Bị chặn 403 Forbidden
        var customerJwt = jwtGen.GenerateToken(ownerId, "CUSTOMER", "buyer@example.com", "Buyer");
        var gateForCustomer = new GateController(qrService, db, jwtGen)
        {
            ControllerContext = CreateAuthContext(customerJwt)
        };

        var forbiddenRes = gateForCustomer.VerifyCheckIn(new CheckInRequest(ticketId, ownerId, qrToken));
        var objRes = Assert.IsType<ObjectResult>(forbiddenRes);
        Assert.Equal(403, objRes.StatusCode);

        // 2. Nhân viên soát vé (GATE_STAFF) gọi API quét vé tại cổng -> Thành công 200 OK
        var staffJwt = jwtGen.GenerateToken(Guid.NewGuid().ToString(), "GATE_STAFF", "staff@example.com", "Gate Staff");
        var gateForStaff = new GateController(qrService, db, jwtGen)
        {
            ControllerContext = CreateAuthContext(staffJwt)
        };

        var okRes = gateForStaff.VerifyCheckIn(new CheckInRequest(ticketId, ownerId, qrToken, "staff@example.com"));
        Assert.IsType<OkObjectResult>(okRes);
        Assert.Equal(TicketStatus.Used, db.Tickets.First(t => t.Id == ticketId).Status);
    }

    [Fact]
    public void AdminController_EnforcesAdminOnly_AndCreatesEventWith32Seats()
    {
        using var db = new ApplicationDbContext();
        var jwtGen = new JwtTokenGenerator();

        // 1. Nhân viên cổng (GATE_STAFF) gọi API quản trị -> Bị chặn 403 Forbidden
        var staffJwt = jwtGen.GenerateToken(Guid.NewGuid().ToString(), "GATE_STAFF", "staff@example.com", "Staff");
        var adminControllerForStaff = new AdminController(db, jwtGen)
        {
            ControllerContext = CreateAuthContext(staffJwt)
        };

        var deniedStats = adminControllerForStaff.GetDashboardStats();
        var deniedResult = Assert.IsType<ObjectResult>(deniedStats);
        Assert.Equal(403, deniedResult.StatusCode);

        // 2. Quản trị viên (ADMIN) tạo sự kiện mới và xem báo cáo -> Thành công 200 OK
        var adminJwt = jwtGen.GenerateToken(Guid.NewGuid().ToString(), "ADMIN", "admin@example.com", "Admin");
        var adminController = new AdminController(db, jwtGen)
        {
            ControllerContext = CreateAuthContext(adminJwt)
        };

        var createRes = adminController.CreateEvent(new CreateEventRequest(
            "TDTU Tech Gala 2026",
            "Hội trường 2A - TDTU",
            DateTime.UtcNow.AddDays(15),
            1200000m,
            800000m,
            500000m));

        Assert.IsType<OkObjectResult>(createRes);
        Assert.Equal(1, db.Events.Count(e => e.Title == "TDTU Tech Gala 2026"));
        Assert.Equal(32, db.Seats.Count());

        var statsRes = adminController.GetDashboardStats();
        Assert.IsType<OkObjectResult>(statsRes);

        // 3. Chỉ ADMIN mới có quyền cấp tài khoản nội bộ GATE_STAFF
        var createStaffRes = adminController.CreateInternalUser(new CreateInternalUserRequest(
            "gate2@passportal.vn",
            "StaffPass123!",
            "Nhân Viên Cổng 2",
            "GATE_STAFF"));

        Assert.IsType<OkObjectResult>(createStaffRes);
        Assert.Equal("GATE_STAFF", db.Users.First(u => u.Email == "gate2@passportal.vn").Role);
    }
}
