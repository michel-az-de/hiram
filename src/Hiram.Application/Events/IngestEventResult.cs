using Hiram.Domain.Events;

namespace Hiram.Application.Events;

public sealed record IngestEventResult(Guid Id, EventStatus Status, bool Replayed = false);
