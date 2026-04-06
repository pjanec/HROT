using System;
using System.IO;
using System.Numerics;
using CarKinem.Road;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Events;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Perception.Events;
using FDP.Toolkit.Physics.Components;
using FDP.Toolkit.Replication.Components;
using Hrot.Editor.Adapters;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Events;
using Hrot.Map.Definitions.Tkb;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

// ═══════════════════════════════════════════════════════════════════════════════
// T001 — Embarkation & Cargo Integration Tests
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// EDIT1-T001/T002/T003/T004 — Headless integration tests for editor authoring systems.
/// Uses <see cref="EditorHarness"/> (no DDS, no Raylib) to exercise
/// <c>EditorCargoSystem</c>, <c>EditorPerceptionSetupSystem</c>, and
/// <c>EditorZoneAuthoringSystem</c>.
/// </summary>
[Collection("EditorOfflineTests")]
public sealed class EditorAuthoringIntegrationTests : IDisposable
{
    private readonly string _tempFile = Path.GetTempFileName() + ".json";

    public void Dispose()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    // ── T001 ── Embarkation ────────────────────────────────────────────────────

    [Fact]
    public void Embarkation_ValidRequest_UpdatesPassengerBufferAndStripsCapabilities()
    {
        using var harness = new EditorHarness();

        var world = harness.Repo;

        // Setup: APC with PassengerBuffer, Soldier with full capabilities.
        var apc     = world.CreateEntity();
        var soldier = world.CreateEntity();

        world.AddComponent(apc,     new PassengerBuffer());
        world.AddComponent(soldier, new ActorCapabilityState
        {
            Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot,
        });

        // Act: publish embark command and advance one frame.
        world.Bus.Publish(new EmbarkEntityCommand { Passenger = soldier, Vehicle = apc });
        harness.PumpFrames(1);

        // Assert: passenger in buffer.
        ref readonly var buf = ref world.GetComponent<PassengerBuffer>(apc);
        Assert.Equal(1, buf.Count);
        Assert.Equal(soldier, buf.Passengers[0]);

        // Assert: IsEmbarkedTag present on soldier.
        Assert.True(world.HasComponent<IsEmbarkedTag>(soldier));

        // Assert: movement + combat capability stripped.
        ref readonly var caps = ref world.GetComponent<ActorCapabilityState>(soldier);
        Assert.False(caps.Capabilities.HasFlag(ActorCapabilities.CanMove));
        Assert.False(caps.Capabilities.HasFlag(ActorCapabilities.CanShoot));
    }

    [Fact]
    public void Embarkation_CapacityLimitEnforced_NoMutationOnOverflow()
    {
        using var harness = new EditorHarness();
        var world = harness.Repo;

        var apc = world.CreateEntity();
        world.AddComponent(apc, new PassengerBuffer { Count = PassengerBuffer.Capacity });

        var extraSoldier = world.CreateEntity();
        world.AddComponent(extraSoldier, new ActorCapabilityState());

        world.Bus.Publish(new EmbarkEntityCommand { Passenger = extraSoldier, Vehicle = apc });
        harness.PumpFrames(1);

        ref readonly var buf = ref world.GetComponent<PassengerBuffer>(apc);
        Assert.Equal(PassengerBuffer.Capacity, buf.Count);
        Assert.False(world.HasComponent<IsEmbarkedTag>(extraSoldier));
    }

    [Fact]
    public void Disembark_RestoresCapabilities()
    {
        using var harness = new EditorHarness();
        var world = harness.Repo;

        var apc     = world.CreateEntity();
        var soldier = world.CreateEntity();
        world.AddComponent(apc,     new PassengerBuffer());
        world.AddComponent(soldier, new ActorCapabilityState
        {
            Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot,
        });

        // Phase 1: Embark.
        world.Bus.Publish(new EmbarkEntityCommand { Passenger = soldier, Vehicle = apc });
        harness.PumpFrames(1);
        Assert.True(world.HasComponent<IsEmbarkedTag>(soldier));

        // Phase 2: Disembark.
        world.Bus.Publish(new DisembarkEntityCommand { Passenger = soldier });
        harness.PumpFrames(1);

        Assert.False(world.HasComponent<IsEmbarkedTag>(soldier));
        ref readonly var caps = ref world.GetComponent<ActorCapabilityState>(soldier);
        Assert.True(caps.Capabilities.HasFlag(ActorCapabilities.CanMove));
        Assert.True(caps.Capabilities.HasFlag(ActorCapabilities.CanShoot));
    }

    // ── T002 ── Target Memory Seeding ─────────────────────────────────────────

    [Fact]
    public unsafe void TargetSeeding_SinglePerceiver_SeedsMemoryBuffer()
    {
        using var harness = new EditorHarness();
        var world = harness.Repo;

        var insurgent = world.CreateEntity();
        var apc       = world.CreateEntity();

        world.AddComponent(insurgent, new TargetMemory());
        world.AddComponent(apc,       new SimTransform { Position = new Vector3(10f, 20f, 0f) });

        world.Bus.Publish(new SeedTargetCommand
        {
            Perceiver  = insurgent,
            Target     = apc,
            ScoreBoost = 100f,
        });
        harness.PumpFrames(1);

        ref readonly var mem = ref world.GetComponent<TargetMemory>(insurgent);
        Assert.Equal(1, mem.Count);
        Assert.Equal((long)apc.PackedValue, mem.EntityIds[0]);
        Assert.True(mem.ThreatScores[0] >= 100f);
    }

    [Fact]
    public unsafe void TargetSeeding_NToOne_AllPerceiversReceiveTarget()
    {
        using var harness = new EditorHarness();
        var world = harness.Repo;

        var apc = world.CreateEntity();
        world.AddComponent(apc, new SimTransform { Position = new Vector3(5f, 5f, 0f) });

        var i1 = world.CreateEntity();
        var i2 = world.CreateEntity();
        var i3 = world.CreateEntity();
        world.AddComponent(i1, new TargetMemory());
        world.AddComponent(i2, new TargetMemory());
        world.AddComponent(i3, new TargetMemory());

        world.Bus.Publish(new SeedTargetCommand { Perceiver = i1, Target = apc, ScoreBoost = 50f });
        world.Bus.Publish(new SeedTargetCommand { Perceiver = i2, Target = apc, ScoreBoost = 50f });
        world.Bus.Publish(new SeedTargetCommand { Perceiver = i3, Target = apc, ScoreBoost = 50f });
        harness.PumpFrames(1);

        Assert.Equal(1, world.GetComponent<TargetMemory>(i1).Count);
        Assert.Equal(1, world.GetComponent<TargetMemory>(i2).Count);
        Assert.Equal(1, world.GetComponent<TargetMemory>(i3).Count);
    }

    [Fact]
    public unsafe void TargetSeeding_OneToN_PerceiverReceivesAllTargets()
    {
        using var harness = new EditorHarness();
        var world = harness.Repo;

        var insurgent = world.CreateEntity();
        world.AddComponent(insurgent, new TargetMemory());

        var apc1 = world.CreateEntity();
        var apc2 = world.CreateEntity();
        var apc3 = world.CreateEntity();
        world.AddComponent(apc1, new SimTransform { Position = new Vector3(1f, 0f, 0f) });
        world.AddComponent(apc2, new SimTransform { Position = new Vector3(2f, 0f, 0f) });
        world.AddComponent(apc3, new SimTransform { Position = new Vector3(3f, 0f, 0f) });

        world.Bus.Publish(new SeedTargetCommand { Perceiver = insurgent, Target = apc1, ScoreBoost = 10f });
        world.Bus.Publish(new SeedTargetCommand { Perceiver = insurgent, Target = apc2, ScoreBoost = 10f });
        world.Bus.Publish(new SeedTargetCommand { Perceiver = insurgent, Target = apc3, ScoreBoost = 10f });
        harness.PumpFrames(1);

        Assert.Equal(3, world.GetComponent<TargetMemory>(insurgent).Count);
    }

    // ── T003 ── Zone Authoring ────────────────────────────────────────────────

    [Fact]
    public void ZoneAuthoring_ObstaclePlacement_SpawnsPhysicsCollider()
    {
        using var harness = new EditorHarness();
        var world = harness.Repo;

        harness.Bus.PublishManaged(new SpawnZoneObstacleCommand
        {
            ZoneName = "test",
            Position = new Vector2(50f, 25f),
            Radius   = 10f,
        });
        harness.PumpFrames(1);

        var entities = world.Query().With<PhysicsCollider>().Build();
        Assert.Equal(1, entities.Count());

        Entity entity = default;
        foreach (var e in entities) { entity = e; break; }
        ref readonly var col = ref world.GetComponent<PhysicsCollider>(entity);
        Assert.Equal(10f, col.Radius, precision: 3);

        ref readonly var xfm = ref world.GetComponent<SimTransform>(entity);
        Assert.Equal(50f, xfm.Position.X, precision: 3);
        Assert.Equal(25f, xfm.Position.Y, precision: 3);
    }

    [Fact]
    public void ZoneAuthoring_RoadNetworkUpdate_InjectsZoneEnvironmentDataSingleton()
    {
        using var harness = new EditorHarness();

        // Create a minimal valid road-network JSON in a temp file.
        string roadJson = @"{
  ""nodes"": [
    { ""id"": 0, ""position"": { ""x"": 0, ""y"": 0 } },
    { ""id"": 1, ""position"": { ""x"": 100, ""y"": 0 } }
  ],
  ""segments"": [
    {
      ""id"": 0, ""startNodeId"": 0, ""endNodeId"": 1,
      ""controlPoints"": {
        ""p0"": { ""x"": 0, ""y"": 0 }, ""t0"": { ""x"": 1, ""y"": 0 },
        ""p1"": { ""x"": 100, ""y"": 0 }, ""t1"": { ""x"": -1, ""y"": 0 }
      },
      ""speedLimit"": 25, ""laneWidth"": 3.5, ""laneCount"": 2
    }
  ]
}";
        File.WriteAllText(_tempFile, roadJson);

        harness.Bus.PublishManaged(new UpdateZoneConfigCommand
        {
            ZoneName        = "test",
            RoadNetworkPath = _tempFile,
        });
        harness.PumpFrames(1);

        Assert.True(harness.Repo.HasSingleton<ZoneEnvironmentData>());
    }

    [Fact]
    public void ZoneAuthoring_FullSave_BundlesZoneDtoInEnvelope()
    {
        using var harness = new EditorHarness();

        // Create minimal road JSON.
        string roadJson = @"{""nodes"":[{""id"":0,""position"":{""x"":0,""y"":0}}],""segments"":[]}";
        File.WriteAllText(_tempFile, roadJson);

        // Spawn obstacle.
        harness.Bus.PublishManaged(new SpawnZoneObstacleCommand
        {
            ZoneName = "test",
            Position = new Vector2(50f, 25f),
            Radius   = 10f,
        });

        // Road network update.
        harness.Bus.PublishManaged(new UpdateZoneConfigCommand
        {
            ZoneName        = "test",
            RoadNetworkPath = _tempFile,
        });
        harness.PumpFrames(2);

        // Save to a second temp file.
        string saveFile = Path.GetTempFileName() + ".json";
        try
        {
            harness.Editor.SaveScenario(saveFile);

            string json     = File.ReadAllText(saveFile);
            var    opts     = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };
            var envelope = System.Text.Json.JsonSerializer.Deserialize<Hrot.Map.Common.Scenario.HrotScenarioEnvelopeDto>(json, opts);

            Assert.NotNull(envelope?.Zones);
            Assert.True(envelope!.Zones!.ContainsKey("test"), "Zone 'test' should be in envelope");

            var zone = envelope.Zones["test"];
            Assert.Equal(_tempFile, zone.RoadNetworkPath);
            Assert.NotNull(zone.Obstacles);
            Assert.Equal(1, zone.Obstacles!.Count);
            Assert.Equal(50f, zone.Obstacles[0].X, precision: 2);
            Assert.Equal(10f, zone.Obstacles[0].Radius, precision: 2);
        }
        finally
        {
            if (File.Exists(saveFile)) File.Delete(saveFile);
        }
    }

    // ── T004 ── Doctrine Catalog ──────────────────────────────────────────────

    [Fact]
    public void DoctrineCatalog_Insurgent_ReturnsInsurgentDoctrines()
    {
        var doctrines = DoctrineCatalog.GetValidDoctrines(TkbEntityTypes.Insurgent);

        Assert.Contains("Ambush",        doctrines);
        Assert.DoesNotContain("WanderCivil", doctrines);
    }

    [Fact]
    public void DoctrineCatalog_Civilian_ReturnsCivilianDoctrines()
    {
        var doctrines = DoctrineCatalog.GetValidDoctrines(TkbEntityTypes.CivilianPedestrian);

        Assert.Contains("WanderCivil", doctrines);
        Assert.DoesNotContain("Ambush",    doctrines);
    }

    [Fact]
    public void EditorMissionService_FiltersOutUnregisteredDoctrines()
    {
        using var harness = new EditorHarness();
        var world = harness.Repo;

        // Spawn an Insurgent entity with TkbIdentity.
        var insurgent = world.CreateEntity();
        world.AddComponent(insurgent, new TkbIdentity { TkbType = TkbEntityTypes.Insurgent });

        // Build a DoctrineRegistry that only registers "Ambush" (not "MoveToLocation").
        var registry = new FDP.Toolkit.Behavior.DoctrineRegistry();
        registry.Register(1, "Ambush", new FDP.Toolkit.Behavior.DoctrineDefinition { Name = "Ambush" });

        var service = new EditorMissionService(world.Bus, world, registry);

        // GetAvailableBehaviors uses entity index as the ID.
        var behaviors = service.GetAvailableBehaviors((long)insurgent.Index);

        Assert.Contains("Ambush",           behaviors);
        Assert.DoesNotContain("MoveToLocation", behaviors);
    }
}
