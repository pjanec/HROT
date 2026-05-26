using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Time.Controllers;
using Xunit;

namespace Hrot.Animation.Integration.Tests;

/// <summary>
/// Test harness for testing PumpUntil implementation.
/// Minimal implementation of IPumpableHarness for unit tests.
/// </summary>
internal class MinimalTestHarness : IPumpableHarness
{
    private int _frameCount;

    public EntityRepository World => throw new NotImplementedException();
    public FdpEventBus EventBus => throw new NotImplementedException();
    public SteppingTimeController Time => throw new NotImplementedException();

    public void PumpFrame(float dt = 1f / 60f) => _frameCount++;

    public void PumpFrames(int count, float dt = 1f / 60f)
    {
        for (int i = 0; i < count; i++)
            PumpFrame(dt);
    }

    public void PumpUntil(
        Func<bool> condition,
        int maxFrames,
        string conditionName,
        Func<string>? diagnosticDump = null,
        float dt = 1f / 60f)
    {
        this.PumpUntilImpl(condition, maxFrames, conditionName, diagnosticDump, dt);
    }

    public int FrameCount => _frameCount;
    public void ResetFrameCount() => _frameCount = 0;
}

/// <summary>
/// Unit tests for PumpUntil infrastructure (ANC-P7-01).
/// </summary>
public class PumpUntilInfrastructureTests
{
    [Fact]
    public void PumpUntil_ReturnsImmediatelyIfConditionTrue()
    {
        // Arrange
        var harness = new MinimalTestHarness();

        // Act & Assert (should not throw)
        harness.PumpUntil(
            () => true,
            maxFrames: 100,
            conditionName: "Always true");

        // Verify: no frames pumped
        Assert.Equal(0, harness.FrameCount);
    }

    [Fact]
    public void PumpUntil_BlocksUntilConditionBecomesTrueWithinBudget()
    {
        // Arrange
        var harness = new MinimalTestHarness();
        int targetFrame = 50;
        int currentFrame = 0;

        // Act & Assert (should not throw)
        harness.PumpUntil(
            () =>
            {
                currentFrame++;
                return currentFrame >= targetFrame;
            },
            maxFrames: 100,
            conditionName: "Reach frame 50");

        // Verify: pumped until condition true
        // Note: currentFrame is incremented in the condition function, so it will be 50 when the condition returns true.
        // But FrameCount only increments when PumpFrame is called, which happens AFTER the condition returns false.
        // So when currentFrame reaches 50 and returns true, PumpFrame hasn't been called yet.
        // Therefore, FrameCount = 49, currentFrame = 50.
        Assert.Equal(targetFrame, currentFrame);
        Assert.Equal(targetFrame - 1, harness.FrameCount);
    }

    [Fact]
    public void PumpUntil_ThrowsTimeoutExceptionIfMaxFramesExceeded()
    {
        // Arrange
        var harness = new MinimalTestHarness();

        // Act & Assert: should throw TimeoutException
        var ex = Assert.Throws<TimeoutException>(() =>
            harness.PumpUntil(
                () => false, // Never true
                maxFrames: 50,
                conditionName: "Never true condition",
                diagnosticDump: () => "Test diagnostic dump"));

        // Verify exception message contains key info
        Assert.Contains("timeout", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("50", ex.Message); // maxFrames
        Assert.Contains("Never true condition", ex.Message); // conditionName
        Assert.Contains("Test diagnostic dump", ex.Message); // diagnostic
    }

    [Fact]
    public void PumpUntil_DiagnosticDumpNotInvokedOnSuccess()
    {
        // Arrange
        var harness = new MinimalTestHarness();
        bool diagnosticInvoked = false;

        // Act
        harness.PumpUntil(
            () => true,
            maxFrames: 100,
            conditionName: "Always true",
            diagnosticDump: () =>
            {
                diagnosticInvoked = true;
                return "diagnostic";
            });

        // Assert: diagnostic was not called
        Assert.False(diagnosticInvoked);
    }

    [Fact]
    public void PumpUntil_DiagnosticDumpInvokedOnTimeout()
    {
        // Arrange
        var harness = new MinimalTestHarness();
        bool diagnosticInvoked = false;

        // Act & Assert
        var ex = Assert.Throws<TimeoutException>(() =>
            harness.PumpUntil(
                () => false,
                maxFrames: 10,
                conditionName: "Never true",
                diagnosticDump: () =>
                {
                    diagnosticInvoked = true;
                    return "diagnostic output";
                }));

        // Assert: diagnostic was called
        Assert.True(diagnosticInvoked);
    }
}
