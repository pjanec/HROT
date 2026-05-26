using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Time.Controllers;
using Hrot.SimHost;

namespace Hrot.Animation.Integration.Tests;

/// <summary>
/// Interface for test harnesses that support frame-limited polling with <see cref="PumpUntil"/>.
/// Used in layer-3 integration tests to advance simulation deterministically until a condition is met or timeout occurs.
/// </summary>
public interface IPumpableHarness
{
    /// <summary>
    /// The entity repository containing all simulation entities and components.
    /// </summary>
    EntityRepository World { get; }

    /// <summary>
    /// The event bus for publishing and reading typed events.
    /// </summary>
    FdpEventBus EventBus { get; }

    /// <summary>
    /// The deterministic time controller (typically <see cref="SteppingTimeController"/>).
    /// </summary>
    SteppingTimeController Time { get; }

    /// <summary>
    /// Advance simulation by one frame at the specified delta time.
    /// </summary>
    /// <param name="dt">Delta time in seconds. Default: 1/60 (60 Hz).</param>
    void PumpFrame(float dt = 1f / 60f);

    /// <summary>
    /// Advance simulation by multiple frames at the specified delta time.
    /// </summary>
    /// <param name="count">Number of frames to advance.</param>
    /// <param name="dt">Delta time per frame in seconds. Default: 1/60 (60 Hz).</param>
    void PumpFrames(int count, float dt = 1f / 60f);

    /// <summary>
    /// Advance simulation until a condition is true or max frames exceeded (timeout).
    /// </summary>
    /// <param name="condition">Predicate to check each frame.</param>
    /// <param name="maxFrames">Maximum frames to pump before timing out.</param>
    /// <param name="conditionName">Human-readable name of the condition (for error messages).</param>
    /// <param name="diagnosticDump">Optional callback to generate diagnostic output on timeout.</param>
    /// <param name="dt">Delta time per frame in seconds. Default: 1/60 (60 Hz).</param>
    /// <exception cref="TimeoutException">Thrown if condition not met after maxFrames.</exception>
    void PumpUntil(
        Func<bool> condition,
        int maxFrames,
        string conditionName,
        Func<string>? diagnosticDump = null,
        float dt = 1f / 60f);
}

/// <summary>
/// Extension methods for <see cref="IPumpableHarness"/>.
/// </summary>
public static class PumpableHarnessExtensions
{
    /// <summary>
    /// Default implementation of <see cref="IPumpableHarness.PumpUntil"/>.
    /// Blocks until condition true OR maxFrames exceeded.
    /// On timeout, invokes diagnosticDump (if provided) and throws TimeoutException.
    /// Checks condition first; pumps only if condition is false.
    /// </summary>
    public static void PumpUntilImpl(
        this IPumpableHarness harness,
        Func<bool> condition,
        int maxFrames,
        string conditionName,
        Func<string>? diagnosticDump = null,
        float dt = 1f / 60f)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            if (condition())
                return; // Success!
            
            harness.PumpFrame(dt);
        }

        // Timeout: invoke diagnostic dump and throw
        var dump = diagnosticDump?.Invoke() ?? "(no diagnostic available)";
        var message = $"PumpUntil timeout after {maxFrames} frames (~{maxFrames * dt:F2}s) waiting for: {conditionName}\n{dump}";
        throw new TimeoutException(message);
    }
}
