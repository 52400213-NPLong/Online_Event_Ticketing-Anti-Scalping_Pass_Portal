using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using EventTicketing.Core.Interfaces;
using EventTicketing.Infrastructure.Data;
using EventTicketing.Infrastructure.Repositories;
using EventTicketing.Infrastructure.Security;
using EventTicketing.Infrastructure.Services;
using EventTicketing.WebApi.Hubs;
using EventTicketing.WebApi.Middlewares;
using EventTicketing.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// 1. Cấu hình Controllers & OpenAPI
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// 2. Cấu hình Realtime SignalR (Cập nhật sơ đồ ghế tức thời)
builder.Services.AddSignalR();

// 3. Cấu hình CORS cho phép Client gọi API mượt mà
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// 4. Đăng ký Entity Framework Core + Microsoft SQL Server (Ưu tiên Docker SQL Server cổng 1433, hỗ trợ thêm SQLEXPRESS local)
string connectionString = ResolveSqlServerConnectionString(builder.Configuration);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ITicketRepository, TicketRepository>();
builder.Services.AddScoped<ITicketBookingService, TicketBookingService>();
builder.Services.AddSingleton<JwtTokenGenerator>();

// Khóa bí mật cho mã QR động HMAC-SHA256 (Anti-Scalping Token)
string qrSecretKey = Environment.GetEnvironmentVariable("DYNAMIC_QR_SECRET_KEY")
    ?? "SuperSecureHmacSecretKeyForDynamicQrTokens2026_TDTU_SE";
builder.Services.AddSingleton<IDynamicQrService>(new DynamicQrService(qrSecretKey));

// 5. Background Service dọn dẹp các ghế giữ chỗ quá hạn 10 phút (Chu kỳ 5s theo NFR-15)
builder.Services.AddHostedService<ExpiredHoldCleanupWorker>();

var app = builder.Build();

// Khởi tạo Database SQL Server & Seeding dữ liệu mẫu (Có cơ chế chờ container SQL Server khởi động hoàn tất)
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    const int maxStartupAttempts = 15;
    for (int attempt = 1; attempt <= maxStartupAttempts; attempt++)
    {
        try
        {
            db.SeedSampleData();
            logger.LogInformation("Connected to Microsoft SQL Server and initialized EventTicketingDb successfully.");
            break;
        }
        catch (Exception) when (attempt < maxStartupAttempts)
        {
            logger.LogWarning("Waiting for SQL Server engine to accept connections (attempt {Attempt}/{Max})...", attempt, maxStartupAttempts);
            Thread.Sleep(TimeSpan.FromSeconds(3));
        }
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();

// Middleware chống lỗi và chống bot/spam request (Rate Limiting)
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<RateLimitingMiddleware>();

// 6. Phục vụ giao diện WebClient tĩnh trực tiếp từ thư mục EventTicketing.WebClient/public & ảnh banner /img
var clientPath = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "EventTicketing.WebClient", "public"));
if (Directory.Exists(clientPath))
{
    var fileProvider = new PhysicalFileProvider(clientPath);
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = fileProvider,
        RequestPath = ""
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = fileProvider,
        RequestPath = ""
    });
}
else
{
    app.UseStaticFiles();
}

var imgPath = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "img"));
if (Directory.Exists(imgPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(imgPath),
        RequestPath = "/img"
    });
}

// 7. Định tuyến API Endpoints & SignalR Hub
app.MapGet("/", () => Results.Redirect("/pages/index.html"));
app.MapControllers();
app.MapHub<SeatHub>("/hubs/seats");

app.Run();

static string ResolveSqlServerConnectionString(IConfiguration configuration)
{
    string defaultConn = configuration.GetConnectionString("DefaultConnection")
        ?? ApplicationDbContext.DefaultSqlServerConnection;

    // Khi chạy dotnet run ngoài Docker mà container SQL Server (localhost,1433) chưa bật, tự động dùng SQLEXPRESS nếu có
    if (defaultConn.StartsWith("Server=localhost,1433", StringComparison.OrdinalIgnoreCase) && !IsLocalPortOpen(1433))
    {
        string? expressConn = configuration.GetConnectionString("SqlExpressConnection");
        if (!string.IsNullOrWhiteSpace(expressConn))
        {
            return expressConn;
        }
    }

    return defaultConn;
}

static bool IsLocalPortOpen(int port)
{
    try
    {
        using var tcp = new TcpClient();
        var connectTask = tcp.ConnectAsync("127.0.0.1", port);
        return connectTask.Wait(TimeSpan.FromMilliseconds(350)) && tcp.Connected;
    }
    catch
    {
        return false;
    }
}
