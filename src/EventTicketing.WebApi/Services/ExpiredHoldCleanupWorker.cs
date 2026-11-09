using System;
using System.Threading;
using System.Threading.Tasks;
using EventTicketing.Core.Constants;
using EventTicketing.Core.Interfaces;
using EventTicketing.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventTicketing.WebApi.Services;

public class ExpiredHoldCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<SeatHub> _seatHub;
    private readonly ILogger<ExpiredHoldCleanupWorker> _logger;

    public ExpiredHoldCleanupWorker(
        IServiceScopeFactory scopeFactory,
        IHubContext<SeatHub> seatHub,
        ILogger<ExpiredHoldCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _seatHub = seatHub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ExpiredHoldCleanupWorker started running (Anti-Scalping Auto-Release with SQL Server DB).");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var bookingService = scope.ServiceProvider.GetRequiredService<ITicketBookingService>();
                int released = await bookingService.ReleaseExpiredHoldsAsync(stoppingToken);
                if (released > 0)
                {
                    _logger.LogInformation("Released {Count} expired seat holds in SQL Server. Reverting seats back to Available.", released);
                    await _seatHub.Clients.All.SendAsync("SeatsRefreshed", stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during expired hold cleanup.");
            }

            // Quét định kỳ mỗi 5 giây theo Bảng tham số mục III.5 & NFR-15 trong đề cương
            await Task.Delay(TimeSpan.FromSeconds(AntiScalpingConfig.CLEANUP_WORKER_INTERVAL_SECONDS), stoppingToken);
        }
    }
}
