using System;
using System.Numerics;
using System.Threading;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.NED.Common;
using CoreGeoPoint = Hrot.Core.Mission.GeoPoint;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using CycloneDDS.Runtime;
using FDP.Toolkit.Replication.Components;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Integration test verifying that "Spawn Moving Vehicle" (MiniExCon gateway flow)
/// produces a moving entity when all five subsystems are running together
/// (Orchestrator + SimHost + IG + ExCon + CGF), replicating "clusterrunner -m all".
///
/// <para>
/// <b>Regression guard:</b> A bug was introduced during the HROT Editor design-batch
/// implementation whereby the entity is spawned but does NOT start moving when CGF
/// is present on the same DDS domain.  This test must pass for the fix to be
/// considered complete.
/// </para>
/// </summary>
public sealed class AllSubsystemsSpawnMovingVehicleTests
{
    // Domain range: 150–159 — between HrotRunnerHarness auto-range (100–145) and
    // AllSubsystemsClusterTransitionTests (160+). CycloneDDS max domain is 232.
    private const int DomainBase = 150;
    private static int _domainCounter = DomainBase - 1;

    private const int GatewayTimeoutFrames  = 1000;
    private const int SpawnTimeoutFrames    = 150;
    private const int MovementTimeoutFrames = 300;
    private const float MovementThresholdMetres = 0.05f;

    private readonly ITestOutputHelper _out;

    public AllSubsystemsSpawnMovingVehicleTests(ITestOutputHelper output)
        => _out = output;

    /// <summary>
    /// Verifies the full "Spawn Moving Vehicle" button flow with all five subsystems
    /// active (Orchestrator, SimHost, IG, ExCon, CGF) — the same topology as
    /// "clusterrunner -m all", running fully headless in a single process.
    /// <list type="number">
    ///   <item>IG sends <c>CreateEntityRequest</c> and receives a valid entity ID.</item>
    ///   <item>IG sends <c>MissionControlRequest</c> (WanderMilitary) and receives <c>Ack</c>.</item>
    ///   <item>Entity appears on IG with <c>Active</c> lifecycle and a <c>SimTransform</c>.</item>
    ///   <item>IG <see cref="SimTransform"/> position changes within
    ///         <see cref="MovementTimeoutFrames"/> frames, proving the entity is moving
    ///         even with CGF present.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void AllSubsystems_SpawnMovingVehicleViaGateway_EntityMovesOnIg()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        _out.WriteLine($"[A0] Domain: {domainId}");

        // ── Boot all five subsystems in one fully-headless orchestrator ───────
        // all == orchestrator,simhost,ig,excon,cgf
        using var harness = new HrotRunnerHarness("simhost,ig,excon,cgf", domainId);

        // Extra settle: wait for the first 1 Hz NodeHeartbeat from SimHost to reach CGF.
        // BrainMuscleOwnershipStrategy delegates WorldPos to SimHost only after SimHost is
        // registered in CGF's cluster cache (populated from DDS NodeHeartbeat).  If the
        // entity is spawned before that, CGF retains WorldPos authority and CarKinematicsSystem
        // never moves it.  We pump with 5 ms sleeps so ClusterSlave.Tick() fires on all
        // subsystems and SlaveTranslator can write the heartbeat to DDS.
        // Total pre-spawn wall time: harness warmup 300 ms + pump below ~1100 ms = ~1400 ms.
        harness.PumpUntil(() => false, 220); // 220 * 5 ms = ~1100 ms

        var igApp = harness.Ig.App;

        using var observerParticipant = new DdsParticipant((uint)domainId);
        using var ackReader = new DdsReader<CreateUpdateDeleteEntityAck>(observerParticipant, "CreateUpdateDeleteEntityAck");
        using var navIntentReader = new DdsReader<Hrot.NED.Descriptors.NavigationIntent>(observerParticipant, "NavigationIntent");

        long tkbType = TkbEntityTypes.Tank_M1Abrams;

        // ── 1. Fire the gateway spawn+wander flow ─────────────────────────────
        var spawnTask = igApp.TestHook_SubmitMiniExConSpawnWithWanderMission(
            tkbType, ForceId.Friend, 100f, 200f);
        _out.WriteLine("[A1] TestHook_SubmitMiniExConSpawnWithWanderMission fired.");

        // ── 2. Capture the CreateEntityAck ───────────────────────────────────
        CreateUpdateDeleteEntityAck spawnAck = default;
        bool ackObserved = harness.PumpUntil(
            () => TryTakeAnyCreateAck(ackReader, out spawnAck),
            GatewayTimeoutFrames);
        Assert.True(ackObserved,
            "CreateUpdateDeleteEntityAck did not arrive in time. " +
            "CreateEntityRequestSystem on CGF may not have processed the request.");
        Assert.True(spawnAck.EntityId > 0,
            "CreateUpdateDeleteEntityAck returned a zero/negative entity ID.");

        long networkId = spawnAck.EntityId;
        _out.WriteLine($"[A2] ACK received. networkId={networkId}.");

        // ── 3. Wait for the full async chain (MissionControl included) ────────
        bool taskDone = harness.PumpUntil(() => spawnTask.IsCompleted, 200);
        if (!taskDone && !spawnTask.IsCompleted)
            spawnTask.Wait(2000);
        if (spawnTask.IsFaulted)
            throw spawnTask.Exception!.GetBaseException();
        _out.WriteLine($"[A3] Gateway task done: {spawnTask.Result}.");

        // ── 4. Wait for IG entity to appear with Active lifecycle ─────────────
        bool igActive = harness.PumpUntil(
            () => IgEntityIsActive(harness, networkId),
            SpawnTimeoutFrames);
        Assert.True(igActive,
            $"IG entity (networkId={networkId}) did not reach Active lifecycle within {SpawnTimeoutFrames} frames.");
        _out.WriteLine("[A4] IG entity is Active.");

        // ── Diagnostic: pump until CGF entity has NavigationIntent set, then snapshot ─
        // If timeout occurs, the BTree/dispatch pipeline is broken.
        {
            var cgfMap = harness.Cgf?.GhostEntityMap;
            var cgfWorld = harness.Cgf?.World;
            bool cgfNavReady = harness.PumpUntil(() =>
            {
                if (cgfMap == null || cgfWorld == null) return false;
                if (!cgfMap.TryGetEntity(networkId, out var e)) return false;
                if (!cgfWorld.IsAlive(e)) return false;
                if (!cgfWorld.HasComponent<FDP.Toolkit.Navigation.NavigationIntent>(e)) return false;
                return cgfWorld.GetComponent<FDP.Toolkit.Navigation.NavigationIntent>(e).Mode
                       != FDP.Toolkit.Navigation.NavigationMode.None;
            }, 100);
            _out.WriteLine($"[DIAG] CGF NavigationIntent.Mode became non-None: {cgfNavReady}");

            // Also wait for SimHost to receive NavigationIntent from DDS
            var shMap = harness.SimHost.App.TestHook_EntityMap;
            var shWorld = harness.SimHost.App.World;
            bool shNavReady = harness.PumpUntil(() =>
            {
                if (shWorld == null) return false;
                if (!shMap.TryGetEntity(networkId, out var e)) return false;
                if (!shWorld.IsAlive(e)) return false;
                if (!shWorld.HasComponent<FDP.Toolkit.Navigation.NavigationIntent>(e)) return false;
                return shWorld.GetComponent<FDP.Toolkit.Navigation.NavigationIntent>(e).Mode
                       != FDP.Toolkit.Navigation.NavigationMode.None;
            }, 200);
            _out.WriteLine($"[DIAG] SimHost NavigationIntent.Mode became non-None: {shNavReady}");

            // DDS-level diagnostic: check if the NavigationIntent topic has any published data
            {
                bool anyDds = false;
                using var loan = navIntentReader.Take();
                foreach (var sample in loan)
                {
                    if (!sample.IsValid) continue;
                    anyDds = true;
                    _out.WriteLine($"[DIAG-DDS] NavigationIntent on DDS: EntityId={sample.Data.EntityId} Mode={sample.Data.Mode}");
                }
                if (!anyDds)
                    _out.WriteLine("[DIAG-DDS] NavigationIntent topic: NO DDS samples received");
            }
        }
        DiagnoseCgfEntity(harness, networkId, _out);
        DiagnoseSimHostEntity(harness, networkId, _out);

        // ── 5. Record baseline IG position ───────────────────────────────────
        var posA = GetIgSimTransform(harness, networkId).Position;
        _out.WriteLine($"[A5] Baseline IG position: ({posA.X:F3}, {posA.Y:F3}).");

        // ── 6. Verify position changes ────────────────────────────────────────
        bool moved = harness.PumpUntil(() =>
        {
            var posNow = GetIgSimTransform(harness, networkId).Position;
            return Vector3.Distance(posNow, posA) >= MovementThresholdMetres;
        }, MovementTimeoutFrames);

        var posB = GetIgSimTransform(harness, networkId).Position;
        float travelledMetres = Vector3.Distance(posA, posB);
        _out.WriteLine(
            $"[A6] Final IG position: ({posB.X:F3}, {posB.Y:F3}), " +
            $"travelled={travelledMetres:F4} m, moved={moved}.");

        Assert.True(moved,
            $"Entity (networkId={networkId}) spawned via NedCommandGateway never moved on IG " +
            $"when all five subsystems (including CGF) are running. " +
            $"Baseline=({posA.X:F3},{posA.Y:F3}), final=({posB.X:F3},{posB.Y:F3}), " +
            $"travelled={travelledMetres:F4} m (threshold={MovementThresholdMetres} m). " +
            $"This is the 'clusterrunner -m all' spawn-vehicle regression.");
    }

    // ── Assertion helpers ─────────────────────────────────────────────────────

    private static bool TryTakeAnyCreateAck(
        DdsReader<CreateUpdateDeleteEntityAck> reader,
        out CreateUpdateDeleteEntityAck ack)
    {
        using var loan = reader.Take(1);
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            ack = sample.Data;
            return true;
        }
        ack = default;
        return false;
    }

    private static bool IgEntityIsActive(HrotRunnerHarness harness, long networkId)
    {
        var entityMap = harness.Ig.App.TestHook_EntityMap;
        if (!entityMap.TryGetEntity(networkId, out var entity)) return false;
        var world = harness.Ig.App.World;
        if (!world.IsAlive(entity)) return false;
        return world.GetLifecycleState(entity) == EntityLifecycle.Active;
    }

    private static SimTransform GetIgSimTransform(HrotRunnerHarness harness, long networkId)
    {
        var entityMap = harness.Ig.App.TestHook_EntityMap;
        if (!entityMap.TryGetEntity(networkId, out var entity)) return default;
        var world = harness.Ig.App.World;
        if (!world.IsAlive(entity) || !world.HasComponent<SimTransform>(entity)) return default;
        return world.GetComponent<SimTransform>(entity);
    }

    private static void DiagnoseCgfEntity(HrotRunnerHarness harness, long networkId, ITestOutputHelper output)
    {
        var cgfMap   = harness.Cgf?.GhostEntityMap;
        var cgfWorld = harness.Cgf?.World;
        if (cgfMap == null || cgfWorld == null) { output.WriteLine("[DIAG-CGF] CGF not available"); return; }
        if (!cgfMap.TryGetEntity(networkId, out var entity)) { output.WriteLine($"[DIAG-CGF] entity {networkId} not in map"); return; }
        if (!cgfWorld.IsAlive(entity)) { output.WriteLine("[DIAG-CGF] entity not alive"); return; }

        string docHash = cgfWorld.HasComponent<FDP.Toolkit.Behavior.Components.DoctrineState>(entity)
            ? cgfWorld.GetComponent<FDP.Toolkit.Behavior.Components.DoctrineState>(entity).ActiveDoctrineHash.ToString()
            : "no-component";
        string navMode = cgfWorld.HasComponent<FDP.Toolkit.Navigation.NavigationIntent>(entity)
            ? cgfWorld.GetComponent<FDP.Toolkit.Navigation.NavigationIntent>(entity).Mode.ToString()
            : "no-component";
        string planPhase = cgfWorld.HasComponent<FDP.Toolkit.Behavior.Components.MissionPlanQueue>(entity)
            ? $"phase={cgfWorld.GetComponent<FDP.Toolkit.Behavior.Components.MissionPlanQueue>(entity).CurrentPhase} count={cgfWorld.GetComponent<FDP.Toolkit.Behavior.Components.MissionPlanQueue>(entity).PhaseCount}"
            : "no-component";
        string loco = cgfWorld.HasComponent<FDP.Toolkit.Behavior.Components.LocomotionChannel>(entity)
            ? $"action={cgfWorld.GetComponent<FDP.Toolkit.Behavior.Components.LocomotionChannel>(entity).ActiveAction} status={cgfWorld.GetComponent<FDP.Toolkit.Behavior.Components.LocomotionChannel>(entity).Status} dispId={cgfWorld.GetComponent<FDP.Toolkit.Behavior.Components.LocomotionChannel>(entity).DispatchedInstanceId} actId={cgfWorld.GetComponent<FDP.Toolkit.Behavior.Components.LocomotionChannel>(entity).ActionInstanceId}"
            : "no-component";
        string caps = cgfWorld.HasComponent<FDP.Toolkit.Behavior.Components.ActorCapabilityState>(entity)
            ? cgfWorld.GetComponent<FDP.Toolkit.Behavior.Components.ActorCapabilityState>(entity).Capabilities.ToString()
            : "no-component";
        output.WriteLine($"[DIAG-CGF] DoctrineHash={docHash} NavMode={navMode} MissionPlanQueue={planPhase}");
        output.WriteLine($"[DIAG-CGF] LocoChannel={loco} ActorCaps={caps}");
    }

    private static void DiagnoseSimHostEntity(HrotRunnerHarness harness, long networkId, ITestOutputHelper output)
    {
        var shMap   = harness.SimHost.App.TestHook_EntityMap;
        var shWorld = harness.SimHost.App.World;
        if (shWorld == null) { output.WriteLine("[DIAG-SH] SimHost world not available"); return; }
        if (!shMap.TryGetEntity(networkId, out var entity)) { output.WriteLine($"[DIAG-SH] entity {networkId} not in map"); return; }
        if (!shWorld.IsAlive(entity)) { output.WriteLine("[DIAG-SH] entity not alive"); return; }

        string navMode = shWorld.HasComponent<FDP.Toolkit.Navigation.NavigationIntent>(entity)
            ? shWorld.GetComponent<FDP.Toolkit.Navigation.NavigationIntent>(entity).Mode.ToString()
            : "no-component";
        string navStateMode = shWorld.HasComponent<CarKinem.Core.NavState>(entity)
            ? shWorld.GetComponent<CarKinem.Core.NavState>(entity).Mode.ToString()
            : "no-component";
        string lifecyle = shWorld.GetLifecycleState(entity).ToString();
        output.WriteLine($"[DIAG-SH] NavIntent.Mode={navMode} NavState.Mode={navStateMode} Lifecycle={lifecyle}");
    }
}
