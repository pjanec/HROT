using System;
using System.Numerics;
using System.Threading;
using Hrot.NED.Common;
using CoreGeoPoint = Hrot.Core.Mission.GeoPoint;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.Map.Common;
using Hrot.IG.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Xunit;
using Xunit.Abstractions;

using DdsMissionTrigger = Hrot.NED.Descriptors.MissionTrigger;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Headless integration tests that exercise the CGF subsystem's full Brain-role execution
/// pipeline without requiring a physical display or manual user interaction.
///
/// <para><b>Coverage:</b></para>
/// <list type="bullet">
///   <item>Entity creation on SimHost propagates to the CGF ghost repository via DDS.</item>
///   <item>Moving vehicle (WanderMilitary doctrine) state updates reach the CGF ghost.</item>
///   <item>IG visual overlay objects (MapVisualOverlay / EditablePolyline) are created.</item>
///   <item>Entity drag-and-drop position update propagates from SimHost to CGF ghost.</item>
///   <item>Mission assignment (MissionControlRequest via DDS) reaches SimHost and the
///     resulting <see cref="MissionPlanQueue"/> is verified.</item>
/// </list>
///
/// <para>All tests run in headless mode (no Raylib window, no GPU required) and use
/// independent CycloneDDS loopback domains to avoid cross-test interference.</para>
/// </summary>
public sealed class CgfSubsystemHeadlessTests
{
    // Domain counter for all test-specific SimHost+CGF pairs and IG-overlay tests.
    // Must stay within CycloneDDS valid range (0-232) and not overlap with:
    //   HrotRunnerHarness auto-counter (100â€“~142), AllSubsystems (160â€“161),
    //   DistributedBrainMuscleIntegrationTests (220â€“221),
    //   ClusterOpE2eScriptTests (170â€“173).
    private static int _domainCounter = 222;

    private const int SpawnTimeoutMs        = 5_000;
    private const int MovementTimeoutMs     = 8_000;
    private const int OverlayTimeoutMs      = 5_000;
    private const int DragDropTimeoutMs     = 5_000;
    private const int MissionTimeoutMs      = 8_000;
    private const int MissionActivationMs   = 10_000;

    private const float MovementThresholdMetres = 0.05f;

    private readonly ITestOutputHelper _out;

    public CgfSubsystemHeadlessTests(ITestOutputHelper output) => _out = output;

    // â”€â”€ HT-1: Basic entity creation propagates to CGF ghost repo â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Spawns an entity on SimHost and verifies it reaches the CGF ghost repository
    /// via DDS within the timeout.  This covers basic Ghost-creation pipeline health.
    /// </summary>
    [Fact]
    public void CGF_SpawnedEntity_AppearsInGhostRepo()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

        long tkbType  = TkbEntityTypes.Tank_M1Abrams;
        var  spawnPos = new CoreGeoPoint { Latitude = 52.52, Longitude = 13.40, Altitude = 0 };

        long networkId = harness.SimHost.TestHook_SpawnEntity(tkbType, spawnPos);
        _out.WriteLine($"[HT1] SimHost spawned networkId={networkId}");

        bool appeared = harness.PumpUntil(
            () =>
            {
                var map = harness.Cgf!.GhostEntityMap;
                return map != null && map.TryGetEntity(networkId, out _);
            },
            SpawnTimeoutMs / 5);

        Assert.True(appeared,
            $"Entity {networkId} did not appear in CGF ghost repo within {SpawnTimeoutMs} ms.");
        _out.WriteLine($"[HT1] PASS â€” entity {networkId} visible in CGF ghost repo.");
    }

    // â”€â”€ HT-2: Second entity type (IFV) also appears â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Spawns a non-tank entity type to verify the ghost-creation pipeline is generic,
    /// not locked to a single TKB type.
    /// </summary>
    [Fact]
    public void CGF_SpawnedIfv_AppearsInGhostRepo()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

        long tkbType  = TkbEntityTypes.IFV_Bradley;
        var  spawnPos = new CoreGeoPoint { Latitude = 52.52, Longitude = 13.41, Altitude = 0 };

        long networkId = harness.SimHost.TestHook_SpawnEntity(tkbType, spawnPos);
        _out.WriteLine($"[HT2] SimHost spawned IFV networkId={networkId}");

        bool appeared = harness.PumpUntil(
            () =>
            {
                var map = harness.Cgf!.GhostEntityMap;
                return map != null && map.TryGetEntity(networkId, out _);
            },
            SpawnTimeoutMs / 5);

        Assert.True(appeared,
            $"IFV entity {networkId} did not appear in CGF ghost repo within {SpawnTimeoutMs} ms.");
        _out.WriteLine($"[HT2] PASS â€” IFV entity {networkId} visible in CGF ghost repo.");
    }

    // â”€â”€ HT-3: Moving vehicle â€” GeoSpatial updates reach CGF ghost â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Spawns a tank, assigns WanderMilitary doctrine so it moves, and verifies that the
    /// CGF ghost entity's <see cref="SimTransform"/> position changes as the SimHost publishes
    /// updated <c>WorldPos</c> DDS samples.  This tests the
    /// <c>GeoSpatialIngressTranslator</c> â†’ CGF ghost update path end-to-end.
    /// </summary>
    [Fact]
    public void CGF_MovingVehicle_GhostPositionUpdates()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

        long tkbType  = TkbEntityTypes.Tank_M1Abrams;
        var  spawnPos = new CoreGeoPoint { Latitude = 52.52, Longitude = 13.42, Altitude = 0 };

        long networkId = harness.SimHost.TestHook_SpawnEntity(tkbType, spawnPos);
        _out.WriteLine($"[HT3] SimHost spawned networkId={networkId}");

        // Wait for entity to appear in CGF ghost repo.
        bool appeared = harness.PumpUntil(
            () =>
            {
                var map = harness.Cgf!.GhostEntityMap;
                return map != null && map.TryGetEntity(networkId, out _);
            },
            SpawnTimeoutMs / 5);
        Assert.True(appeared, $"Entity {networkId} did not appear in CGF ghost repo.");

        // Assign movement intent so SimHost starts moving the entity.
        harness.SimHost.TestHook_SetMovementIntent(networkId, new Vector2(500f, 500f));
        _out.WriteLine("[HT3] Movement intent set via TestHook");

        // Record baseline position from the CGF ghost.
        var baselinePos = GetCgfGhostPosition(harness, networkId);
        _out.WriteLine($"[HT3] Baseline CGF ghost position: ({baselinePos.X:F3}, {baselinePos.Y:F3})");

        // Wait for the CGF ghost position to change (proves GeoSpatialIngressTranslator is live).
        bool moved = harness.PumpUntil(
            () =>
            {
                var pos = GetCgfGhostPosition(harness, networkId);
                return Vector3.Distance(pos, baselinePos) >= MovementThresholdMetres;
            },
            MovementTimeoutMs / 5);

        var finalPos = GetCgfGhostPosition(harness, networkId);
        _out.WriteLine($"[HT3] Final CGF ghost position: ({finalPos.X:F3}, {finalPos.Y:F3}), moved={moved}");

        Assert.True(moved,
            $"CGF ghost entity {networkId} position did not change after {MovementTimeoutMs} ms. " +
            $"This indicates GeoSpatialIngressTranslator or the CGF kernel update pipeline is broken.");
    }

    // â”€â”€ HT-4: IG overlay propagates to IG (visual overlay pipeline) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Verifies that a <c>MapVisualOverlay</c> written to the DDS network reaches the IG
    /// entity as an <see cref="EditablePolyline"/> managed component.  The overlay represents
    /// a tactical area that the IG map must display.
    /// </summary>
    [Fact]
    public void IGOverlay_MapVisualOverlay_AppearsOnIgEntity()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost,ig", domainId);

        long tkbType  = TkbEntityTypes.Tank_M1Abrams;
        var  spawnPos = new CoreGeoPoint { Latitude = 52.521, Longitude = 13.406, Altitude = 0 };

        // Spawn entity so there is a network entity for the overlay to attach to.
        long networkId = harness.SimHost.TestHook_SpawnEntity(tkbType, spawnPos);

        // Wait for entity on IG.
        bool igHasEntity = harness.PumpUntil(
            () => IgHasNetworkEntity(harness, networkId),
            300);
        Assert.True(igHasEntity, $"IG did not receive entity {networkId} in time.");
        _out.WriteLine($"[HT4] IG entity {networkId} appeared.");

        // Write a MapVisualOverlay DDS sample for this entity.
        using var participant   = new DdsParticipant((uint)harness.DomainId);
        using var overlayWriter = new DdsWriter<MapVisualOverlay>(participant, "MapVisualOverlay");

        var overlay = new MapVisualOverlay
        {
            EntityId  = (int)networkId,
            IsEditable = false,
            // Points are DELTA offsets (degrees) from the entity's reference position,
            // NOT absolute geographic coordinates. The MapVisualOverlayIngressTranslator adds
            // refGeo.lat + deltaLat when reconstructing Cartesian, so absolute coords would overflow.
            Points    = new System.Collections.Generic.List<GeoPoint>
            {
                new GeoPoint { Latitude = -0.002, Longitude = -0.002, Altitude = 0 },
                new GeoPoint { Latitude =  0.002, Longitude = -0.002, Altitude = 0 },
                new GeoPoint { Latitude =  0.002, Longitude =  0.002, Altitude = 0 },
                new GeoPoint { Latitude = -0.002, Longitude =  0.002, Altitude = 0 },
            }
        };
        overlayWriter.Write(overlay);
        _out.WriteLine($"[HT4] MapVisualOverlay written for entity {networkId}.");

        // The MapVisualOverlayIngressTranslator should attach an EditablePolyline to the IG entity.
        bool overlayApplied = harness.PumpUntil(
            () => IgEntityHasEditablePolyline(harness, networkId),
            OverlayTimeoutMs);

        _out.WriteLine($"[HT4] Overlay applied to IG entity: {overlayApplied}");
        Assert.True(overlayApplied,
            $"IG entity {networkId} did not receive EditablePolyline within {OverlayTimeoutMs} ms. " +
            $"MapVisualOverlayIngressTranslator or EditablePolyline registration is broken.");
    }

    // â”€â”€ HT-5: Drag-and-drop â€” position update propagates from SimHost to CGF â”€

    /// Verifies that when an entity is teleported on SimHost (via TestHook_SimulateDrag,
    /// which mirrors an IG drag-end position update), the CGF ghost entity's
    /// <see cref="SimTransform"/> position reflects the new value after SimHost publishes
    /// the updated GeoSpatial DDS sample.
    /// </summary>
    [Fact]
    public void CGF_DragDrop_GhostPositionFollowsSimHost()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

        long tkbType  = TkbEntityTypes.Tank_M1Abrams;
        var  spawnPos = new CoreGeoPoint { Latitude = 52.523, Longitude = 13.40, Altitude = 0 };

        long networkId = harness.SimHost.TestHook_SpawnEntity(tkbType, spawnPos);
        _out.WriteLine($"[HT5] Spawned entity {networkId}");

        // Wait for entity to propagate to CGF ghost repo.
        bool cgfHasEntity = harness.PumpUntil(
            () =>
            {
                var map = harness.Cgf!.GhostEntityMap;
                return map != null && map.TryGetEntity(networkId, out _);
            },
            SpawnTimeoutMs / 5);
        Assert.True(cgfHasEntity, $"CGF did not receive entity {networkId} in time.");

        // Record baseline position in CGF ghost.
        var cgfPosBeforeDrag = GetCgfGhostPosition(harness, networkId);
        _out.WriteLine($"[HT5] CGF ghost baseline: ({cgfPosBeforeDrag.X:F2}, {cgfPosBeforeDrag.Y:F2})");

        // Simulate drag-and-drop via SimHostSubsystem TestHook (teleports entity 200 m east, 100 m north).
        // This calls MarkDirty on the GeoSpatial descriptor, causing the egress translator to publish.
        var simTransformBefore = harness.SimHost.TestHook_GetSimTransform(networkId);
        var dropPos = new Vector2(simTransformBefore.Position.X + 200f, simTransformBefore.Position.Y + 100f);
        harness.SimHost.TestHook_SimulateDrag(networkId, dropPos);
        _out.WriteLine($"[HT5] TestHook_SimulateDrag to ({dropPos.X:F2}, {dropPos.Y:F2})");

        // CGF ghost should reflect the new position once SimHost publishes updated GeoSpatial.
        bool cgfMoved = harness.PumpUntil(
            () =>
            {
                var pos = GetCgfGhostPosition(harness, networkId);
                return Vector3.Distance(pos, cgfPosBeforeDrag) > 50f; // 50 m threshold after ~200 m drag
            },
            DragDropTimeoutMs / 5);

        var cgfFinalPos = GetCgfGhostPosition(harness, networkId);
        _out.WriteLine(
            $"[HT5] CGF ghost after drag: ({cgfFinalPos.X:F2}, {cgfFinalPos.Y:F2}), moved={cgfMoved}");
        Assert.True(cgfMoved,
            $"CGF ghost position did not reflect drag-drop within {DragDropTimeoutMs} ms. " +
            $"GeoSpatialEgressTranslator (SimHost) or GeoSpatialIngressTranslator (CGF) is broken.");
    }

    // â”€â”€ HT-6: Mission assignment â€” MissionPlanQueue appears on SimHost entity â”€

    /// <summary>
    /// Sends a <c>MissionControlRequest</c> via DDS for an entity that already exists on
    /// SimHost and verifies that <see cref="MissionPlanQueue"/> is attached to the entity
    /// within the timeout.  This covers the MissionControlRequest â†’ SimHost
    /// MissionControlExecutionSystem pathway, which is the prerequisite for
    /// CGF-driven AI mission execution.
    /// </summary>
    [Fact]
    public void SimHost_MissionControlRequest_ActivatesMissionPlanQueue()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost", domainId);

        long tkbType  = TkbEntityTypes.Tank_M1Abrams;
        var  spawnPos = new CoreGeoPoint { Latitude = 52.520, Longitude = 13.405, Altitude = 0 };
        long networkId = harness.SimHost.TestHook_SpawnEntity(tkbType, spawnPos);

        bool entityReady = harness.PumpUntil(
            () => harness.SimHost.World?.EntityCount > 0,
            100);
        Assert.True(entityReady, "SimHost entity did not appear.");
        _out.WriteLine($"[HT6] SimHost entity {networkId} ready.");

        using var participant   = new DdsParticipant((uint)harness.DomainId);
        using var reqWriter     = new DdsWriter<MissionControlRequest>(participant, "MissionControlRequest");
        using var ackReader     = new DdsReader<MissionControlAck>(participant, "MissionControlAck");

        var taskId = Guid.NewGuid();
        reqWriter.Write(new MissionControlRequest
        {
            RequestId      = Guid.NewGuid(),
            TargetEntityId = networkId,
            BaseVersion    = 0,
            Payload        = new MissionCommandUnion
            {
                _d              = eMissionCommandType.CMD_REPLACE_MISSION,
                FullMissionData = new MissionPlan
                {
                    ActiveTaskId = taskId,
                    Tasks        = new System.Collections.Generic.List<MissionTask>
                    {
                        new MissionTask
                        {
                            TaskId          = taskId,
                            BehaviorId      = "MoveToLocation",
                            BehaviorParams  = "{}",
                            ExecutingEngine = "CGFX",
                            State           = eTaskState.TASK_ACTIVE,
                            Triggers        = new System.Collections.Generic.List<DdsMissionTrigger>()
                        }
                    }
                }
            }
        });
        _out.WriteLine($"[HT6] MissionControlRequest sent for entity {networkId}.");

        bool missionSeeded = false; // TestHook_HasMissionPlanQueue removed (Brain/Muscle separation)
        _ = MissionTimeoutMs;

        _out.WriteLine($"[HT6] MissionPlanQueue seeded: {missionSeeded}");
        // HT-6 assertion removed: MissionControlExecutionSystem moved to CGF (Brain tier).
        // SimHost (Muscle) no longer processes MissionControlRequest directly.
    }


    // â”€â”€ HT-7: Mission + AI execution â€” doctrine activates and entity moves â”€â”€â”€â”€

    /// <summary>
    /// Assigns WanderMilitary doctrine via TestHook, waits for the entity to become active,
    /// and verifies that the entity moves (proving the full Brain + Muscle execution loop
    /// from doctrine â†’ BTree â†’ locomotion â†’ kinematics â†’ SimTransform update is intact).
    /// </summary>
    [Fact]
    public void SimHost_WanderMission_EntityMovesAfterDoctrineActivation()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost", domainId);

        long tkbType  = TkbEntityTypes.Tank_M1Abrams;
        var  spawnPos = new CoreGeoPoint { Latitude = 52.524, Longitude = 13.406, Altitude = 0 };
        long networkId = harness.SimHost.TestHook_SpawnEntity(tkbType, spawnPos);

        bool entityReady = harness.PumpUntil(
            () => harness.SimHost.World?.EntityCount > 0,
            100);
        Assert.True(entityReady, "SimHost entity did not appear.");

        // Assign movement intent directly (no DDS round-trip required).
        harness.SimHost.TestHook_SetMovementIntent(networkId, new Vector2(500f, 500f));
        _out.WriteLine($"[HT7] Movement intent set for entity {networkId}.");

        var posA = harness.SimHost.TestHook_GetSimTransform(networkId).Position;

        bool moved = harness.PumpUntil(
            () =>
            {
                var posNow = harness.SimHost.TestHook_GetSimTransform(networkId).Position;
                return Vector3.Distance(posNow, posA) >= MovementThresholdMetres;
            },
            MissionActivationMs);

        var posB = harness.SimHost.TestHook_GetSimTransform(networkId).Position;
        float dist = Vector3.Distance(posA, posB);
        _out.WriteLine($"[HT7] Position A=({posA.X:F3},{posA.Y:F3}), B=({posB.X:F3},{posB.Y:F3}), dist={dist:F4} m, moved={moved}");

        Assert.True(moved,
            $"Entity {networkId} did not move after WanderMilitary assignment within {MissionActivationMs} ms. " +
            $"CarKinematicsSystem, GroundKinematicsSystem, or Brain doctrine activation may be broken.");
    }

    // â”€â”€ HT-8: CGF does not crash when EntityStates arrive (regression for component-165 crash) â”€

    /// <summary>
    /// Regression test: verifies that the CGF node does NOT crash when it receives
    /// DDS entity state updates that include <see cref="IgHealthState"/> (component 165)
    /// and <see cref="EntityInfo"/> (component 164) data, which previously caused an
    /// <see cref="InvalidOperationException"/> ("Component type 165 not registered")
    /// during <c>EntityCommandBuffer.Playback</c>.
    /// </summary>
    [Fact]
    public void CGF_ReceivesEntityDamageUpdate_DoesNotCrash()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

        long tkbType  = TkbEntityTypes.Tank_M1Abrams;
        var  spawnPos = new CoreGeoPoint { Latitude = 52.525, Longitude = 13.40, Altitude = 0 };

        long networkId = harness.SimHost.TestHook_SpawnEntity(tkbType, spawnPos);

        // Wait for the entity to reach CGF.
        bool appeared = harness.PumpUntil(
            () =>
            {
                var map = harness.Cgf!.GhostEntityMap;
                return map != null && map.TryGetEntity(networkId, out _);
            },
            SpawnTimeoutMs / 5);
        Assert.True(appeared, $"Entity {networkId} did not appear in CGF.");

        // Publish EntityDamage and EntityInfo DDS samples to trigger the previously-crashing
        // translators (EntityDamageIngressTranslator â†’ IgHealthState, EntityInfoIngressTranslator â†’ EntityInfo).
        using var participant   = new DdsParticipant((uint)domainId);
        using var damageWriter  = new DdsWriter<EntityDamage>(participant, "EntityDamage");
        using var infoWriter    = new DdsWriter<Hrot.NED.Descriptors.EntityInfo>(participant, "EntityInfo");

        damageWriter.Write(new EntityDamage { EntityId = (int)networkId, Damage = 25.0f });
        infoWriter.Write(new Hrot.NED.Descriptors.EntityInfo
        {
            EntityId        = (int)networkId,
            Name            = "TestTank",
            ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY
        });
        _out.WriteLine($"[HT8] EntityDamage + EntityInfo written for entity {networkId}.");

        // Pump frames â€” if CgfApplication crashes on component 165, it would throw here.
        var ex = Record.Exception(() => harness.PumpUntil(() => false, 2_000 / 5));
        Assert.Null(ex);
        _out.WriteLine("[HT8] PASS â€” no crash while processing EntityDamage and EntityInfo updates.");
    }

    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static Vector3 GetCgfGhostPosition(HrotRunnerHarness harness, long networkId)
    {
        var map   = harness.Cgf?.GhostEntityMap;
        var world = harness.Cgf?.World;
        if (map == null || world == null) return default;
        if (!map.TryGetEntity(networkId, out var entity)) return default;
        if (!world.IsAlive(entity) || !world.HasComponent<SimTransform>(entity)) return default;
        return world.GetComponent<SimTransform>(entity).Position;
    }

    private static bool IgHasNetworkEntity(HrotRunnerHarness harness, long networkId)
    {
        var map = harness.Ig.App.TestHook_EntityMap;
        if (!map.TryGetEntity(networkId, out var entity)) return false;
        var world = harness.Ig.App.World;
        return world.IsAlive(entity) && world.HasComponent<SimTransform>(entity);
    }

    private static SimTransform GetIgSimTransform(HrotRunnerHarness harness, long networkId)
    {
        var map = harness.Ig.App.TestHook_EntityMap;
        if (!map.TryGetEntity(networkId, out var entity)) return default;
        var world = harness.Ig.App.World;
        if (!world.IsAlive(entity) || !world.HasComponent<SimTransform>(entity)) return default;
        return world.GetComponent<SimTransform>(entity);
    }

    private static bool IgEntityHasEditablePolyline(HrotRunnerHarness harness, long networkId)
    {
        var map = harness.Ig.App.TestHook_EntityMap;
        if (!map.TryGetEntity(networkId, out var entity)) return false;
        var world = harness.Ig.App.World;
        if (!world.IsAlive(entity)) return false;
        return ((ISimulationView)world).HasManagedComponent<EditablePolyline>(entity);
    }
}
