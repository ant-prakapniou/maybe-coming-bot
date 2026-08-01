namespace MaybeComingBot.Constants;

public static class TimeConstants
{
    // Базовые константы времени
    public const int MinutesInHour = 60;
    public const int MinutesInDay = 24 * MinutesInHour; // 1440

    // Граничные значения для времени
    public const int MaxValidMinutes = MinutesInDay - 1; // 1439 (23:59)
    public const int MaxMinutesInDay = MinutesInDay;     // 1440 (24:00)

    // Константы для retry политики
    public const int BaseRetryDelayMinutes = 5;
    public const int MaxRetryDelayMinutes = 240; // 4 часа
    public const int MaxRetryBackoffFailures = 30;
}