namespace Rizedown.Services;

public interface IDelayScheduler
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default);
}
