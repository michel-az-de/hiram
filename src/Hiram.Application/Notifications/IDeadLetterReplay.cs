namespace Hiram.Application.Notifications;

public interface IDeadLetterReplay
{
    Task<ReplayOutcome> ReplayAsync(Guid tenantId, Guid notificationId, CancellationToken cancellationToken);
}
