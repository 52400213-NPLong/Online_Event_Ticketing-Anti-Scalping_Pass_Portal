using System;
using System.Security.Cryptography;
using System.Text;
using EventTicketing.Core.Constants;
using EventTicketing.Core.Interfaces;

namespace EventTicketing.Infrastructure.Security
{
    public class DynamicQrService : IDynamicQrService
    {
        private readonly byte[] _hmacSecretKey;

        public DynamicQrService(string secretKey)
        {
            if (string.IsNullOrWhiteSpace(secretKey))
                throw new ArgumentNullException(nameof(secretKey), "Secret key cannot be empty.");

            _hmacSecretKey = Encoding.UTF8.GetBytes(secretKey);
        }

        public string GenerateDynamicToken(Guid ticketId, string userId)
        {
            long currentTimestampWindow = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / AntiScalpingConfig.DYNAMIC_QR_VALIDITY_SECONDS;
            string rawPayload = $"{ticketId}:{userId}:{currentTimestampWindow}";

            using (var hmac = new HMACSHA256(_hmacSecretKey))
            {
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawPayload));
                return Convert.ToBase64String(hash);
            }
        }

        public bool ValidateToken(Guid ticketId, string userId, string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;

            long currentWindow = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / AntiScalpingConfig.DYNAMIC_QR_VALIDITY_SECONDS;

            // Kiểm tra cửa sổ thời gian hiện tại và 1 cửa sổ trước đó (cho phép lệch mạng nhỏ)
            for (int offset = 0; offset <= 1; offset++)
            {
                long windowToTest = currentWindow - offset;
                string rawPayload = $"{ticketId}:{userId}:{windowToTest}";

                using (var hmac = new HMACSHA256(_hmacSecretKey))
                {
                    byte[] computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawPayload));
                    string expectedToken = Convert.ToBase64String(computedHash);

                    if (CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(expectedToken), 
                        Encoding.UTF8.GetBytes(token)))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}