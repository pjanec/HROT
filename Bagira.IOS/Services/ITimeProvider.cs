namespace Bagira.IOS.Services;

/// <summary>
/// Abstraction over time retrieval. Injected into RequestTransactionManager so that
/// unit tests can control the clock deterministically without using Thread.Sleep().
/// </summary>
public interface ITimeProvider
{
    DateTime UtcNow { get; }
}

/// <summary>
/// Production implementation that delegates to the real system clock.
/// </summary>
public sealed class SystemTimeProvider : ITimeProvider
{
    public static readonly SystemTimeProvider Instance = new();
    public DateTime UtcNow => DateTime.UtcNow;
}
