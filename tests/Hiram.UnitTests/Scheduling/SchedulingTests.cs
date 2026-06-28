using Hiram.Application.Scheduling;

namespace Hiram.UnitTests.Scheduling;

public class SchedulingTests
{
    // Fixed-offset zone so the test does not depend on the ICU/IANA database being present.
    private static readonly TimeZoneInfo Tenant = TimeZoneInfo.CreateCustomTimeZone("tenant-3", TimeSpan.FromHours(-3), "tenant-3", "tenant-3");
    private static readonly DateTimeOffset OffWindow = new(2026, 6, 28, 2, 0, 0, TimeSpan.FromHours(-3));

    [Fact]
    public void OffWindowEvent_DefersToWindowOpen_InTenantTz()
    {
        var scheduler = new WindowScheduler(() => TimeSpan.Zero);

        var dispatchAt = scheduler.ComputeDispatchAt(OffWindow, new TimeOnly(8, 0), new TimeOnly(20, 0), Tenant);

        Assert.Equal(new DateTimeOffset(2026, 6, 28, 8, 0, 0, TimeSpan.FromHours(-3)), dispatchAt);
    }

    [Fact]
    public void WindowOpen_SpreadsByJitter_NoSpike()
    {
        var jitters = new Queue<TimeSpan>([TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(9)]);
        var scheduler = new WindowScheduler(() => jitters.Dequeue());

        var d1 = scheduler.ComputeDispatchAt(OffWindow, new TimeOnly(8, 0), new TimeOnly(20, 0), Tenant);
        var d2 = scheduler.ComputeDispatchAt(OffWindow, new TimeOnly(8, 0), new TimeOnly(20, 0), Tenant);
        var d3 = scheduler.ComputeDispatchAt(OffWindow, new TimeOnly(8, 0), new TimeOnly(20, 0), Tenant);

        Assert.True(d1 != d2 && d2 != d3 && d1 != d3);
    }

    [Fact]
    public void DailyLimit_BlocksExcess()
    {
        var policy = new DailyLimitPolicy();

        Assert.True(policy.IsExceeded(currentCount: 5, dailyLimit: 5));
        Assert.False(policy.IsExceeded(currentCount: 4, dailyLimit: 5));
        Assert.False(policy.IsExceeded(currentCount: 100, dailyLimit: 0));
    }
}
