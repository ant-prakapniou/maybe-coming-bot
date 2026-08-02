using MaybeComingBot.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MaybeComingBot.Models;
using System.Threading;

namespace MaybeComingBot.Services;

/// <summary>
/// Background service that periodically removes expired user states.
/// </summary>
/// <remarks>
/// This service runs as a hosted service and performs cleanup of user session data
/// that has exceeded the configured time-to-live (TTL). This prevents memory bloat
/// from inactive users and ensures fresh state for returning users.
/// </remarks>
public class UserStateCleanupService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromMinutes(1);

    private readonly ILogger<UserStateCleanupService> _logger;
    private readonly IUserStateService _userStateService;
    private readonly TimeSpan _expiry;
    private int _totalCleaned;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserStateCleanupService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance for this class.</param>
    /// <param name="userStateService">The user state service for managing user data.</param>
    /// <param name="settings">The bot settings containing TTL configuration.</param>
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

    /// <summary>
    /// Gets the total number of user states cleaned up since service startup.
    /// </summary>
    /// <value>
    /// A thread-safe integer representing the total cleanup count.
    /// </value>
    public int TotalCleaned => Volatile.Read(ref _totalCleaned);

    /// <summary>
    /// Executes the background service logic.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token for service shutdown.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// <para>
    /// The service runs in a loop with the following flow:
    /// <list type="number">
    /// <item><description>Waits for the cleanup interval (15 minutes)</description></item>
    /// <item><description>Retrieves expired user keys from the state service</description></item>
    /// <item><description>Clears each expired state</description></item>
    /// <item><description>Logs progress every 50 entries</description></item>
    /// <item><description>Updates the total cleaned counter</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// If an error occurs, the service waits for 1 minute before retrying
    /// to avoid tight error loops.
    /// </para>
    /// </remarks>
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

                var expiredKeys = _userStateService.GetExpiredKeys(_expiry)?.ToList() ?? [];

                if (expiredKeys.Count is 0)
                {
                    _logger.LogDebug("No expired user states to clean up");
                    continue;
                }

                // Clean with progress logging
                var cleanedCount = 0;
                foreach (var key in expiredKeys)
                {
                    _userStateService.Clear(key);
                    cleanedCount++;

                    // Log every 50 entries
                    if (cleanedCount % 50 is 0)
                    {
                        _logger.LogDebug("Cleaned {Cleaned}/{Total} expired user states",
                            cleanedCount, expiredKeys.Count);
                    }
                }

                Interlocked.Add(ref _totalCleaned, expiredKeys.Count);

                _logger.LogInformation(
                    "🧹 Cleaned up {Count} expired user states (TTL: {ExpiryHours:F1}h). Total cleaned: {Total}",
                    expiredKeys.Count,
                    _expiry.TotalHours,
                    TotalCleaned);
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

        _logger.LogInformation("🛑 User state cleanup stopped. Total cleaned: {Total}", TotalCleaned);
    }

    /// <summary>
    /// Stops the background service.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the stop operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("User state cleanup is stopping...");
        await base.StopAsync(cancellationToken);
    }
}