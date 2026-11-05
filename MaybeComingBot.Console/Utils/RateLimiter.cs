using MaybeComingBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace MaybeComingBot.Utils;

/// <summary>
/// Provides rate limiting functionality to prevent abuse and spam.
/// </summary>
/// <remarks>
/// <para>
/// This class tracks user requests and enforces limits based on a sliding window.
/// Administrators are exempt from rate limiting. Expired entries are automatically
/// cleaned up by a background timer to prevent memory leaks.
/// </para>
/// <para>
/// Time is obtained via <see cref="TimeProvider"/>. In production the
/// parameterless constructor uses <see cref="TimeProvider.System"/>; tests
/// inject a <c>FakeTimeProvider</c> so window-reset behaviour is deterministic
/// and does not need real <see cref="Task.Delay"/> calls.
/// </para>
/// <para>
/// Per-user state is protected by a <see cref="SemaphoreSlim"/> rather than a
/// <see cref="lock"/>, because <see cref="IsAllowedAsync"/> is asynchronous and
/// must not block the calling thread under contention.
/// </para>
/// <para>
/// Cleanup runs on a <see cref="Timer"/> and is tracked as a <see cref="Task"/> so
/// <see cref="Dispose"/> can cancel it and wait for completion. Without this, a
/// cleanup that is in flight when the service is disposed could race with the
/// disposal of the cleanup semaphore and throw <see cref="ObjectDisposedException"/>.
/// </para>
/// </remarks>
public class RateLimiter : IDisposable
{
    private readonly ConcurrentDictionary<long, UserRateLimit> _userLimits = new();
    private readonly int _maxRequests;
    private readonly TimeSpan _window;
    private readonly HashSet<long> _adminUserIds;
    private readonly ILogger<RateLimiter> _logger;
    private readonly TimeProvider _time;
    private readonly Timer _cleanupTimer;
    private readonly SemaphoreSlim _cleanupLock = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private Task? _cleanupTask;
    private bool _disposed;
    private int _totalRateLimitExceeded;

    private const int CleanupIntervalMinutes = 10;
    private const int ExpirationMultiplier = 2;
    private const int DisposeWaitSeconds = 5;

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimiter"/> class
    /// using the system clock.
    /// </summary>
    /// <param name="settings">The bot settings containing rate limit configuration.</param>
    /// <param name="logger">The logger instance for this class.</param>
    public RateLimiter(
        IOptions<BotSettings> settings,
        ILogger<RateLimiter> logger)
        : this(settings, logger, TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimiter"/> class
    /// with an explicit time provider. Intended for tests.
    /// </summary>
    /// <param name="settings">The bot settings containing rate limit configuration.</param>
    /// <param name="logger">The logger instance for this class.</param>
    /// <param name="time">The time provider used to obtain the current UTC time.</param>
    public RateLimiter(
        IOptions<BotSettings> settings,
        ILogger<RateLimiter> logger,
        TimeProvider time)
    {
        _maxRequests = settings.Value.RateLimitMaxRequests;
        _window = TimeSpan.FromMinutes(settings.Value.RateLimitWindowMinutes);
        _adminUserIds = new HashSet<long>(settings?.Value?.AdminUserIds ?? Array.Empty<long>());
        _logger = logger;
        _time = time;

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
    /// Each allowed call increments the request count for the user.
    /// </remarks>
    public async Task<bool> IsAllowedAsync(long userId)
    {
        // Administrators are always allowed
        if (_adminUserIds.Contains(userId))
            return true;

        var now = _time.GetUtcNow().UtcDateTime;
        var limit = _userLimits.GetOrAdd(userId, _ => new UserRateLimit(now));

        await limit.Gate.WaitAsync().ConfigureAwait(false);
        try
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
                return false;
            }

            limit.Count++;
            return true;
        }
        finally
        {
            limit.Gate.Release();
        }
    }

    /// <summary>
    /// Timer callback that initiates cleanup of expired entries.
    /// </summary>
    /// <param name="state">The timer state (not used).</param>
    private void CleanupExpiredEntries(object? state)
    {
        if (_disposed) return;

        _cleanupTask = Task.Run(async () =>
        {
            try
            {
                await CleanupExpiredEntriesAsync(_disposeCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cleanup failed");
            }
        }, _disposeCts.Token);
    }

    /// <summary>
    /// Removes expired rate limit entries from the dictionary.
    /// </summary>
    /// <param name="ct">Cancellation token for the cleanup operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task CleanupExpiredEntriesAsync(CancellationToken ct)
    {
        if (_disposed) return;

        if (!await _cleanupLock.WaitAsync(TimeSpan.Zero, ct).ConfigureAwait(false))
        {
            _logger.LogDebug("Cleanup already in progress, skipping");
            return;
        }

        try
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var expirationThreshold = _window * ExpirationMultiplier;

            var expiredKeys = _userLimits
                .Where(kvp => now - kvp.Value.WindowStart > expirationThreshold)
                .Select(kvp => kvp.Key)
                .ToList();

            if (expiredKeys.Count == 0)
                return;

            foreach (var key in expiredKeys)
            {
                if (_userLimits.TryRemove(key, out var removed))
                {
                    removed.Dispose();
                    _logger.LogDebug("Cleaned up expired rate limit entry for {UserId}", key);
                }
            }

            _logger.LogDebug("Cleaned up {Count} expired rate limit entries", expiredKeys.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
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
    public void ResetUser(long userId)
    {
        if (_userLimits.TryRemove(userId, out var removed))
        {
            removed.Dispose();
            _logger.LogDebug("Reset rate limit for {UserId}", userId);
        }
    }

    /// <summary>
    /// Gets the total number of rate limit exceeded events since startup.
    /// </summary>
    public int TotalRateLimitExceeded => Volatile.Read(ref _totalRateLimitExceeded);

    /// <summary>
    /// Gets the number of users currently tracked by the rate limiter.
    /// </summary>
    public int ActiveUsersCount => _userLimits.Count;

    /// <summary>
    /// Disposes the rate limiter and releases all resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // 1. Stop the timer so no new cleanup tasks are scheduled.
        _cleanupTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _cleanupTimer.Dispose();

        // 2. Cancel any in-flight cleanup.
        try
        {
            _disposeCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed; nothing to cancel.
        }

        // 3. Wait (bounded) for the cleanup task to observe cancellation and exit.
        try
        {
            _cleanupTask?.Wait(TimeSpan.FromSeconds(DisposeWaitSeconds));
        }
        catch (AggregateException ex)
        {
            _logger.LogDebug(ex, "Cleanup task ended with an exception during dispose");
        }

        // 4. Release resources.
        _disposeCts.Dispose();
        _cleanupLock.Dispose();

        foreach (var limit in _userLimits.Values)
            limit.Dispose();
        _userLimits.Clear();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Represents the rate limit state for a single user.
    /// </summary>
    private sealed class UserRateLimit(DateTime now) : IDisposable
    {

        /// <summary>Serializes access to the per-user window state.</summary>
        public readonly SemaphoreSlim Gate = new(1, 1);

        public DateTime WindowStart { get; set; } = now;
        public int Count { get; set; }

        public void Dispose() => Gate.Dispose();
    }
}