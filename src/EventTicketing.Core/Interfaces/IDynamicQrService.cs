using System;

namespace EventTicketing.Core.Interfaces
{
    public interface IDynamicQrService
    {
        string GenerateDynamicToken(Guid ticketId, string userId);
        bool ValidateToken(Guid ticketId, string userId, string token);
    }
}