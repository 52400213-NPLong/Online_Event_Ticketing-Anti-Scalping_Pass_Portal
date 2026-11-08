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
        var clientId = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

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
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.ContentType = "application/json";
                context.Response.WriteAsJsonAsync(new { message = "Too many requests. Please try again later." });
                return;
            }

            windowRequests.Enqueue(now);
        }

        await _next(context);
    }
}
