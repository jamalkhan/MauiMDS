namespace Rizedown.Services;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
    DateTimeOffset Now { get; }
}
