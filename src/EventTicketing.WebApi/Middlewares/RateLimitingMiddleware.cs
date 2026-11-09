namespace EventTicketing.WebApi.Middlewares;

public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TimeSpan _window = TimeSpan.FromMinutes(1);
    private readonly int _maxRequests = 60;
    private static readonly Dictionary<string, Queue<DateTime>> RequestTracker = new();

    public RateLimitingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Chỉ giới hạn tần suất gọi trên các API endpoints (không chặn tải file giao diện tĩnh HTML/CSS/JS)
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        var clientId = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        bool isRateLimited = false;

        lock (RequestTracker)
        {
            if (!RequestTracker.TryGetValue(clientId, out var windowRequests))
            {
                windowRequests = new Queue<DateTime>();
                RequestTracker[clientId] = windowRequests;
            }

            var now = DateTime.UtcNow;
            while (windowRequests.Count > 0 && now - windowRequests.Peek() > _window)
            {
                windowRequests.Dequeue();
            }

            if (windowRequests.Count >= _maxRequests)
            {
                isRateLimited = true;
            }
            else
            {
                windowRequests.Enqueue(now);
            }
        }

        if (isRateLimited)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { message = "Too many requests (Anti-Bot Rate Limit). Please try again later." });
            return;
        }

        await _next(context);
    }
}
