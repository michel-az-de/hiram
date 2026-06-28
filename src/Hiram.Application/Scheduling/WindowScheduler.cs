namespace Hiram.Application.Scheduling;

// Computes when a message may be sent. Inside the window, send now (jittered to avoid a spike). Off the
// window, defer to the next window open in the tenant timezone (jittered). Suppression is never the
// answer: the EasyStok cron sends at window open, so deferral preserves parity (ADR-020).
public sealed class WindowScheduler
{
    private readonly Func<TimeSpan> _jitter;

    public WindowScheduler(Func<TimeSpan> jitter)
    {
        _jitter = jitter;
    }

    public DateTimeOffset ComputeDispatchAt(DateTimeOffset now, TimeOnly? windowStart, TimeOnly? windowEnd, TimeZoneInfo timeZone)
    {
        if (windowStart is null || windowEnd is null)
            return now;

        var local = TimeZoneInfo.ConvertTime(now, timeZone);
        var localTime = TimeOnly.FromDateTime(local.DateTime);

        if (IsWithin(localTime, windowStart.Value, windowEnd.Value))
            return now + _jitter();

        return NextOpen(local, windowStart.Value) + _jitter();
    }

    // Handles overnight windows (start after end), where the open period wraps past midnight.
    private static bool IsWithin(TimeOnly time, TimeOnly start, TimeOnly end) =>
        start <= end ? time >= start && time < end : time >= start || time < end;

    private static DateTimeOffset NextOpen(DateTimeOffset local, TimeOnly start)
    {
        var candidate = new DateTimeOffset(local.Year, local.Month, local.Day, start.Hour, start.Minute, 0, local.Offset);
        return candidate <= local ? candidate.AddDays(1) : candidate;
    }
}
