using System;
using System.Threading;
using CycloneDDS.Runtime;
using Fdp.Toolkit.Runner;
using Fdp.Toolkit.Replication.Services;
using Fdp.Core;
using Hrot.CGF;
using Hrot.ClusterRunner.Services;
using Hrot.Common;
using Hrot.ExCon;
using Hrot.IG;
using Hrot.Map.Common;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Network.NED.Factory;
using Hrot.Orchestrator;
using Hrot.SimHost;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Domain-isolated orchestration harness for Runner integration tests.
///
/// <para>
/// <see cref="OrchestratorSubsystem"/> is the FIRST subsystem in the list so that
/// <see cref="ClusterMaster"/> (and its embedded <c>DdsIdAllocatorServer</c>) are running
/// before <see cref="SimHostSubsystem"/> initialises. This ensures that
/// <c>SimHostApp.EnsureIdAllocatorRouting</c> finds a publication match immediately
/// instead of waiting up to 30 s before throwing.
/// </para>
/// </summary>
public sealed class HrotRunnerHarness : IDisposable
{
    private const int DomainIdBase = 100;
    // Warmup is intentionally short: OrchestratorSubsystem (first in the list) starts
    // DdsIdAllocatorServer before SimHostSubsystem calls EnsureIdAllocatorRouting, so the
    // ID-allocator match is near-instant. CycloneDDS loopback SPDP/SEDP discovery for the
    // remaining application topics completes within ~200 ms on typical hardware.
    private const int WarmupFrames = 20;    // 20 × 5 ms = 100 ms of pumped frames
    private const int PumpSleepMs = 5;
    /// <summary>
    /// Extra wall-clock sleep AFTER warmup frames, allowing DDS SPDP/SEDP discovery to complete
    /// for any topic whose reader/writer pair was not yet matched by the last warmup frame.
    /// 200 ms is sufficient for loopback CycloneDDS discovery on all topics used by the harness.
    /// </summary>
    private const int PostWarmupSettleMs = 200;
    /// <summary>
    /// Extra frames pumped after the standard warmup when CGF is present.
    /// <c>ClusterSlave</c> fires the first <c>NodeHeartbeat</c> after 1 s of real time.
    /// <c>BrainMuscleOwnershipStrategy</c> needs at least one heartbeat from SimHost before it
    /// can delegate WorldPos authority to a MuscleGround node on entity creation.
    /// 220 frames × 5 ms sleep = 1 100 ms, which is safely longer than 1 s.
    /// </summary>
    private const int CgfHeartbeatWarmupFrames = 220;

    private static int _domainCounter = DomainIdBase - 1;

    public int DomainId { get; }
    public SubsystemOrchestrator Orchestrator { get; }
    public OrchestratorSubsystem OrchestratorSvc { get; }
    public SimHostSubsystem SimHost { get; }
    public IgSubsystem Ig { get; }
    public ExConSubsystem ExCon { get; }
    public CgfSubsystem? Cgf { get; private set; }

    // Shared DDS participant owned by the harness; disposed after Orchestrator.Shutdown().
    private readonly DdsParticipant _participant;

    public HrotRunnerHarness()
    {
        DomainId = Interlocked.Increment(ref _domainCounter);

        // Create a single shared DDS participant for the harness domain.
        // All subsystems share this participant so the composition root (this harness)
        // owns the DDS lifecycle, matching the hexagonal architecture requirement.
        _participant = HrotEnvironment.CreateParticipant(DomainId);
        var factory = new NedNetworkFactory(
            participant:  _participant,
            entityMap:    new NetworkEntityMap(),
            geoTransform: HrotEnvironment.CreateGeoTransform(),
            eventBus:     new FdpEventBus(),
            localNodeId:  0,
            role:         NodeRole.MuscleGround | NodeRole.Perception);

        OrchestratorSvc = new OrchestratorSubsystem(factory);  // HEXAG2-S009: factory required
        SimHost = new SimHostSubsystem(factory);
        Ig = new IgSubsystem(factory);
        ExCon = new ExConSubsystem(factory);
        Cgf = new CgfSubsystem(factory);  // CGF processes CreateEntityRequest and sends ACKs

        var options = new RunnerOptions { Headless = true, DomainId = DomainId };
        Orchestrator = new SubsystemOrchestrator(new ISubsystem[]
        {
            OrchestratorSvc,   // must be first: starts DdsIdAllocatorServer before SimHost
            SimHost,
            Ig,
            ExCon,
            Cgf,
        }, options);

        BootOrCleanUp();
    }

    /// <summary>
    /// Creates a harness with a specific set of subsystem names and domain ID (for shared-domain tests).
    /// Typically used alongside <see cref="CgfHarness(int)"/> for IT-4 tests.
    /// <para>Subsystem names are comma-separated and case-insensitive: simhost, ig, excon, cgf.</para>
    /// </summary>
    public HrotRunnerHarness(string modes, int domainId)
    {
        DomainId = domainId;

        var requested = new System.Collections.Generic.HashSet<string>(
            modes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);

        // Create a single shared DDS participant for the harness domain.
        _participant = HrotEnvironment.CreateParticipant(domainId);
        var factory = new NedNetworkFactory(
            participant:  _participant,
            entityMap:    new NetworkEntityMap(),
            geoTransform: HrotEnvironment.CreateGeoTransform(),
            eventBus:     new FdpEventBus(),
            localNodeId:  0,
            role:         NodeRole.MuscleGround | NodeRole.Perception);

        OrchestratorSvc = new OrchestratorSubsystem(factory);  // HEXAG2-S009: factory required
        SimHost         = new SimHostSubsystem(factory);
        Ig              = new IgSubsystem(factory);
        ExCon           = new ExConSubsystem(factory);

        // Always include Orchestrator; conditionally include other subsystems.
        var subsystems = new System.Collections.Generic.List<ISubsystem> { OrchestratorSvc };
        if (requested.Contains("simhost")) subsystems.Add(SimHost);
        if (requested.Contains("ig"))      subsystems.Add(Ig);
        if (requested.Contains("excon"))   subsystems.Add(ExCon);
        if (requested.Contains("cgf"))
        {
            Cgf = new CgfSubsystem(factory);
            subsystems.Add(Cgf);
        }

        var options = new RunnerOptions { Headless = true, DomainId = domainId };
        Orchestrator = new SubsystemOrchestrator(subsystems, options);

        BootOrCleanUp();
    }

    /// <summary>
    /// ⭐⭐⭐ <c>QA-002</c> — <b>a constructor that throws must not leak a DDS participant.</b>
    ///
    /// <para>⛔ xUnit does NOT call <see cref="IDisposable.Dispose"/> on an instance whose constructor
    /// threw. 📐 Measured 2026-08-26: when <c>Initialize()</c> failed (an
    /// <c>OutOfMemoryException</c> out of <c>EntityIndex..ctor</c>), the already-created
    /// <see cref="DdsParticipant"/> and its background <c>HostedIdAllocatorServer</c> poll thread
    /// survived for the life of the process; the abandoned reader later raised
    /// <c>dds_take failed: -3</c> on that thread and killed the test host.</para>
    ///
    /// <para>⭐ So the boot is wrapped: on failure everything already constructed is torn down and the
    /// ORIGINAL exception is rethrown, so the test still fails for its real reason — ⛔ this hides
    /// nothing.</para>
    /// </summary>
    private void BootOrCleanUp()
    {
        try
        {
            Orchestrator.Initialize();
            Warmup();
        }
        catch
        {
            try { Dispose(); } catch { /* teardown of a half-built harness is best-effort */ }
            throw;
        }
    }

    public void PumpFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            Orchestrator.RunFrames(1);
        }
    }

    public bool PumpUntil(Func<bool> condition, int timeoutFrames = 300)
    {
        if (condition()) return true;

        for (int i = 0; i < timeoutFrames; i++)
        {
            Orchestrator.RunFrames(1);
            Thread.Sleep(PumpSleepMs);

            if (condition()) return true;
        }

        return false;
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // QA-002: this also runs from BootOrCleanUp on a HALF-BUILT harness, where a subsystem may
        // never have been initialised. Shutdown must not mask the original boot failure.
        try
        {
            Orchestrator.Shutdown();
        }
        catch (Exception)
        {
            // Intentional no-op: see the participant note below — the same reasoning applies to a
            // subsystem that cannot shut down cleanly because it never came up.
        }

        // Dispose the shared participant after all DDS readers/writers owned by the
        // subsystems have been torn down inside Shutdown().
        // Defensive: if a prior exception left DDS readers in a bad state (e.g. dds_take
        // failed: -3 / ReturnCode.BadParameter), suppress the teardown exception so it
        // cannot abort the test host process and prevent remaining tests from executing.
        try
        {
            _participant.Dispose();
        }
        catch (Exception)
        {
            // Intentional no-op: DDS teardown errors after abnormal shutdown must not
            // propagate as unhandled exceptions and kill the xUnit test host.
        }
    }

    private void Warmup()
    {
        for (int i = 0; i < WarmupFrames; i++)
        {
            Orchestrator.RunFrames(1);
            Thread.Sleep(PumpSleepMs);
        }

        // Extra settle time: give CycloneDDS SPDP/SEDP discovery time to complete for all
        // topics (EntityMaster, GeoSpatial, CreateEntityRequest/Ack, MissionControlRequest/Ack,
        // etc.) even when the process starts cold (no DDS participant has run before).
        Thread.Sleep(PostWarmupSettleMs);

        // When CGF is present, BrainMuscleOwnershipStrategy must know about SimHost before any
        // entity is created via CreateEntityRequest. SimHost's ClusterSlave fires its first
        // NodeHeartbeat after 1 s of real time. Pump CgfHeartbeatWarmupFrames extra frames
        // (>1100 ms) so the heartbeat is received and the cluster cache is populated before
        // any test action is taken.
        if (Cgf != null)
        {
            for (int i = 0; i < CgfHeartbeatWarmupFrames; i++)
            {
                Orchestrator.RunFrames(1);
                Thread.Sleep(PumpSleepMs);
            }
        }

        ResumeTimeAfterBoot();
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-148</c> — RESUME THE CLOCK, because production now boots PAUSED.</b>
    ///
    /// <para>🔴 <b>Without this the harness pumps a STOPPED CLOCK.</b> 📐 Measured `2026-09-01`:
    /// after 120 pumped frames <c>view.Tick = 361</c> — the ECS world IS ticking — while
    /// <c>GlobalTime</c> reads <c>DeltaTime=0 · TimeScale=1 · TotalTime=0.000 · FrameNumber=1</c>
    /// and <c>SimHost TimeControllerMode = Deterministic</c>. ⇒ every system that integrates over
    /// <c>DeltaTime</c> does nothing, silently, and a vehicle given a destination never moves.</para>
    ///
    /// <para>📌 <b>Why it appeared:</b> <c>CE-101</c> (<c>60daaaf6d</c>, <c>2026-08-28</c>) made
    /// <c>--mode all</c> boot paused — a CORRECT fix to a real user-visible defect — by having
    /// <c>OrchestratorSubsystem</c> pass <c>startPaused: true</c>. ⛔ This harness constructs that same
    /// subsystem and nobody told it. ⚠⚠ It went unnoticed for four days because <c>CE-101</c> gated on a
    /// live boot and NOT on this suite, which <c>BP-378</c> then claimed could not be gated — a claim
    /// already false by that date. ⇒ 🔒 <b>the filter-around caused the regression it was hiding.</b></para>
    ///
    /// <para>⭐⭐ <b>Why RESUME rather than an opt-out flag.</b> A <c>startPaused</c> parameter on
    /// <c>OrchestratorSubsystem</c> would let tests skip production's real boot sequence — ⛔ re-opening
    /// exactly this gap, where the suite and the product disagree about how the cluster starts. ⭐ Resuming
    /// is what an OPERATOR does, so the harness now exercises the true sequence: boot paused, then play.</para>
    ///
    /// <para>⚠ <b>The pause/step tests are unaffected</b> — they issue their own <c>PauseTime</c> and assert
    /// from there, so a running baseline is the correct starting state for them.</para>
    /// </summary>
    private void ResumeTimeAfterBoot()
    {
        var master = OrchestratorSvc.TestHook_ClusterMaster;
        if (master == null) return;   // a half-built harness: BootOrCleanUp is already unwinding

        master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.ResumeTime,
            PayloadJson   = string.Empty,
        }).GetAwaiter().GetResult();

        // ⚠ The op is asynchronous across the cluster: the master publishes a mode switch and each
        //   slave swaps its controller on its own tick. Returning before that lands would hand the test
        //   a harness that is *about to* run — the same ambiguity this method exists to remove.
        //   ⭐ So pump until the clock is genuinely advancing, and stop pumping the moment it is.
        for (int i = 0; i < ResumeSettleFrames; i++)
        {
            Orchestrator.RunFrames(1);
            Thread.Sleep(PumpSleepMs);

            var world = SimHost.World;
            if (world != null
                && world.HasSingleton<Fdp.Core.GlobalTime>()
                && world.GetSingleton<Fdp.Core.GlobalTime>().IsAdvancing)
                return;
        }

        // ⛔ Deliberately NOT an exception. A harness that throws here would convert every test in the
        //   suite into the same opaque failure. ⚠ The tests that care assert on movement or sim-time and
        //   will fail with their own, more informative message; `WhyDoesTheVehicleNotMoveProbe` dumps the
        //   clock state directly when this needs diagnosing again.
    }

    /// <summary>Frames to pump waiting for <c>ResumeTime</c> to reach the slaves (× <c>PumpSleepMs</c>).</summary>
    private const int ResumeSettleFrames = 120;
}

