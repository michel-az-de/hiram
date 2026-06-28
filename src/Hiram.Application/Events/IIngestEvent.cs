namespace Hiram.Application.Events;

public interface IIngestEvent
{
    Task<IngestEventResult> IngestAsync(IngestEventCommand command, CancellationToken cancellationToken);
}
