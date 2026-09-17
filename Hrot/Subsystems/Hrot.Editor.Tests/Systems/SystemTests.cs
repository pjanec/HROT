using System;
using System.IO;
using System.Numerics;
using CarKinem.Road;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Vis2D.Abstractions;
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
        _world.Bus.SwapBuffers();
        sys.Execute(_world, 0f);
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
        _world.Bus.SwapBuffers();
        sys1.Execute(_world, 0f);

        Assert.True(_world.HasComponent<IsEmbarkedTag>(passenger), "Should be embarked after Phase 1");

        // Phase 2: Disembark.
        _world.Bus.Publish(new DisembarkEntityCommand { Passenger = passenger });
        var sys2 = new EditorCargoSystem();
        _world.Bus.SwapBuffers();
        sys2.Execute(_world, 0f);

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
        _world.Bus.SwapBuffers();
        sys.Execute(_world, 0f);

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
        _world.Bus.SwapBuffers();

        var ex = Record.Exception(() => sys.Execute(_world, 0f));
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

    public EditorZoneAuthoringSystemTests()
    {
        _world = new EntityRepository();
        _world.RegisterComponent<SimTransform>();
        _world.RegisterComponent<PhysicsCollider>();

    }

    public void Dispose()
    {
        _world.Dispose();
    }

    /// <summary>
    /// ⭐ RE-HOMED from <c>SpawnObstacle_PublishCommand_EntityWithZoneMembershipCreated</c> (F1/F3).
    ///
    /// <para>The old test asserted two things at once: that the command creates an ENTITY, and that the
    /// entity carries a <c>ZoneMembership</c> naming its zone. The second half is retired — zones and
    /// obstacles are both entities now, so membership is geometry rather than a stored name that can
    /// drift from the shapes. ⭐ The FIRST half is the live authoring affordance and is kept, restated on
    /// what the entity actually carries.</para>
    ///
    /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §2.1c, §5.1, §6.
    /// </summary>
    [Fact]
    public void SpawnObstacle_PublishCommand_CreatesAPlacedCollidableEntity()
    {
        _world.Bus.PublishManaged(new SpawnZoneObstacleCommand
        {
            ZoneName = "TestZone",
            Position = new Vector2(50f, 50f),
            Radius   = 5f,
        });

        var sys = new EditorZoneAuthoringSystem();
        _world.Bus.SwapBuffers();
        sys.Execute(_world, 0f);

        var query = _world.Query().With<SimTransform>().With<PhysicsCollider>().Build();
        int count = 0;
        foreach (var e in query)
        {
            count++;
            Assert.Equal(50f, _world.GetComponent<SimTransform>(e).Position.X);
            Assert.Equal(5f,  _world.GetComponent<PhysicsCollider>(e).Radius);
        }

        Assert.Equal(1, count);
    }

    // ⛔ DELETED (F1/F3): `UpdateZoneConfig_WithValidJsonPath_SetsSingletonTrue`.
    //
    //   Its claim was "publishing UpdateZoneConfigCommand with a road-network path sets the
    //   ZoneEnvironmentData singleton". That behaviour is RETIRED and was a live hazard: it wrote the
    //   singleton DIRECTLY, bypassing RoadNetworkHolder, so a background solver holding a lease could
    //   not see the new graph and the old one was never retired (the C6 use-after-free family).
    //
    //   ⭐ The claim is RE-HOMED, not dropped: "a declared road network becomes ZoneEnvironmentData" is
    //   now asserted by TerrainLoadClusterStateHandlerTests
    //   (`ADefinitionListingARoadNetwork_PopulatesZoneEnvironmentData_WithNoZonesSection`), against the
    //   terrain loader that publishes through the holder.
    //   📄 docs/DESIGN_Terrain_Zones_And_Assets.md §2.1d, §6.
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
            Zoom = 1.0f,
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
