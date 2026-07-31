using MaybeComingBot.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MaybeComingBot.Models;

namespace MaybeComingBot.Services;

public class UserStateCleanupService : BackgroundService
{
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
        _logger.LogInformation("User state cleanup started. TTL: {ExpiryHours}h", _expiry.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
                if (stoppingToken.IsCancellationRequested)
                    break;

                var expiredKeys = _userStateService.GetExpiredKeys(_expiry).ToList();
                if (expiredKeys.Count == 0)
                    continue;

                foreach (var key in expiredKeys)
                    _userStateService.Clear(key);

                _logger.LogInformation("Cleaned up {Count} expired user states", expiredKeys.Count);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during user state cleanup");
                // Не подавляем исключение полностью, но даём сервису продолжить работу
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        _logger.LogInformation("User state cleanup stopped");
    }
}