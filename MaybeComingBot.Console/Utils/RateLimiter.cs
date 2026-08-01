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
    private bool _disposed;

    public RateLimiter(
        IOptions<BotSettings> settings,
        ILogger<RateLimiter> logger)
    {
        _maxRequests = settings.Value.RateLimitMaxRequests;
        _window = TimeSpan.FromMinutes(settings.Value.RateLimitWindowMinutes);
        _adminUserIds = settings.Value.AdminUserIds ?? Array.Empty<long>(); // Защита от null
        _logger = logger;

        _cleanupTimer = new Timer(
            CleanupExpiredEntries,
            null,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(10));
    }

    public async Task<bool> IsAllowedAsync(long userId)
    {
        // Админы всегда могут проходить
        if (_adminUserIds.Contains(userId))
            return true;

        var now = DateTime.UtcNow;
        var limit = _userLimits.GetOrAdd(userId, _ => new UserRateLimit());

        // Блокируем только на время обновления счетчика
        lock (limit)
        {
            // Проверяем, не истекло ли окно
            if (now - limit.WindowStart > _window)
            {
                limit.WindowStart = now;
                limit.Count = 0;
            }

            // Проверяем лимит
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

    private async void CleanupExpiredEntries(object? state)
    {
        try
        {
            await CleanupExpiredEntriesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cleanup failed");
        }
    }

    private async Task CleanupExpiredEntriesAsync()
    {
        if (!await _cleanupLock.WaitAsync(0))
        {
            _logger.LogDebug("Cleanup already in progress, skipping");
            return;
        }

        try
        {
            var now = DateTime.UtcNow;
            var expirationThreshold = _window * 2;

            // Собираем ключи для удаления
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

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Останавливаем таймер
        _cleanupTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _cleanupTimer?.Dispose();

        _cleanupLock?.Dispose();
        _userLimits.Clear();

        GC.SuppressFinalize(this);
    }

    private class UserRateLimit
    {
        public DateTime WindowStart { get; set; } = DateTime.UtcNow;
        public int Count { get; set; }
    }
}