using MaybeComingBot.Utils;

namespace MaybeComingBot.Models;

public sealed class Trip
{
    private string _time = string.Empty;
    private string _note = string.Empty;
    private int _bus;

    public Trip() { }

    public Trip(string time, int bus, string? note = null)
    {
        Time = time ?? throw new ArgumentNullException(nameof(time));
        Bus = bus > 0 ? bus : throw new ArgumentException("Bus must be > 0", nameof(bus));
        Note = note ?? string.Empty;
    }

    public string Time
    {
        get => _time;
        set => _time = value ?? string.Empty;
    }

    public int Bus
    {
        get => _bus;
        set => _bus = value > 0 ? value : 0;
    }

    public string Note
    {
        get => _note;
        set => _note = value ?? string.Empty;
    }

    public int? ToMinutes()
    {
        return TimeHelper.ToMinutes(Time);
    }

    public int ToMinutesSafe(int defaultValue = 0)
    {
        return TimeHelper.ToMinutesSafe(Time, defaultValue);
    }

    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(Time) &&
               TimeHelper.IsValidTime(Time) &&
               Bus > 0;
    }

    public override string ToString()
    {
        return string.IsNullOrEmpty(Note)
            ? $"{Time} (Bus {Bus})"
            : $"{Time} (Bus {Bus}, {Note})";
    }
}