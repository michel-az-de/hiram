namespace Hiram.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
