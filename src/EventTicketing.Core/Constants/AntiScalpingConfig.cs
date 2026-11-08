namespace EventTicketing.Core.Constants
{
    public static class AntiScalpingConfig
    {
        // Chống Magic Numbers
        public const int DYNAMIC_QR_VALIDITY_SECONDS = 30;
        public const int MAX_TICKETS_PER_USER = 4;
        public const int HOLD_DURATION_MINUTES = 10;
        public const string HMAC_SECRET_KEY_ENV = "DYNAMIC_QR_SECRET_KEY";
    }
}