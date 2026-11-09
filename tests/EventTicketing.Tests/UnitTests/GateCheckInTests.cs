using System;
using System.Linq;
using EventTicketing.Core.Domain.Entities;
using EventTicketing.Core.Domain.Enums;
using EventTicketing.Infrastructure.Data;
using EventTicketing.Infrastructure.Security;
using EventTicketing.WebApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EventTicketing.Tests.UnitTests;

public class GateCheckInTests
{
    private const string SecretKey = "GateCheckInSecretKey2026Test!";

    [Fact]
    public void VerifyCheckIn_ValidToken_ReturnsSuccessAndMarksTicketUsed()
    {
        using var db = new ApplicationDbContext();
        var qrService = new DynamicQrService(SecretKey);
        var controller = new GateController(qrService, db);

        var ticketId = Guid.NewGuid();
        var userId = "USER-TEST-GATE";

        db.Tickets.Add(new Ticket
        {
            Id = ticketId,
            UserId = userId,
            Status = TicketStatus.Issued
        });
        db.SaveChanges();

        // Sinh mã QR động hợp lệ
        var token = qrService.GenerateDynamicToken(ticketId, userId);

        var response = controller.VerifyCheckIn(new CheckInRequest(ticketId, userId, token));
        var okResult = Assert.IsType<OkObjectResult>(response);

        // Vé phải được cập nhật trạng thái đã dùng (Used) trong SQLite DB
        var ticket = db.Tickets.First(t => t.Id == ticketId);
        Assert.Equal(TicketStatus.Used, ticket.Status);
    }

    [Fact]
    public void VerifyCheckIn_AlreadyUsedTicket_ReturnsConflictAntiScalpingFraud()
    {
        using var db = new ApplicationDbContext();
        var qrService = new DynamicQrService(SecretKey);
        var controller = new GateController(qrService, db);

        var ticketId = Guid.NewGuid();
        var userId = "USER-SCREENSHOT-REPLAY";

        // Vé đã được check-in trước đó
        db.Tickets.Add(new Ticket
        {
            Id = ticketId,
            UserId = userId,
            Status = TicketStatus.Used
        });
        db.SaveChanges();

        var token = qrService.GenerateDynamicToken(ticketId, userId);
        var response = controller.VerifyCheckIn(new CheckInRequest(ticketId, userId, token));

        var conflictResult = Assert.IsType<ConflictObjectResult>(response);
        Assert.NotNull(conflictResult.Value);
    }

    [Fact]
    public void VerifyCheckIn_InvalidOrForgedToken_ReturnsBadRequest()
    {
        using var db = new ApplicationDbContext();
        var qrService = new DynamicQrService(SecretKey);
        var controller = new GateController(qrService, db);

        var ticketId = Guid.NewGuid();
        var userId = "USER-FORGER";

        db.Tickets.Add(new Ticket
        {
            Id = ticketId,
            UserId = userId,
            Status = TicketStatus.Issued
        });
        db.SaveChanges();

        var fakeToken = "FakeOrExpiredTokenString==";
        var response = controller.VerifyCheckIn(new CheckInRequest(ticketId, userId, fakeToken));

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(response);
        Assert.NotNull(badRequestResult.Value);
    }

    [Fact]
    public void VerifyCheckIn_NonOwnerUserId_ReturnsBadRequestNonTransferableBlock()
    {
        using var db = new ApplicationDbContext();
        var qrService = new DynamicQrService(SecretKey);
        var controller = new GateController(qrService, db);

        var ticketId = Guid.NewGuid();
        db.Tickets.Add(new Ticket
        {
            Id = ticketId,
            UserId = "USER-ORIGINAL-OWNER",
            Status = TicketStatus.Issued
        });
        db.SaveChanges();

        // Kẻ mua vé chợ đen cố dùng UserId khác để check-in
        var attackerToken = qrService.GenerateDynamicToken(ticketId, "USER-BUYER-RESALE");
        var response = controller.VerifyCheckIn(new CheckInRequest(ticketId, "USER-BUYER-RESALE", attackerToken));

        Assert.IsType<BadRequestObjectResult>(response);
        var ticket = db.Tickets.First(t => t.Id == ticketId);
        Assert.Equal(TicketStatus.Issued, ticket.Status);
    }
}
