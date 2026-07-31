namespace MaybeComingBot.Abstractions;

public interface IScheduleRefreshService
{
    Task ForceRefreshAsync(CancellationToken ct = default);
    string GetStatus();
    string GetLastAttemptInfo();
    DateTime LastSuccessfulUpdate { get; }
    int FailureCount { get; }
}