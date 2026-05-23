using FluentAssertions;
using Hrot.BTree.Editor.Debug;
using Hrot.Editor.AiShared.Debug;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

// Spy coordinator that records which control methods were called.
file sealed class SpyCoordinator : AiTracerCoordinator
{
    public bool StepOneTickRequested;
    public bool PauseRequested;
    public bool ContinueRequested;
    public override void RequestStepOneTick() => StepOneTickRequested = true;
    public override void RequestPause()       => PauseRequested       = true;
    public override void RequestContinue()    => ContinueRequested    = true;
}

public sealed class BTreeStepControlTests
{
    [Fact]
    public void StepOver_calls_RequestStepOneTick()
    {
        var spy = new SpyCoordinator();
        var session = new BTreeDebugSession(spy);
        session.StepOver();
        spy.StepOneTickRequested.Should().BeTrue();
    }

    [Fact]
    public void StepInto_calls_RequestStepOneTick()
    {
        var spy = new SpyCoordinator();
        var session = new BTreeDebugSession(spy);
        session.StepInto();
        spy.StepOneTickRequested.Should().BeTrue();
    }

    [Fact]
    public void StepOut_calls_RequestStepOneTick()
    {
        var spy = new SpyCoordinator();
        var session = new BTreeDebugSession(spy);
        session.StepOut();
        spy.StepOneTickRequested.Should().BeTrue();
    }

    [Fact]
    public void Pause_calls_RequestPause()
    {
        var spy = new SpyCoordinator();
        var session = new BTreeDebugSession(spy);
        session.Pause();
        spy.PauseRequested.Should().BeTrue();
    }

    [Fact]
    public void Continue_after_Pause_calls_RequestContinue()
    {
        var spy = new SpyCoordinator();
        var session = new BTreeDebugSession(spy);
        // Must be paused first for Continue() to act.
        session.Pause();
        session.Continue();
        spy.ContinueRequested.Should().BeTrue();
    }

    [Fact]
    public void Continue_without_Pause_does_not_call_RequestContinue()
    {
        var spy = new SpyCoordinator();
        var session = new BTreeDebugSession(spy);
        session.Continue();
        spy.ContinueRequested.Should().BeFalse();
    }

    [Fact]
    public void Pause_twice_does_not_call_RequestPause_second_time()
    {
        // First Pause sets IsPaused; second Pause should be a no-op.
        var spy = new SpyCoordinator();
        var session = new BTreeDebugSession(spy);
        session.Pause();
        spy.PauseRequested = false;
        session.Pause();
        spy.PauseRequested.Should().BeFalse();
    }
}
