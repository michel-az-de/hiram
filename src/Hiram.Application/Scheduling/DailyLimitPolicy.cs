namespace Hiram.Application.Scheduling;

public sealed class DailyLimitPolicy
{
    // A limit of zero (or less) means no limit.
    public bool IsExceeded(int currentCount, int dailyLimit) => dailyLimit > 0 && currentCount >= dailyLimit;
}
