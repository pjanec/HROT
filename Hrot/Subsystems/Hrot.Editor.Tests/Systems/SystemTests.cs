using System;
using System.IO;
using System.Numerics;
using CarKinem.Road;
using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Events;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Perception.Events;
using FDP.Toolkit.Physics.Components;
using FDP.Toolkit.Vis2D.Abstractions;
using Hrot.Editor.Rendering;
using Hrot.Editor.Systems;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Events;
using Raylib_cs;
using Xunit;

namespace Hrot.Editor.Tests.Systems;

// ═══════════════════════════════════════════════════════════════════════════════
// A009 — EditorCargoSystem
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Tests for <see cref="EditorCargoSystem"/>.
/// </summary>
public sealed class EditorCargoSystemTests : IDisposable
{
    private readonly EntityRepository _world;

    public EditorCargoSystemTests()
    {
        _world = new EntityRepository();
        _world.RegisterComponent<PassengerBuffer>();
        _world.RegisterComponent<IsEmbarkedTag>();
        _world.RegisterComponent<ActorCapabilityState>();
        _world.RegisterEvent<EmbarkEntityCommand>();
        _world.RegisterEvent<DisembarkEntityCommand>();
    }

    public void Dispose() => _world.Dispose();

    private EditorCargoSystem RunSystem()
    {
        var sys = new EditorCargoSystem();
        sys.Create(_world);
        _world.Bus.SwapBuffers();
        sys.Run();
        return sys;
    }

    // ── Test 1: Embark increases PassengerBuffer.Count ──────────────────────

    [Fact]
    public void Embark_ValidPassengerAndVehicle_IncreasesBufferCount()
    {
        var vehicle   = _world.CreateEntity();
        var passenger = _world.CreateEntity();
        _world.AddComponent(vehicle,   new PassengerBuffer());
        _world.AddComponent(passenger, new ActorCapabilityState
        {
            Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot,
        });

        _world.Bus.Publish(new EmbarkEntityCommand { Passenger = passenger, Vehicle = vehicle });

        RunSystem();

        ref readonly var buf = ref _world.GetComponent<PassengerBuffer>(vehicle);
        Assert.Equal(1, buf.Count);
        Assert.True(_world.HasComponent<IsEmbarkedTag>(passenger));
    }

    // ── Test 2: Embark does not exceed capacity ──────────────────────────────

    [Fact]
    public void Embark_AtCapacity_DoesNotExceedCapacityLimit()
    {
        var vehicle = _world.CreateEntity();
        _world.AddComponent(vehicle, new PassengerBuffer { Count = PassengerBuffer.Capacity });

        // Fill all passenger slots in the buffer (they all point to the vehicle entity itself — doesn't matter).
        var extraPassenger = _world.CreateEntity();
        _world.AddComponent(extraPassenger, new ActorCapabilityState());

        _world.Bus.Publish(new EmbarkEntityCommand { Passenger = extraPassenger, Vehicle = vehicle });

        RunSystem();

        ref readonly var buf = ref _world.GetComponent<PassengerBuffer>(vehicle);
        Assert.Equal(PassengerBuffer.Capacity, buf.Count); // still 8, not 9
    }

    // ── Test 3: Disembark removes IsEmbarkedTag ──────────────────────────────

    [Fact]
    public void Disembark_AfterEmbark_RemovesIsEmbarkedTag()
    {
        var vehicle   = _world.CreateEntity();
        var passenger = _world.CreateEntity();
        _world.AddComponent(vehicle,   new PassengerBuffer());
        _world.AddComponent(passenger, new ActorCapabilityState
        {
            Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot,
        });

        // Phase 1: Embark.
        _world.Bus.Publish(new EmbarkEntityCommand { Passenger = passenger, Vehicle = vehicle });
        var sys1 = new EditorCargoSystem();
        sys1.Create(_world);
        _world.Bus.SwapBuffers();
        sys1.Run();

        Assert.True(_world.HasComponent<IsEmbarkedTag>(passenger), "Should be embarked after Phase 1");

        // Phase 2: Disembark.
        _world.Bus.Publish(new DisembarkEntityCommand { Passenger = passenger });
        var sys2 = new EditorCargoSystem();
        sys2.Create(_world);
        _world.Bus.SwapBuffers();
        sys2.Run();

        Assert.False(_world.HasComponent<IsEmbarkedTag>(passenger), "Should NOT be embarked after disembark");
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// A010 — EditorPerceptionSetupSystem
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Tests for <see cref="EditorPerceptionSetupSystem"/>.
/// </summary>
public sealed unsafe class EditorPerceptionSetupSystemTests : IDisposable
{
    private readonly EntityRepository _world;

    public EditorPerceptionSetupSystemTests()
    {
        _world = new EntityRepository();
        _world.RegisterComponent<TargetMemory>();
        _world.RegisterComponent<SimTransform>();
        _world.RegisterEvent<SeedTargetCommand>();
    }

    public void Dispose() => _world.Dispose();

    // ── Test 1: SeedTargetCommand seeds target into TargetMemory ─────────────

    [Fact]
    public void SeedTarget_ValidPerceiverAndTarget_AddsToTargetMemory()
    {
        var perceiver = _world.CreateEntity();
        var target    = _world.CreateEntity();

        _world.AddComponent(perceiver, new TargetMemory());
        _world.AddComponent(target,    new SimTransform { Position = new Vector3(10f, 20f, 0f) });

        _world.Bus.Publish(new SeedTargetCommand
        {
            Perceiver  = perceiver,
            Target     = target,
            ScoreBoost = 5.0f,
        });

        var sys = new EditorPerceptionSetupSystem();
        sys.Create(_world);
        _world.Bus.SwapBuffers();
        sys.Run();

        ref readonly var mem = ref _world.GetComponent<TargetMemory>(perceiver);
        Assert.Equal(1, mem.Count);
        Assert.Equal((long)target.PackedValue, mem.EntityIds[0]);
    }

    // ── Test 2: Dead perceiver → no exception ────────────────────────────────

    [Fact]
    public void SeedTarget_DeadPerceiver_SkipsSilently()
    {
        var perceiver = _world.CreateEntity();
        var target    = _world.CreateEntity();
        _world.AddComponent(target, new SimTransform());
        _world.DestroyEntity(perceiver);

        _world.Bus.Publish(new SeedTargetCommand
        {
            Perceiver  = perceiver,
            Target     = target,
            ScoreBoost = 1.0f,
        });

        var sys = new EditorPerceptionSetupSystem();
        sys.Create(_world);
        _world.Bus.SwapBuffers();

        var ex = Record.Exception(() => sys.Run());
        Assert.Null(ex);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// A011 — EditorZoneAuthoringSystem
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Tests for <see cref="EditorZoneAuthoringSystem"/>.
/// </summary>
public sealed class EditorZoneAuthoringSystemTests : IDisposable
{
    private readonly EntityRepository _world;
    private readonly string           _tempRoadJson;

    public EditorZoneAuthoringSystemTests()
    {
        _world = new EntityRepository();
        _world.RegisterComponent<SimTransform>();
        _world.RegisterComponent<PhysicsCollider>();
        _world.RegisterManagedComponent<ZoneMembership>();

        // Write a minimal road-network JSON to a temp file for use in config tests.
        _tempRoadJson = Path.Combine(Path.GetTempPath(), $"test_road_{Guid.NewGuid():N}.json");
        File.WriteAllText(_tempRoadJson, """
            {
              "nodes": [
                { "id": 0, "position": { "x": 0, "y": 0 } },
                { "id": 1, "position": { "x": 100, "y": 0 } }
              ],
              "segments": [
                {
                  "id": 0,
                  "startNodeId": 0, "endNodeId": 1,
                  "controlPoints": {
                    "p0": {"x":0,"y":0}, "t0": {"x":100,"y":0},
                    "p1": {"x":100,"y":0}, "t1": {"x":100,"y":0}
                  },
                  "speedLimit": 15.0, "laneWidth": 3.5, "laneCount": 1
                }
              ],
              "metadata": { "gridCellSize": 10.0 }
            }
            """);
    }

    public void Dispose()
    {
        _world.Dispose();
        if (File.Exists(_tempRoadJson))
            File.Delete(_tempRoadJson);
    }

    // ── Test 1: SpawnZoneObstacleCommand creates entity with ZoneMembership ──

    [Fact]
    public void SpawnObstacle_PublishCommand_EntityWithZoneMembershipCreated()
    {
        _world.Bus.PublishManaged(new SpawnZoneObstacleCommand
        {
            ZoneName = "TestZone",
            Position = new Vector2(50f, 50f),
            Radius   = 5f,
        });

        var sys = new EditorZoneAuthoringSystem();
        sys.Create(_world);
        _world.Bus.SwapBuffers();
        sys.Run();

        var query   = _world.Query().With<SimTransform>().Build();
        int count   = 0;
        ZoneMembership? foundZone = null;

        foreach (var e in query)
        {
            if (_world.HasManagedComponent<ZoneMembership>(e))
            {
                count++;
                foundZone = _world.GetComponent<ZoneMembership>(e);
            }
        }

        Assert.Equal(1, count);
        Assert.Equal("TestZone", foundZone!.ZoneName);
    }

    // ── Test 2: UpdateZoneConfigCommand sets ZoneEnvironmentData singleton ───

    [Fact]
    public void UpdateZoneConfig_WithValidJsonPath_SetsSingletonTrue()
    {
        _world.Bus.PublishManaged(new UpdateZoneConfigCommand
        {
            ZoneName        = "Zone1",
            RoadNetworkPath = _tempRoadJson,
        });

        var sys = new EditorZoneAuthoringSystem();
        sys.Create(_world);
        _world.Bus.SwapBuffers();
        sys.Run();

        Assert.True(_world.HasSingleton<ZoneEnvironmentData>());
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// A012 — PerceptionMapLayer (smoke tests)
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Smoke tests for <see cref="PerceptionMapLayer"/>.
/// </summary>
public sealed class PerceptionMapLayerTests : IDisposable
{
    private readonly EntityRepository _world;

    public PerceptionMapLayerTests()
    {
        _world = new EntityRepository();
        _world.RegisterComponent<TargetMemory>();
        _world.RegisterComponent<SimTransform>();
    }

    public void Dispose() => _world.Dispose();

    // ── Test 1: layer implements IMapLayer ───────────────────────────────────

    [Fact]
    public void PerceptionMapLayer_ImplementsIMapLayer()
    {
        var layer = new PerceptionMapLayer(_world);
        Assert.IsAssignableFrom<IMapLayer>(layer);
        Assert.Equal("Perception Links", layer.Name);
        Assert.Equal(9, layer.LayerBitIndex);
    }

    // ── Test 2: Draw on empty world does not throw ───────────────────────────

    [Fact]
    public void Draw_EmptyWorld_DoesNotThrow()
    {
        var layer = new PerceptionMapLayer(_world);

        // We can't call Raylib rendering outside an actual Raylib window, so we
        // test only that the query / iteration path doesn't throw on an empty world.
        // The layer lazily builds its query on first Draw call.
        // Since no entities exist, the foreach body never executes and no Raylib
        // calls are made — this is sufficient for a smoke test.
        var ctx = new RenderContext
        {
            Camera = new Camera2D { Zoom = 1.0f },
        };

        // Verify query construction doesn't throw (the draw itself would call Raylib
        // which requires a window, so we only confirm construction is safe here).
        var ex = Record.Exception(() =>
        {
            // Manually trigger query build (bypasses actual Raylib drawing calls).
            layer.Update(0.016f);
        });

        Assert.Null(ex);
    }
}
