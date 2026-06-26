namespace Hiram.Application.Notifications;

public enum ReplayOutcome
{
    Replayed,
    NotFound,
    NotDeadLettered,
    AlreadyReplayed
}
