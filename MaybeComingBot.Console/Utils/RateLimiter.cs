using MaybeComingBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace MaybeComingBot.Utils;

/// <summary>
/// Provides rate limiting functionality to prevent abuse and spam.
/// </summary>
/// <remarks>
/// This class tracks user requests and enforces limits based on a sliding window.
/// Administrators are exempt from rate limiting. Expired entries are automatically
/// cleaned up by a background timer to prevent memory leaks.
/// </remarks>
public class RateLimiter : IDisposable
{
    private readonly ConcurrentDictionary<long, UserRateLimit> _userLimits = new();
    private readonly int _maxRequests;
    private readonly TimeSpan _window;
    private readonly HashSet<long> _adminUserIds;
    private readonly ILogger<RateLimiter> _logger;
    private readonly Timer _cleanupTimer;
    private readonly SemaphoreSlim _cleanupLock = new(1, 1);
    private bool _disposed;
    private int _totalRateLimitExceeded;

    private const int CleanupIntervalMinutes = 10;
    private const int ExpirationMultiplier = 2;

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimiter"/> class.
    /// </summary>
    /// <param name="settings">The bot settings containing rate limit configuration.</param>
    /// <param name="logger">The logger instance for this class.</param>
    public RateLimiter(
        IOptions<BotSettings> settings,
        ILogger<RateLimiter> logger)
    {
        _maxRequests = settings.Value.RateLimitMaxRequests;
        _window = TimeSpan.FromMinutes(settings.Value.RateLimitWindowMinutes);
        _adminUserIds = new HashSet<long>(settings?.Value?.AdminUserIds ?? Array.Empty<long>());
        _logger = logger;

        _cleanupTimer = new Timer(
            CleanupExpiredEntries,
            null,
            TimeSpan.FromMinutes(CleanupIntervalMinutes),
            TimeSpan.FromMinutes(CleanupIntervalMinutes));
    }

    /// <summary>
    /// Checks if a user is allowed to make a request based on the rate limit policy.
    /// </summary>
    /// <param name="userId">The Telegram user ID. Must be greater than 0.</param>
    /// <returns>
    /// <c>true</c> if the user is allowed to proceed; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// Administrators bypass rate limiting entirely.
    /// Rate limit is enforced per user with a sliding window.
    /// Each call increments the request count for the user.
    /// </remarks>
    public Task<bool> IsAllowedAsync(long userId)
    {
        // Administrators are always allowed
        if (_adminUserIds.Contains(userId))
            return Task.FromResult(true);

        var now = DateTime.UtcNow;
        var limit = _userLimits.GetOrAdd(userId, _ => new UserRateLimit());

        // Lock only for counter update to ensure atomicity
        lock (limit)
        {
            // Check if the window has expired
            if (now - limit.WindowStart > _window)
            {
                limit.WindowStart = now;
                limit.Count = 0;
            }

            // Check if limit is exceeded
            if (limit.Count >= _maxRequests)
            {
                Interlocked.Increment(ref _totalRateLimitExceeded);
                _logger.LogWarning("Rate limit exceeded for {UserId} (count: {Count}, window: {Window}m)",
                    userId, limit.Count, _window.TotalMinutes);
                return Task.FromResult(false);
            }

            limit.Count++;
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// Timer callback that initiates cleanup of expired entries.
    /// </summary>
    /// <param name="state">The timer state (not used).</param>
    private void CleanupExpiredEntries(object? state)
    {
        if (_disposed) return;

        Task.Run(async () =>
        {
            try
            {
                await CleanupExpiredEntriesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cleanup failed");
            }
        });
    }

    /// <summary>
    /// Removes expired rate limit entries from the dictionary.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Entries are considered expired when their last activity is older than
    /// <see cref="ExpirationMultiplier"/> × the rate limit window.
    /// This prevents the dictionary from growing indefinitely.
    /// </remarks>
    private async Task CleanupExpiredEntriesAsync()
    {
        if (_disposed) return;

        if (!await _cleanupLock.WaitAsync(TimeSpan.Zero))
        {
            _logger.LogDebug("Cleanup already in progress, skipping");
            return;
        }

        try
        {
            var now = DateTime.UtcNow;
            var expirationThreshold = _window * ExpirationMultiplier;

            // Collect keys to remove
            var expiredKeys = _userLimits
                .Where(kvp => now - kvp.Value.WindowStart > expirationThreshold)
                .Select(kvp => kvp.Key)
                .ToList();

            if (expiredKeys.Count == 0)
                return;

            foreach (var key in expiredKeys)
            {
                if (_userLimits.TryRemove(key, out _))
                {
                    _logger.LogDebug("Cleaned up expired rate limit entry for {UserId}", key);
                }
            }

            _logger.LogDebug("Cleaned up {Count} expired rate limit entries", expiredKeys.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up rate limit entries");
        }
        finally
        {
            _cleanupLock.Release();
        }
    }

    /// <summary>
    /// Resets the rate limit counter for a specific user.
    /// </summary>
    /// <param name="userId">The Telegram user ID to reset.</param>
    /// <remarks>
    /// This method completely removes the user's rate limit entry,
    /// allowing them to start fresh with a clean window.
    /// Useful for admins to unblock users or during testing.
    /// </remarks>
    public void ResetUser(long userId)
    {
        if (_userLimits.TryRemove(userId, out _))
        {
            _logger.LogDebug("Reset rate limit for {UserId}", userId);
        }
    }

    /// <summary>
    /// Gets the total number of rate limit exceeded events since startup.
    /// </summary>
    /// <value>
    /// An integer representing the count of rate limit violations.
    /// </value>
    /// <remarks>
    /// This value is thread-safe and can be used for monitoring and alerting.
    /// </remarks>
    public int TotalRateLimitExceeded => Volatile.Read(ref _totalRateLimitExceeded);

    /// <summary>
    /// Gets the number of users currently tracked by the rate limiter.
    /// </summary>
    /// <value>
    /// The active user count in the rate limit dictionary.
    /// </value>
    public int ActiveUsersCount => _userLimits.Count;

    /// <summary>
    /// Disposes the rate limiter and releases all resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Stop and dispose the timer
        _cleanupTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _cleanupTimer?.Dispose();

        _cleanupLock?.Dispose();
        _userLimits.Clear();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Represents the rate limit state for a single user.
    /// </summary>
    /// <remarks>
    /// Tracks the start of the current sliding window and the number of requests
    /// made within it. Instances are stored per user in <see cref="_userLimits"/>
    /// and are access-safe via a lock in <see cref="IsAllowedAsync"/>.
    /// </remarks>
    private sealed class UserRateLimit
    {
        /// <summary>Gets or sets the start of the current rate limit window.</summary>
        public DateTime WindowStart { get; set; } = DateTime.UtcNow;

        /// <summary>Gets or sets the request count within the current window.</summary>
        public int Count { get; set; }
    }
}