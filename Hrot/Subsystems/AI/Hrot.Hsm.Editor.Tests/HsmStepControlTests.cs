using FluentAssertions;
using Hrot.Editor.AiShared.Debug;
using Hrot.Hsm.Editor.Debug;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

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

public sealed class HsmStepControlTests
{
    [Fact]
    public void StepOver_calls_RequestStepOneTick()
    {
        var spy = new SpyCoordinator();
        var session = new HsmDebugSession(spy);
        session.StepOver();
        spy.StepOneTickRequested.Should().BeTrue();
    }

    [Fact]
    public void StepInto_calls_RequestStepOneTick()
    {
        var spy = new SpyCoordinator();
        var session = new HsmDebugSession(spy);
        session.StepInto();
        spy.StepOneTickRequested.Should().BeTrue();
    }

    [Fact]
    public void StepOut_calls_RequestStepOneTick()
    {
        var spy = new SpyCoordinator();
        var session = new HsmDebugSession(spy);
        session.StepOut();
        spy.StepOneTickRequested.Should().BeTrue();
    }

    [Fact]
    public void Pause_calls_RequestPause()
    {
        var spy = new SpyCoordinator();
        var session = new HsmDebugSession(spy);
        session.Pause();
        spy.PauseRequested.Should().BeTrue();
    }

    [Fact]
    public void Continue_after_Pause_calls_RequestContinue()
    {
        var spy = new SpyCoordinator();
        var session = new HsmDebugSession(spy);
        session.Pause();
        session.Continue();
        spy.ContinueRequested.Should().BeTrue();
    }

    [Fact]
    public void Continue_without_Pause_does_not_call_RequestContinue()
    {
        var spy = new SpyCoordinator();
        var session = new HsmDebugSession(spy);
        session.Continue();
        spy.ContinueRequested.Should().BeFalse();
    }

    [Fact]
    public void Pause_twice_does_not_call_RequestPause_second_time()
    {
        var spy = new SpyCoordinator();
        var session = new HsmDebugSession(spy);
        session.Pause();
        spy.PauseRequested = false;
        session.Pause();
        spy.PauseRequested.Should().BeFalse();
    }
}
