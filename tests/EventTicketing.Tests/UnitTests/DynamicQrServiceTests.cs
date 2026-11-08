using EventTicketing.Infrastructure.Security;
using Xunit;

namespace EventTicketing.Tests.UnitTests;

public class DynamicQrServiceTests
{
    private const string TestSecretKey = "SuperSecretKeyForTestingAntiScalping123!";

    [Fact]
    public void GenerateToken_ValidInput_ReturnsNonEmptyToken()
    {
        var qrService = new DynamicQrService(TestSecretKey);

        var token = qrService.GenerateDynamicToken(Guid.NewGuid(), "USER-1001");

        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    public void ValidateToken_SameWindow_ReturnsTrue()
    {
        var qrService = new DynamicQrService(TestSecretKey);
        var ticketId = Guid.NewGuid();
        var userId = "USER-1002";

        var token = qrService.GenerateDynamicToken(ticketId, userId);
        var isValid = qrService.ValidateToken(ticketId, userId, token);

        Assert.True(isValid);
    }

    [Fact]
    public void ValidateToken_InvalidUserId_ReturnsFalse()
    {
        var qrService = new DynamicQrService(TestSecretKey);
        var ticketId = Guid.NewGuid();
        var originalUserId = "USER-ORIGINAL";

        var token = qrService.GenerateDynamicToken(ticketId, originalUserId);
        var isValid = qrService.ValidateToken(ticketId, "USER-ATTACKER", token);

        Assert.False(isValid);
    }
}
