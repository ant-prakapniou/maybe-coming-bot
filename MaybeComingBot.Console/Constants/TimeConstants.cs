namespace MaybeComingBot.Constants;

public static class TimeConstants
{
    public const int MinutesInHour = 60;
    public const int MinutesInDay = 24 * 60;
    public const int MaxMinutesInDay = MinutesInDay - 1;
    public const int MaxRetryDelayMinutes = 240;
    public const int BaseRetryDelayMinutes = 5;
    public const int MaxRetryBackoffFailures = 30;
}