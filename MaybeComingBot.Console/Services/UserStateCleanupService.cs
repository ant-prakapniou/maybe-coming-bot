using MaybeComingBot.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MaybeComingBot.Models;

namespace MaybeComingBot.Services;

public class UserStateCleanupService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromMinutes(1);

    private readonly ILogger<UserStateCleanupService> _logger;
    private readonly IUserStateService _userStateService;
    private readonly TimeSpan _expiry;

    public UserStateCleanupService(
        ILogger<UserStateCleanupService> logger,
        IUserStateService userStateService,
        IOptions<BotSettings> settings)
    {
        _logger = logger;
        _userStateService = userStateService;
        var ttlHours = settings.Value.UserStateTtlHours;
        _expiry = TimeSpan.FromHours(ttlHours > 0 ? ttlHours : 2);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "🔄 User state cleanup started. TTL: {ExpiryHours:F1}h, Interval: {CleanupMinutes:F1}min",
            _expiry.TotalHours,
            CleanupInterval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CleanupInterval, stoppingToken);

                if (stoppingToken.IsCancellationRequested)
                    break;

                var expiredKeys = _userStateService.GetExpiredKeys(_expiry).ToList();

                if (expiredKeys.Count == 0)
                {
                    _logger.LogDebug("No expired user states to clean up");
                    continue;
                }

                // Очистка с логированием прогресса
                var cleanedCount = 0;
                foreach (var key in expiredKeys)
                {
                    _userStateService.Clear(key);
                    cleanedCount++;

                    // Логируем каждые 50 записей
                    if (cleanedCount % 50 == 0)
                    {
                        _logger.LogDebug("Cleaned {Cleaned}/{Total} expired user states",
                            cleanedCount, expiredKeys.Count);
                    }
                }

                _logger.LogInformation(
                    "🧹 Cleaned up {Count} expired user states (TTL: {ExpiryHours:F1}h)",
                    expiredKeys.Count,
                    _expiry.TotalHours);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error during user state cleanup");
                try
                {
                    await Task.Delay(ErrorDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("🛑 User state cleanup stopped");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("User state cleanup is stopping...");
        await base.StopAsync(cancellationToken);
    }
}