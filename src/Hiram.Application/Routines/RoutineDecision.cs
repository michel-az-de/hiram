namespace Hiram.Application.Routines;

public sealed record RoutineDecision(
    IReadOnlyList<FanoutItem> Fanout,
    IReadOnlyList<SuppressedItem> Suppressed,
    bool NoRoute)
{
    public static RoutineDecision NoMatch() => new([], [], NoRoute: true);
}
