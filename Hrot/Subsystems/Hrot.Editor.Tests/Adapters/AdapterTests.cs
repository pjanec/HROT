using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Hrot.Common.Events;
using Hrot.Common.Orchestration.Handlers;
using Hrot.Editor.Adapters;
using Hrot.Map.Common;
using Hrot.Map.Common.Config;
using Hrot.Map.Common.Events;
using Hrot.Map.Definitions.Tkb;
using Hrot.NED.Common;
using Hrot.Core.Mission;
using Hrot.ScenarioEditor;
using Hrot.ScenarioEditor.Gizmos;
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
        public bool IsMouseButtonPressed(MapMouseButton b)   => false;
        public bool IsMouseButtonDown   (MapMouseButton b)   => false;
        public bool IsMouseButtonReleased(MapMouseButton b)  => false;
        public bool IsKeyPressed  (MapKeyboardKey k) => false;
        public bool IsKeyDown     (MapKeyboardKey k) => false;
        public bool IsKeyReleased (MapKeyboardKey k) => false;
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
        public void StartPlacementMode_PushesPlacementCanvasBridge()
        {
            var adapter = new EditorSpawnAdapter(_canvas, _bus);
            adapter.StartPlacementMode(2001L, null);

            Assert.IsType<PlacementCanvasBridge>(_canvas.ActiveTool);
        }

        [Fact]
        public void StartAreaAuthoringMode_PushesPointSequenceTool()
        {
            var adapter = new EditorSpawnAdapter(_canvas, _bus);
            adapter.StartAreaAuthoringMode("");

            Assert.IsType<Fdp.Toolkit.Vis2D.Tools.PointSequenceTool>(_canvas.ActiveTool);
        }

        [Fact]
        public void StartRouteAuthoringMode_PushesPointSequenceTool()
        {
            var adapter = new EditorSpawnAdapter(_canvas, _bus);
            adapter.StartRouteAuthoringMode();

            Assert.IsType<Fdp.Toolkit.Vis2D.Tools.PointSequenceTool>(_canvas.ActiveTool);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // A002 — EditorMissionService
    // ═══════════════════════════════════════════════════════════════════════════

    public sealed class EditorMissionServiceTests : IDisposable
    {
        private readonly EntityRepository _repo;
        private readonly FdpEventBus      _bus;
        private readonly BehaviorRegistry _registry;

        public EditorMissionServiceTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<TkbIdentity>();
            _repo.RegisterComponent<NetworkIdentity>();
            _repo.RegisterManagedComponent<ActiveMissionPlan>();
            _repo.RegisterEvent<MissionControlAckEvent>();

            _bus      = _repo.Bus;
            _registry = new BehaviorRegistry();
        }

        public void Dispose() => _repo.Dispose();

        [Fact]
        public void GetAvailableBehaviors_InsurgentWithRegisteredAmbush_ReturnsAmbush()
        {
            // Register "Ambush" behavior.
            _registry.Register(1, "Ambush", new BehaviorDefinition
            {
                Name       = "Ambush",
                BrainTier  = BehaviorConstants.BrainTierBTree,
            });

            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new TkbIdentity { TkbType = TkbEntityTypes.Insurgent });
            _repo.AddComponent(entity, new NetworkIdentity { Value = (long)entity.Index });

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

            var plan = new Hrot.Core.Mission.MissionPlan
            {
                ActiveTaskId = Guid.Empty,
                Tasks = new System.Collections.Generic.List<Hrot.Core.Mission.MissionTask>(),
            };

            Task<MissionCommitResult> task = service.CommitMissionAsync((long)entity.Index, plan, 0L);

            // Inject an ACK event that matches the published intent.
            // SwapBuffers is required — PublishManaged writes to the "next frame" buffer.
            _bus.SwapBuffers();
            var intents = _bus.ReadManaged<MissionControlIntent>();
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
            _world.RegisterComponent<Fdp.Core.CommandHierarchy.UnitSubordinate>();
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
            // Burn index 0 so parent starts at index 1.
            _world.CreateEntity();

            var parent = _world.CreateEntity();
            var child  = _world.CreateEntity();

            _world.AddComponent(parent, new EntityInfo
            {
                Name       = new Fdp.Core.FixedString64("Alpha"),
            });
            // Child is subordinate to parent via UnitSubordinate component.
            _world.AddComponent(child, new EntityInfo
            {
                Name       = new Fdp.Core.FixedString64("Bravo"),
            });
            _world.AddComponent(child, new Fdp.Core.CommandHierarchy.UnitSubordinate { Commander = parent });

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
            _world.CreateEntity(); // burn index 0
            var e1 = _world.CreateEntity();
            var e2 = _world.CreateEntity();

            _world.AddComponent(e1, new EntityInfo
            {
                Name       = new Fdp.Core.FixedString64("Tiger"),
            });
            _world.AddComponent(e2, new EntityInfo
            {
                Name       = new Fdp.Core.FixedString64("Wolf"),
            });

            var adapter = CreateAdapter();
            var nodes   = adapter.GetVisibleNodes("Tiger", new HashSet<int>());

            Assert.Single(nodes);
            Assert.Equal("Tiger", nodes[0].Name);
        }

        [Fact]
        public void RequestEmbark_PublishesEmbarkEntityCommand()
        {
            _world.CreateEntity(); // burn index 0
            var passenger = _world.CreateEntity();
            var vehicle   = _world.CreateEntity();

            _world.AddComponent(passenger, new EntityInfo
            {
                Name        = new Fdp.Core.FixedString64("Soldier"),
            });
            _world.AddComponent(vehicle, new EntityInfo
            {
                Name        = new Fdp.Core.FixedString64("Apc"),
            });

            var adapter = CreateAdapter();
            // Populate the index cache.
            adapter.GetVisibleNodes("", new HashSet<int>());

            adapter.RequestEmbark(passenger.Index, vehicle.Index);

            _bus.SwapBuffers();
            var events = _bus.Read<EmbarkEntityCommand>().ToArray();
            Assert.Single(events);
            Assert.Equal(passenger, events[0].Passenger);
            Assert.Equal(vehicle,   events[0].Vehicle);
        }

        [Fact]
        public void RequestDisembark_PublishesDisembarkEntityCommand()
        {
            _world.CreateEntity(); // burn index 0
            var passenger = _world.CreateEntity();
            _world.AddComponent(passenger, new EntityInfo
            {
                Name        = new Fdp.Core.FixedString64("Soldier"),
            });

            var adapter = CreateAdapter();
            adapter.GetVisibleNodes("", new HashSet<int>());

            adapter.RequestDisembark(passenger.Index);

            _bus.SwapBuffers();
            var events = _bus.Read<DisembarkEntityCommand>().ToArray();
            Assert.Single(events);
            Assert.Equal(passenger, events[0].Passenger);
        }

        // CS020-T01
        [Fact]
        public void GetVisibleNodes_CommanderWithRoster_CanAcceptSubordinatesTrue()
        {
            _world.RegisterComponent<Fdp.Core.CommandHierarchy.UnitRoster>();
            _world.CreateEntity(); // burn index 0
            var commander = _world.CreateEntity();
            _world.AddComponent(commander, new EntityInfo { Name = new Fdp.Core.FixedString64("CMD") });
            _world.AddComponent(commander, new Fdp.Core.CommandHierarchy.UnitRoster());  // has roster

            var subordinate = _world.CreateEntity();
            _world.AddComponent(subordinate, new EntityInfo { Name = new Fdp.Core.FixedString64("SUB") });
            _world.AddComponent(subordinate, new Fdp.Core.CommandHierarchy.UnitSubordinate { Commander = commander });

            var adapter = CreateAdapter();
            var nodes   = adapter.GetVisibleNodes("", new HashSet<int>());

            var cmdNode = nodes.Single(n => n.EntityId == commander.Index);
            var subNode = nodes.Single(n => n.EntityId == subordinate.Index);

            Assert.True(cmdNode.CanAcceptSubordinates);
            Assert.False(subNode.CanAcceptSubordinates);
            Assert.Equal(1, subNode.Depth);
        }

        // CS020-T02
        [Fact]
        public void RequestAssignSubordinate_ValidEntities_PublishesCmdAssignSubordinate()
        {
            _world.RegisterEvent<Fdp.Core.CommandHierarchy.CmdAssignSubordinate>();
            _world.CreateEntity(); // burn index 0
            var commander  = _world.CreateEntity();
            var subordinate = _world.CreateEntity();
            _world.AddComponent(commander,   new EntityInfo { Name = new Fdp.Core.FixedString64("CMD") });
            _world.AddComponent(subordinate, new EntityInfo { Name = new Fdp.Core.FixedString64("SUB") });

            var adapter = CreateAdapter();
            adapter.GetVisibleNodes("", new HashSet<int>()); // populate cache

            adapter.RequestAssignSubordinate(subordinate.Index, commander.Index);

            _bus.SwapBuffers();
            var events = _bus.Read<Fdp.Core.CommandHierarchy.CmdAssignSubordinate>().ToArray();
            Assert.Single(events);
            Assert.Equal(subordinate, events[0].Subordinate);
            Assert.Equal(commander,   events[0].Commander);
        }

        // CS020-T03
        [Fact]
        public void RequestAssignSubordinate_UnknownEntity_DoesNotThrow()
        {
            _world.RegisterEvent<Fdp.Core.CommandHierarchy.CmdAssignSubordinate>();
            var adapter = CreateAdapter();

            // No exception; no event.
            adapter.RequestAssignSubordinate(999, 888);

            _bus.SwapBuffers();
            Assert.Empty(_bus.Read<Fdp.Core.CommandHierarchy.CmdAssignSubordinate>().ToArray());
        }

        // CS020-T04
        [Fact]
        public void RequestRemoveSubordinate_ValidEntity_PublishesCmdRemoveSubordinate()
        {
            _world.RegisterEvent<Fdp.Core.CommandHierarchy.CmdRemoveSubordinate>();
            _world.CreateEntity(); // burn index 0
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new EntityInfo { Name = new Fdp.Core.FixedString64("SUB") });

            var adapter = CreateAdapter();
            adapter.GetVisibleNodes("", new HashSet<int>()); // populate cache

            adapter.RequestRemoveSubordinate(entity.Index);

            _bus.SwapBuffers();
            var events = _bus.Read<Fdp.Core.CommandHierarchy.CmdRemoveSubordinate>().ToArray();
            Assert.Single(events);
            Assert.Equal(entity, events[0].Subordinate);
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
            var adapter = new EditorMapPickAdapter(_canvas, HrotEnvironment.CreateGeoTransform());
            Task<Hrot.Core.Mission.GeoPoint> task = adapter.PickLocationAsync();

            // Simulate the operator left-clicking.
            var bridge = Assert.IsType<PlacementCanvasBridge>(_canvas.ActiveTool);
            bridge.HandleClick(new Vector2(0f, 0f), MapMouseButton.Left);

            var result = await task;
            // The task should complete (the exact geo values depend on the transform).
            Assert.True(task.IsCompleted);
            Assert.False(task.IsFaulted);
            Assert.False(task.IsCanceled);
        }

        [Fact]
        public async Task PickLocationAsync_CancellationToken_TaskCancelled()
        {
            var cts     = new CancellationTokenSource();
            var adapter = new EditorMapPickAdapter(_canvas, HrotEnvironment.CreateGeoTransform());
            Task<Hrot.Core.Mission.GeoPoint> task = adapter.PickLocationAsync(cts.Token);

            // Verify the bridge is the active tool before cancellation.
            Assert.IsType<PlacementCanvasBridge>(_canvas.ActiveTool);

            cts.Cancel();

            await Assert.ThrowsAsync<TaskCanceledException>(() => task);
        }

        [Fact]
        public async Task PickAreaEntitiesAsync_ToolFires_TaskCompletesWithList()
        {
            var adapter = new EditorMapPickAdapter(_canvas, HrotEnvironment.CreateGeoTransform());
            Task<IReadOnlyList<int>> task = adapter.PickAreaEntitiesAsync();

            // Simulate a left-click to trigger the gizmo's selection complete callback.
            var bridge = Assert.IsType<PlacementCanvasBridge>(_canvas.ActiveTool);
            bridge.HandleClick(new Vector2(0f, 0f), MapMouseButton.Left);

            var result = await task;
            // Placeholder implementation returns empty list.
            Assert.NotNull(result);
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
            var events = _bus.ReadManaged<UpdateZoneConfigCommand>();
            Assert.Single(events);
            Assert.Equal("zone_alpha",       events[0].ZoneName);
            Assert.Equal("Assets/roads.json", events[0].RoadNetworkPath);
        }

        [Fact]
        public void StartObstaclePlacementMode_PushesPlacementCanvasBridge()
        {
            var adapter = new EditorZoneAdapter(_canvas, _bus);
            adapter.StartObstaclePlacementMode("zone_alpha", 10f);

            Assert.IsType<PlacementCanvasBridge>(_canvas.ActiveTool);
        }

        [Fact]
        public void StartObstaclePlacementMode_OnClick_PublishesSpawnZoneObstacleCommand()
        {
            var adapter = new EditorZoneAdapter(_canvas, _bus);
            adapter.StartObstaclePlacementMode("zone_beta", 5f);

            var bridge = Assert.IsType<PlacementCanvasBridge>(_canvas.ActiveTool);
            bridge.HandleClick(new Vector2(100f, 200f), MapMouseButton.Left);

            _bus.SwapBuffers();
            var events = _bus.ReadManaged<SpawnZoneObstacleCommand>();
            Assert.Single(events);
            Assert.Equal("zone_beta", events[0].ZoneName);
            Assert.Equal(100f, events[0].Position.X, precision: 2);
            Assert.Equal(200f, events[0].Position.Y, precision: 2);
            Assert.Equal(5f, events[0].Radius, precision: 2);
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
            var canvas  = new Fdp.Toolkit.Vis2D.MapCanvas();
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
            var canvas  = new Fdp.Toolkit.Vis2D.MapCanvas();
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
            var canvas  = new Fdp.Toolkit.Vis2D.MapCanvas();
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
            var canvas  = new Fdp.Toolkit.Vis2D.MapCanvas();
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
