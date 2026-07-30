using MaybeComingBot.Models;

namespace MaybeComingBot.Abstractions;

public interface IUserStateService
{
    UserState GetOrCreate(long userId);
    bool TryGet(long userId, out UserState state);
    void SetStop(long userId, string stopId);
    void SetUserInfo(long userId, string firstName, string? lastName, string? username);
    void Clear(long userId);
    void UpdateActivity(long userId);
    IEnumerable<long> GetExpiredKeys(TimeSpan expiry);
    bool HasStopSelected(long userId);
    int GetActiveUsersCount();
}