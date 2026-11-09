namespace EventTicketing.Core.Constants
{
    public static class AntiScalpingConfig
    {
        // Chống Magic Numbers (Tham chiếu Bảng tham số cấu hình mục III.5 trong Đề cương Topic 13)
        public const int DYNAMIC_QR_VALIDITY_SECONDS = 30;
        public const int MAX_TICKETS_PER_USER = 4;
        public const int HOLD_DURATION_MINUTES = 10;
        public const int CLEANUP_WORKER_INTERVAL_SECONDS = 5;
        public const int JWT_EXPIRY_MINUTES = 120;
        public const string HMAC_SECRET_KEY_ENV = "DYNAMIC_QR_SECRET_KEY";
        public const string JWT_ISSUER = "PassPortal.EventTicketing";
        public const string JWT_AUDIENCE = "PassPortal.WebClient";
        public const string DEFAULT_JWT_SECRET = "PassPortal_SuperSecretJwtSigningKey_2026_TDTU_Course502045!";
    }
}
