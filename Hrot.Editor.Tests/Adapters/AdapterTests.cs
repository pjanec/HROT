using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Events;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Abstractions;
using Hrot.Common.Events;
using Hrot.Common.Orchestration.Handlers;
using Hrot.Editor.Adapters;
using Hrot.Editor.Tools;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.Map.Common.Config;
using Hrot.Map.Common.Events;
using Hrot.Map.Definitions.Tkb;
using Hrot.NED.Common;
using Hrot.ScenarioEditor;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;
using Moq;
using Raylib_cs;
using Xunit;

namespace Hrot.Editor.Tests.Adapters
{
    // ── Shared test infrastructure ────────────────────────────────────────────

    /// <summary>
    /// Minimal <see cref="IInputProvider"/> stub for test-only canvas construction.
    /// Returns all defaults (no input pressed, no mouse movement).
    /// </summary>
    internal sealed class TestInputProvider : IInputProvider
    {
        public Vector2 MousePosition  { get; set; } = Vector2.Zero;
        public Vector2 MouseDelta     { get; set; } = Vector2.Zero;
        public float   MouseWheelMove { get; set; } = 0f;
        public bool    IsMouseCaptured    { get; set; } = false;
        public bool    IsKeyboardCaptured { get; set; } = false;
        public bool IsMouseButtonPressed(MouseButton b)   => false;
        public bool IsMouseButtonDown   (MouseButton b)   => false;
        public bool IsMouseButtonReleased(MouseButton b)  => false;
        public bool IsKeyPressed  (KeyboardKey k) => false;
        public bool IsKeyDown     (KeyboardKey k) => false;
        public bool IsKeyReleased (KeyboardKey k) => false;
        public int  GetKeyPressed()               => 0;
    }

    /// <summary>
    /// Test-scoped wrapper around a real <see cref="MapCanvas"/> (null-input) that
    /// tracks the last pushed tool.
    /// </summary>
    internal sealed class TestMapCanvas : MapCanvas
    {
        public TestMapCanvas() : base(new TestInputProvider()) { }
    }

    /// <summary>
    /// Simple <see cref="IScenarioStateProvider"/> test double.
    /// </summary>
    internal sealed class FakeStateProvider : IScenarioStateProvider
    {
        public ScenarioEditorState CurrentState { get; set; } = ScenarioEditorState.Idle;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // A001 — EditorSpawnAdapter
    // ═══════════════════════════════════════════════════════════════════════════

    public sealed class EditorSpawnAdapterTests
    {
        private readonly TestMapCanvas _canvas = new();
        private readonly FdpEventBus   _bus    = new();

        [Fact]
        public void StartPlacementMode_PushesCreationTool()
        {
            var adapter = new EditorSpawnAdapter(_canvas, _bus);
            adapter.StartPlacementMode(2001L, null);

            Assert.IsType<Hrot.ScenarioEditor.Tools.CreationTool>(_canvas.ActiveTool);
        }

        [Fact]
        public void StartAreaAuthoringMode_PushesPointSequenceTool()
        {
            var adapter = new EditorSpawnAdapter(_canvas, _bus);
            adapter.StartAreaAuthoringMode("");

            Assert.IsType<FDP.Toolkit.Vis2D.Tools.PointSequenceTool>(_canvas.ActiveTool);
        }

        [Fact]
        public void StartRouteAuthoringMode_PushesPointSequenceTool()
        {
            var adapter = new EditorSpawnAdapter(_canvas, _bus);
            adapter.StartRouteAuthoringMode();

            Assert.IsType<FDP.Toolkit.Vis2D.Tools.PointSequenceTool>(_canvas.ActiveTool);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // A002 — EditorMissionService
    // ═══════════════════════════════════════════════════════════════════════════

    public sealed class EditorMissionServiceTests : IDisposable
    {
        private readonly EntityRepository _repo;
        private readonly FdpEventBus      _bus;
        private readonly DoctrineRegistry _registry;

        public EditorMissionServiceTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<TkbIdentity>();
            _repo.RegisterManagedComponent<ActiveMissionPlan>();
            _repo.RegisterEvent<MissionControlAckEvent>();

            _bus      = _repo.Bus;
            _registry = new DoctrineRegistry();
        }

        public void Dispose() => _repo.Dispose();

        [Fact]
        public void GetAvailableBehaviors_InsurgentWithRegisteredAmbush_ReturnsAmbush()
        {
            // Register "Ambush" doctrine.
            _registry.Register(1, "Ambush", new DoctrineDefinition
            {
                Name       = "Ambush",
                BrainTier  = BehaviorConstants.BrainTierBTree,
            });

            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new TkbIdentity { TkbType = TkbEntityTypes.Insurgent });

            var service = new EditorMissionService(_bus, _repo, _registry);
            var behaviors = service.GetAvailableBehaviors((long)entity.Index);

            Assert.Contains("Ambush", behaviors);
        }

        [Fact]
        public void GetAvailableBehaviors_DeadEntity_ReturnsEmpty()
        {
            var entity = _repo.CreateEntity();
            _repo.DestroyEntity(entity);

            var service = new EditorMissionService(_bus, _repo, _registry);
            var behaviors = service.GetAvailableBehaviors((long)entity.Index);

            Assert.Empty(behaviors);
        }

        [Fact]
        public void CommitMissionAsync_PollAcksWithMatchingAck_ResolvesSuccess()
        {
            var entity = _repo.CreateEntity();
            var service = new EditorMissionService(_bus, _repo, _registry);

            var plan = new Hrot.NED.Descriptors.MissionPlan
            {
                ActiveTaskId = Guid.Empty,
                Tasks = new System.Collections.Generic.List<Hrot.NED.Descriptors.MissionTask>(),
            };

            Task<MissionCommitResult> task = service.CommitMissionAsync((long)entity.Index, plan, 0L);

            // Inject an ACK event that matches the published intent.
            // SwapBuffers is required — PublishManaged writes to the "next frame" buffer.
            _bus.SwapBuffers();
            var intents = _bus.ConsumeManaged<MissionControlIntent>();
            Assert.Single(intents);

            Guid requestId = intents[0].RequestId;

            // Publish a matching ACK.
            _bus.Publish(new MissionControlAckEvent
            {
                RequestId  = requestId,
                ErrorCode  = 0,
                NewVersion = 1L,
            });
            _bus.SwapBuffers(); // make the ACK event visible to Consume<>

            service.PollAcks();

            Assert.True(task.IsCompleted);
            Assert.True(task.Result.Success);
            Assert.Equal(1L, task.Result.NewVersion);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // A003 — EditorOrbatAdapter
    // ═══════════════════════════════════════════════════════════════════════════

    public sealed class EditorOrbatAdapterTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly FdpEventBus      _bus;

        public EditorOrbatAdapterTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<EntityInfo>();
            // Register events for embark/disembark (EmbarkEntityCommand is [EventId] so it's auto-registered)
            _bus = _world.Bus;
        }

        public void Dispose() => _world.Dispose();

        private EditorOrbatAdapter CreateAdapter()
        {
            var mockLogic = new Mock<IEditorLogic>();
            var mockSpawn = new Mock<ISpawnController>();
            return new EditorOrbatAdapter(_world, _bus, mockLogic.Object, mockSpawn.Object);
        }

        [Fact]
        public void GetVisibleNodes_TwoEntities_ReturnsCorrectDepths()
        {
            // Entity index 0 is the CommanderId=0 sentinel ("no commander").
            // Burn it so parent starts at index 1.
            _world.CreateEntity();

            var parent = _world.CreateEntity();
            var child  = _world.CreateEntity();

            _world.AddComponent(parent, new EntityInfo
            {
                Name       = new Fdp.Kernel.FixedString64("Alpha"),
                CommanderId = 0,
            });
            _world.AddComponent(child, new EntityInfo
            {
                Name       = new Fdp.Kernel.FixedString64("Bravo"),
                CommanderId = parent.Index, // parent.Index == 1 (not 0)
            });

            var adapter = CreateAdapter();
            var nodes   = adapter.GetVisibleNodes("", new HashSet<int>());

            Assert.Equal(2, nodes.Count);
            var parentNode = nodes[0]; // root first (depth 0)
            var childNode  = nodes[1];

            Assert.Equal(0, parentNode.Depth);
            Assert.Equal(1, childNode.Depth);
        }

        [Fact]
        public void GetVisibleNodes_WithFilter_ExcludesNonMatchingNodes()
        {
            _world.CreateEntity(); // burn index 0 (CommanderId sentinel)
            var e1 = _world.CreateEntity();
            var e2 = _world.CreateEntity();

            _world.AddComponent(e1, new EntityInfo
            {
                Name       = new Fdp.Kernel.FixedString64("Tiger"),
                CommanderId = 0,
            });
            _world.AddComponent(e2, new EntityInfo
            {
                Name       = new Fdp.Kernel.FixedString64("Wolf"),
                CommanderId = 0,
            });

            var adapter = CreateAdapter();
            var nodes   = adapter.GetVisibleNodes("Tiger", new HashSet<int>());

            Assert.Single(nodes);
            Assert.Equal("Tiger", nodes[0].Name);
        }

        [Fact]
        public void RequestEmbark_PublishesEmbarkEntityCommand()
        {
            _world.CreateEntity(); // burn index 0 (CommanderId sentinel)
            var passenger = _world.CreateEntity();
            var vehicle   = _world.CreateEntity();

            _world.AddComponent(passenger, new EntityInfo
            {
                Name        = new Fdp.Kernel.FixedString64("Soldier"),
                CommanderId = 0,
            });
            _world.AddComponent(vehicle, new EntityInfo
            {
                Name        = new Fdp.Kernel.FixedString64("Apc"),
                CommanderId = 0,
            });

            var adapter = CreateAdapter();
            // Populate the index cache.
            adapter.GetVisibleNodes("", new HashSet<int>());

            adapter.RequestEmbark(passenger.Index, vehicle.Index);

            _bus.SwapBuffers();
            var events = _bus.Consume<EmbarkEntityCommand>().ToArray();
            Assert.Single(events);
            Assert.Equal(passenger, events[0].Passenger);
            Assert.Equal(vehicle,   events[0].Vehicle);
        }

        [Fact]
        public void RequestDisembark_PublishesDisembarkEntityCommand()
        {
            _world.CreateEntity(); // burn index 0 (CommanderId sentinel)
            var passenger = _world.CreateEntity();
            _world.AddComponent(passenger, new EntityInfo
            {
                Name        = new Fdp.Kernel.FixedString64("Soldier"),
                CommanderId = 0,
            });

            var adapter = CreateAdapter();
            adapter.GetVisibleNodes("", new HashSet<int>());

            adapter.RequestDisembark(passenger.Index);

            _bus.SwapBuffers();
            var events = _bus.Consume<DisembarkEntityCommand>().ToArray();
            Assert.Single(events);
            Assert.Equal(passenger, events[0].Passenger);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // A004 — EditorMapPickAdapter
    // ═══════════════════════════════════════════════════════════════════════════

    public sealed class EditorMapPickAdapterTests
    {
        private readonly TestMapCanvas _canvas = new();

        [Fact]
        public async Task PickLocationAsync_ToolFires_TaskCompletesWithGeoPoint()
        {
            var adapter = new EditorMapPickAdapter(_canvas);
            Task<GeoPoint> task = adapter.PickLocationAsync();

            // Simulate the tool firing the callback.
            var tool = Assert.IsType<LocationPickerTool>(_canvas.ActiveTool);
            tool.OnLocationPicked?.Invoke(new GeoPoint { Latitude = 32.0, Longitude = 34.5 });

            var result = await task;
            Assert.Equal(32.0, result.Latitude, 6);
            Assert.Equal(34.5, result.Longitude, 6);
        }

        [Fact]
        public async Task PickLocationAsync_CancellationToken_TaskCancelled()
        {
            var cts     = new CancellationTokenSource();
            var adapter = new EditorMapPickAdapter(_canvas);
            Task<GeoPoint> task = adapter.PickLocationAsync(cts.Token);

            // Cancel before the pick fires.
            cts.Cancel();

            await Assert.ThrowsAsync<TaskCanceledException>(() => task);
        }

        [Fact]
        public async Task PickAreaEntitiesAsync_ToolFires_TaskCompletesWithList()
        {
            var adapter = new EditorMapPickAdapter(_canvas);
            Task<IReadOnlyList<int>> task = adapter.PickAreaEntitiesAsync();

            var tool = Assert.IsType<ModalBoxSelectionTool>(_canvas.ActiveTool);
            tool.OnSelectionComplete?.Invoke(new[] { 1, 2, 3 });

            var result = await task;
            Assert.Equal(3, result.Count);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // A005 — EditorZoneAdapter
    // ═══════════════════════════════════════════════════════════════════════════

    public sealed class EditorZoneAdapterTests
    {
        private readonly TestMapCanvas _canvas = new();
        private readonly FdpEventBus   _bus    = new();

        [Fact]
        public void SetRoadNetworkPath_PublishesUpdateZoneConfigCommand()
        {
            var adapter = new EditorZoneAdapter(_canvas, _bus);
            adapter.SetRoadNetworkPath("zone_alpha", "Assets/roads.json");

            _bus.SwapBuffers(); // PublishManaged writes to next-frame buffer
            var events = _bus.ConsumeManaged<UpdateZoneConfigCommand>();
            Assert.Single(events);
            Assert.Equal("zone_alpha",       events[0].ZoneName);
            Assert.Equal("Assets/roads.json", events[0].RoadNetworkPath);
        }

        [Fact]
        public void StartObstaclePlacementMode_PushesObstaclePlacementTool()
        {
            var adapter = new EditorZoneAdapter(_canvas, _bus);
            adapter.StartObstaclePlacementMode("zone_alpha", 10f);

            Assert.IsType<ObstaclePlacementTool>(_canvas.ActiveTool);
        }

        [Fact]
        public void StartObstaclePlacementMode_OnClick_PublishesSpawnZoneObstacleCommand()
        {
            var adapter = new EditorZoneAdapter(_canvas, _bus);
            adapter.StartObstaclePlacementMode("zone_beta", 5f);

            var tool = Assert.IsType<ObstaclePlacementTool>(_canvas.ActiveTool);
            tool.OnObstaclePlaced?.Invoke(new Vector2(100f, 200f));

            _bus.SwapBuffers(); // PublishManaged writes to next-frame buffer
            var events = _bus.ConsumeManaged<SpawnZoneObstacleCommand>();
            Assert.Single(events);
            Assert.Equal("zone_beta", events[0].ZoneName);
            Assert.Equal(5f,          events[0].Radius);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // A007 — EditorPreviewAdapter
    // ═══════════════════════════════════════════════════════════════════════════

    public sealed class EditorPreviewAdapterTests : IDisposable
    {
        private readonly EntityRepository    _repo;
        private readonly PreviewClusterOpHandler _handler;
        private readonly FakeStateProvider   _state;

        public EditorPreviewAdapterTests()
        {
            _repo    = new EntityRepository();
            _handler = new PreviewClusterOpHandler(_repo);
            _state   = new FakeStateProvider();
        }

        public void Dispose() => _repo.Dispose();

        [Fact]
        public void IsInPreviewMode_OperatingPreview_ReturnsTrue()
        {
            _state.CurrentState = ScenarioEditorState.OperatingPreview;
            var adapter = new EditorPreviewAdapter(_handler, _state);

            Assert.True(adapter.IsInPreviewMode);
        }

        [Fact]
        public void IsInPreviewMode_LoadingPreview_ReturnsTrue()
        {
            _state.CurrentState = ScenarioEditorState.LoadingPreview;
            var adapter = new EditorPreviewAdapter(_handler, _state);

            Assert.True(adapter.IsInPreviewMode);
        }

        [Fact]
        public void IsInPreviewMode_OperatingEdit_ReturnsFalse()
        {
            _state.CurrentState = ScenarioEditorState.OperatingEdit;
            var adapter = new EditorPreviewAdapter(_handler, _state);

            Assert.False(adapter.IsInPreviewMode);
        }

        [Fact]
        public void EnterPreviewMode_CreatesSnapshot()
        {
            // Register a component so the snapshot has content.
            _repo.RegisterComponent<TkbIdentity>();
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new TkbIdentity { TkbType = 99L });

            var adapter = new EditorPreviewAdapter(_handler, _state);
            adapter.EnterPreviewMode();

            // The internal snap should now be non-null (verified via internal accessor).
            Assert.NotNull(_handler.TestHook_Snap);
        }

        [Fact]
        public void ExitPreviewMode_AfterEnter_ClearsSnapshot()
        {
            _repo.RegisterComponent<TkbIdentity>();

            var adapter = new EditorPreviewAdapter(_handler, _state);
            adapter.EnterPreviewMode();
            adapter.ExitPreviewMode();

            Assert.Null(_handler.TestHook_Snap);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // A008 — EditorMapConfigAdapter
    // ═══════════════════════════════════════════════════════════════════════════

    public sealed class EditorMapConfigAdapterTests
    {
        [Fact]
        public void GetCurrentConfig_ReflectsMapViewConfigDefaults()
        {
            var config  = new MapViewConfig();
            var canvas  = new FDP.Toolkit.Vis2D.MapCanvas();
            var adapter = new EditorMapConfigAdapter(config, canvas);

            MapLayerState state = adapter.GetCurrentConfig();

            Assert.Equal(config.ShowSatelliteLayer, state.Satellite);
            Assert.Equal(config.ShowGrid,           state.Grid);
            // Canvas starts with ActiveLayerMask = 0xFFFFFFFF — all layers on.
            Assert.True(state.GroundUnits);
            Assert.True(state.AirUnits);
            Assert.True(state.Vehicles);
            Assert.True(state.TacticalGraphics);
            Assert.True(state.RoadGraphs);
        }

        [Fact]
        public void ApplyConfig_SatelliteOff_UpdatesShowSatelliteLayerToFalse()
        {
            var config  = new MapViewConfig { ShowSatelliteLayer = true };
            var canvas  = new FDP.Toolkit.Vis2D.MapCanvas();
            var adapter = new EditorMapConfigAdapter(config, canvas);

            adapter.ApplyConfig(new MapLayerState(
                Satellite:        false,
                GroundUnits:      true,
                AirUnits:         true,
                Vehicles:         true,
                TacticalGraphics: true,
                RoadGraphs:       true,
                Grid:             false));

            Assert.False(config.ShowSatelliteLayer);
            Assert.False(config.ShowGrid);
        }

        [Fact]
        public void ApplyConfig_VehiclesOff_ClearsVehiclesBitInCanvasMask()
        {
            var config  = new MapViewConfig();
            var canvas  = new FDP.Toolkit.Vis2D.MapCanvas();
            var adapter = new EditorMapConfigAdapter(config, canvas);

            adapter.ApplyConfig(new MapLayerState(
                Satellite:        true,
                GroundUnits:      true,
                AirUnits:         true,
                Vehicles:         false,
                TacticalGraphics: true,
                RoadGraphs:       true,
                Grid:             false));

            Assert.Equal(0u, canvas.ActiveLayerMask & Hrot.Map.Common.Config.MapLayerBits.VehiclesBit);
        }

        [Fact]
        public void ApplyConfig_AllTrue_SetsAllFieldsTrue()
        {
            var config  = new MapViewConfig();
            var canvas  = new FDP.Toolkit.Vis2D.MapCanvas();
            var adapter = new EditorMapConfigAdapter(config, canvas);

            adapter.ApplyConfig(new MapLayerState(true, true, true, true, true, true, true));

            Assert.True(config.ShowSatelliteLayer);
            Assert.True(config.ShowGrid);
            Assert.NotEqual(0u, canvas.ActiveLayerMask & Hrot.Map.Common.Config.MapLayerBits.GroundUnitsBit);
            Assert.NotEqual(0u, canvas.ActiveLayerMask & Hrot.Map.Common.Config.MapLayerBits.AirUnitsBit);
            Assert.NotEqual(0u, canvas.ActiveLayerMask & Hrot.Map.Common.Config.MapLayerBits.VehiclesBit);
        }
    }
}
