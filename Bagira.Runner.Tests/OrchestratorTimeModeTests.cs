using System;
using System.Threading;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Runner.Services;
using CycloneDDS.Runtime;
using FDP.Framework.Runner;
using FDP.Toolkit.Time.Messages;
using ModuleHost.Core.Time;

namespace Bagira.Runner.Tests;

/// <summary>
/// CGF1-A.1 (BATCH-09): Verifies that <see cref="OrchestratorSubsystem"/> consumes
/// <see cref="Bagira.Orchestrator.DrillMaster.PendingTimeMode"/> and drives
/// <see cref="FDP.Toolkit.Time.Controllers.DistributedTimeCoordinator.SwitchToDeterministic"/>
/// when a <c>TransitionState → LoadingLive</c> request carries
/// <c>"TimeMode": "Deterministic"</c> in its payload.
/// </summary>
[Collection("OrchestratorTimeModeTests")]
public class OrchestratorTimeModeTests
{
    // Domain 15 is reserved for all orchestrator tests (shared with DrillMasterBootstrapTests
    // via the same xunit.runner.json serial constraint on the Bagira.Orchestrator.Tests assembly;
    // this assembly uses its own serial collection to avoid domain contention).
    private const int TestDomain = 15;

    /// <summary>
    /// When OrchestratorSubsystem ticks after a SysOpRequest with
    /// <c>"TimeMode":"Deterministic"</c> heading toward LoadingLive is processed by DrillMaster,
    /// the internal DistributedTimeCoordinator must publish a <see cref="SwitchTimeModeEvent"/>
    /// with <see cref="FDP.Toolkit.Time.Messages.TimeMode.Deterministic"/> to the event bus.
    /// </summary>
    [Fact(Timeout = 20_000)]
    public void PendingTimeMode_Deterministic_PublishesSwitchTimeModeEvent()
    {
        var subsystem = new OrchestratorSubsystem();
        subsystem.Initialize(new SubsystemConfig { DomainId = TestDomain });

        using var testParticipant = new DdsParticipant(TestDomain);
        using var sysOpWriter     = new DdsWriter<SysOpRequest>(testParticipant);

        // Allow DDS endpoint discovery to settle.
        Thread.Sleep(400);

        // Send TransitionState → LoadingLive with Deterministic time mode.
        sysOpWriter.Write(new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.TransitionState,
            // DSMState.LoadingLive = 30; JSON object form to carry TimeMode property.
            PayloadJson   = @"{""TargetState"":30,""TimeMode"":""Deterministic""}",
        });

        // Tick until a SwitchTimeModeEvent with TargetMode=Deterministic appears on the bus.
        SwitchTimeModeEvent? captured = null;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            subsystem.Update(1f / 60f);
            var bus = subsystem.TimeBusForTest;
            if (bus != null)
            {
                foreach (var ev in bus.Consume<SwitchTimeModeEvent>())
                {
                    if (ev.TargetMode == TimeMode.Deterministic)
                    {
                        captured = ev;
                        break;
                    }
                }
            }
            if (captured.HasValue) break;
            Thread.Sleep(20);
        }

        subsystem.Shutdown();

        Assert.True(captured.HasValue,
            "OrchestratorSubsystem did not publish SwitchTimeModeEvent(Deterministic) " +
            "after DrillMaster.PendingTimeMode was set to 'Deterministic'.");
        Assert.Equal(TimeMode.Deterministic, captured!.Value.TargetMode);
        Assert.True(captured.Value.BarrierWallTicks > 0,
            "BarrierWallTicks must be a future wall-tick value (> 0).");
    }

    /// <summary>
    /// When the payload does not include <c>"TimeMode":"Deterministic"</c>, transitions
    /// should NOT cause a <see cref="SwitchTimeModeEvent"/> to be published.
    /// </summary>
    [Fact(Timeout = 15_000)]
    public void PendingTimeMode_Absent_DoesNotPublishSwitchTimeModeEvent()
    {
        var subsystem = new OrchestratorSubsystem();
        subsystem.Initialize(new SubsystemConfig { DomainId = TestDomain });

        using var testParticipant = new DdsParticipant(TestDomain);
        using var sysOpWriter     = new DdsWriter<SysOpRequest>(testParticipant);

        Thread.Sleep(400);

        // Send without TimeMode property (plain integer payload).
        sysOpWriter.Write(new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.TransitionState,
            PayloadJson   = ((int)DSMState.LoadingLive).ToString(),
        });

        bool seenDeterministicEvent = false;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            subsystem.Update(1f / 60f);
            var bus = subsystem.TimeBusForTest;
            if (bus != null)
            {
                foreach (var ev in bus.Consume<SwitchTimeModeEvent>())
                {
                    if (ev.TargetMode == TimeMode.Deterministic)
                        seenDeterministicEvent = true;
                }
            }
            Thread.Sleep(20);
        }

        subsystem.Shutdown();

        Assert.False(seenDeterministicEvent,
            "No SwitchTimeModeEvent(Deterministic) should be emitted when TimeMode is absent.");
    }
}

/// <summary>xUnit collection definition for serial execution of orchestrator time-mode tests on domain 15.</summary>
[CollectionDefinition("OrchestratorTimeModeTests", DisableParallelization = true)]
public class OrchestratorTimeModeCollection { }
