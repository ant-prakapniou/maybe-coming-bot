using MaybeComingBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace MaybeComingBot.Utils;

public class RateLimiter : IDisposable
{
    private readonly ConcurrentDictionary<long, UserRateLimit> _userLimits = new();
    private readonly int _maxRequests;
    private readonly TimeSpan _window;
    private readonly long[] _adminUserIds;
    private readonly ILogger<RateLimiter> _logger;
    private readonly Timer _cleanupTimer;
    private readonly SemaphoreSlim _cleanupLock = new(1, 1);

    public RateLimiter(
        IOptions<BotSettings> settings,
        ILogger<RateLimiter> logger)
    {
        _maxRequests = settings.Value.RateLimitMaxRequests;
        _window = TimeSpan.FromMinutes(settings.Value.RateLimitWindowMinutes);
        _adminUserIds = settings.Value.AdminUserIds;
        _logger = logger;

        _cleanupTimer = new Timer(CleanupExpiredEntries, null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
    }

    public async Task<bool> IsAllowedAsync(long userId)
    {
        if (_adminUserIds.Contains(userId))
            return true;

        var now = DateTime.UtcNow;
        CleanupSingleUser(userId, now);

        var limit = _userLimits.GetOrAdd(userId, _ => new UserRateLimit());

        lock (limit)
        {
            if (now - limit.WindowStart > _window)
            {
                limit.WindowStart = now;
                limit.Count = 0;
            }

            if (limit.Count >= _maxRequests)
            {
                _logger.LogWarning("Rate limit exceeded for {UserId} (count: {Count}, window: {Window}m)",
                    userId, limit.Count, _window.TotalMinutes);
                return false;
            }

            limit.Count++;
            return true;
        }
    }

    private void CleanupSingleUser(long userId, DateTime now)
    {
        if (_userLimits.TryGetValue(userId, out var limit))
        {
            lock (limit)
            {
                if (now - limit.WindowStart > _window * 2)
                {
                    _userLimits.TryRemove(userId, out _);
                }
            }
        }
    }

    private async void CleanupExpiredEntries(object? state)
    {
        if (!await _cleanupLock.WaitAsync(0))
            return;

        try
        {
            var now = DateTime.UtcNow;
            var expiredKeys = _userLimits
                .Where(kvp => now - kvp.Value.WindowStart > _window * 2)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                if (_userLimits.TryRemove(key, out _))
                {
                    _logger.LogDebug("Cleaned up expired rate limit entry for {UserId}", key);
                }
            }

            if (expiredKeys.Count > 0)
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

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        _cleanupLock?.Dispose();
    }

    private class UserRateLimit
    {
        public DateTime WindowStart { get; set; } = DateTime.UtcNow;
        public int Count { get; set; }
    }
}