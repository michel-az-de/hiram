namespace Hiram.Application.Notifications;

public interface ISubmitNotification
{
    Task<SubmitNotificationResult> SubmitAsync(SubmitNotificationCommand command, CancellationToken cancellationToken);
}
