using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using Fdp.Core;
using Hrot.Core.Network;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Vis2D;
using EcsNavigationIntent = Fdp.Toolkit.Navigation.NavigationIntent;
using EcsNavigationMode = Fdp.Toolkit.Navigation.NavigationMode;
using Fdp.Toolkit.Vis2D.Components;
using FdpEntityInspectorPanel = Fdp.Presentation.Panels.EntityInspectorPanel;
using FdpEventBrowserPanel = Fdp.Presentation.Panels.EventBrowserPanel;
using FdpRepositoryAdapter = Fdp.Presentation.Adapters.RepositoryAdapter;
using FdpInspectorState = Fdp.Presentation.Abstractions.InspectorState;
using Fdp.Presentation.Utils;
using Fdp.Toolkit.Vis2D.Layers;
using Fdp.Toolkit.Vis2D.Tools;
using Hrot.Presentation.Facades;
using Hrot.UI.Common.Facades;
using CarKinem.Commands;
using CarKinem.Core;
using CarKinem.Trajectory;
using Fdp.ModuleHost;
using Hrot.SimHost.UI;
using Hrot.SimHost.Visualization;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Utilities;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Hrot.Map.Common.Events;
using Fdp.Toolkit.NetworkSpawning;
using Hrot.Presentation.Adapters;

namespace Hrot.SimHost
{
    /// <summary>
    /// Self-contained graphical visualization layer for the SimHost subsystem.
    ///
    /// <para>Lifecycle (called by <see cref="Hrot.ClusterRunner.Services.SimHostSubsystem"/>):
    /// <list type="number">
    ///   <item><see cref="Initialize"/> — create canvas, layers, tools and UI once.</item>
    ///   <item><see cref="Update"/> — process input, advance tool state, update scenario.</item>
    ///   <item><see cref="DrawWorld"/> — render map canvas (2-D world, Raylib only).</item>
    ///   <item><see cref="DrawUI"/> — render ImGui panels (called inside rlImGui.Begin/End).</item>
    ///   <item><see cref="Dispose"/> — release unmanaged resources.</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class SimHostVisualization : IDisposable
    {
        // ── Core ECS refs ─────────────────────────────────────────────────────
        private EntityRepository?   _repo;
        private ModuleHostKernel?   _kernel;

        // ── Visualization ─────────────────────────────────────────────────────
        private MapCanvas?              _map;
        private SimHostVehicleVisualizer? _visualizer;
        private SimHostSelectionManager?  _selection;
        private SimHostInspectorAdapter?  _inspector;
        private StandardInteractionTool?  _interactionTool;
        private EntityQuery?              _vehicleQuery;

        // ── UI ────────────────────────────────────────────────────────────────
        private SimHostMainUI?         _ui;
        private SimHostScenarioManager? _scenario;

        // ── FDP framework panels (Task 16) ─────────────────────────────────────
        private FdpEntityInspectorPanel _fdpEntityInspector = new();
        private FdpEventBrowserPanel    _fdpEventBrowser    = new();
        private FdpRepositoryAdapter?   _fdpRepoAdapter;
        private FdpInspectorState       _fdpInspectorState  = new();
        private uint                    _fdpFrameCount;
        private MapPickServiceBridge?   _mapPickBridge;

        /// <summary>When set, the Window Manager renders these panels; DrawUI skips them.</summary>
        private bool _panelsWindowManaged;

        // ── Mission control (right-click navigate via doctrine) ───────────────
        private ISimHostMissionSender? _missionSender;

        private long _worldPosDescriptorId;
        private bool _initialized;

        // ── Public access (tests / other subsystems) ──────────────────────────
        public SimHostSelectionManager? Selection => _selection;

        /// <summary>Returns the map camera or <see langword="null"/> when not initialized.</summary>
        public MapCamera? GetMapCamera() => _map?.Camera;

        // ── Window-manager panel accessors ────────────────────────────────────
        /// <summary>The SimHost controls UI (simulation + spawn panels).</summary>
        public SimHostMainUI?            UI                 => _ui;
        /// <summary>Getter for the ECS repository (available after Initialize).</summary>
        public EntityRepository?         GetRepo()          => _repo;
        /// <summary>Getter for the module host kernel (available after Initialize).</summary>
        public ModuleHostKernel?         GetKernel()        => _kernel;
        /// <summary>Getter for the scenario manager (available after Initialize).</summary>
        public SimHostScenarioManager?   GetScenario()      => _scenario;
        /// <summary>The FDP entity inspector panel.</summary>
        public FdpEntityInspectorPanel   FdpEntityInspector => _fdpEntityInspector;
        /// <summary>The FDP event browser panel.</summary>
        public FdpEventBrowserPanel      FdpEventBrowser    => _fdpEventBrowser;
        /// <summary>Getter for the FDP repository adapter (available after first DrawUI call).</summary>
        public FdpRepositoryAdapter?     GetFdpRepoAdapter() => _fdpRepoAdapter;
        /// <summary>The FDP inspector state.</summary>
        public FdpInspectorState         FdpInspectorState  => _fdpInspectorState;
        /// <summary>Map-pick bridge for component-editor map picking (available after Initialize).</summary>
        public MapPickServiceBridge?     GetMapPickBridge()  => _mapPickBridge;

        /// <summary>
        /// Signals that panels have been registered with a Window Manager.
        /// After this call <see cref="DrawUI"/> only renders popups.
        /// </summary>
        public void SetPanelsWindowManaged() => _panelsWindowManaged = true;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        /// <summary>
        /// Wires the visualization to a fully-initialised ECS world.
        /// Must be called after the kernel is initialized and road/trajectory data
        /// are available (i.e. after <c>SimulationLogicModule.RegisterSystems</c>).
        /// </summary>
        public void Initialize(
            EntityRepository        repo,
            ModuleHostKernel        kernel,
            CarKinem.Road.RoadNetworkBlob road,
            TrajectoryPoolManager    trajectoryPool,
            CarKinem.Formation.FormationTemplateManager formationTemplates,
            ISimHostMissionSender missionSender,
            INetworkIdAllocator?    idAllocator = null,
            int                     localNodeId = 0,
            long                    worldPosDescriptorId = 0)
        {
            _repo                 = repo         ?? throw new ArgumentNullException(nameof(repo));
            _kernel               = kernel        ?? throw new ArgumentNullException(nameof(kernel));
            _missionSender        = missionSender ?? throw new ArgumentNullException(nameof(missionSender));
            _worldPosDescriptorId = worldPosDescriptorId;

            // ── Selection & inspector ─────────────────────────────────────────
            _selection = new SimHostSelectionManager();
            _inspector = new SimHostInspectorAdapter(_selection, repo);
            _fdpRepoAdapter = new FdpRepositoryAdapter(repo);
            _fdpEventBrowser.RegisterBus("World (Main Simulation)", repo.Bus);

            // Task 47: register context menu handlers for the FDP entity inspector.
            _fdpEntityInspector.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
            {
                builder.AddItem("Center on entity", () => CenterCameraOnEntity(entity));
                builder.AddItem("Select entity", () =>
                {
                    _selection!.Set(entity);
                    _fdpInspectorState.SelectedEntity = entity;
                });

                builder.AddSeparator();
                builder.AddItem("Delete entity", () =>
                {
                    if (_repo!.IsAlive(entity))
                    {
                        if (_repo.HasComponent<NetworkIdentity>(entity))
                        {
                            ref readonly var netId = ref _repo.GetComponentRO<NetworkIdentity>(entity);
                            _repo.Bus.PublishManaged(new DestroyEntityCommand
                            {
                                NetworkId = netId.Value,
                                Reason    = "inspector-deleted"
                            });
                        }
                        else
                        {
                            _repo.DestroyEntity(entity);
                        }

                        if (_selection!.Contains(entity))
                        {
                            _selection.Remove(entity);
                            _fdpInspectorState.SelectedEntity = null;
                        }
                    }
                });
            }));

            // ── Scenario manager ──────────────────────────────────────────────
            _scenario = new SimHostScenarioManager(repo, road, trajectoryPool, formationTemplates, idAllocator: idAllocator, localNodeId: localNodeId);

            // ── UI ────────────────────────────────────────────────────────────
            _ui = new SimHostMainUI();

            // ── Entity query (vehicles) ───────────────────────────────────────
            _vehicleQuery = repo.Query().With<VehicleState>().With<VehicleParams>().Build();

            // ── Map canvas & layers ───────────────────────────────────────────
            _map       = new MapCanvas();
            _map.Camera.Offset = new Vector2(1280 / 2f, 720 / 2f);
            _map.AddResource(trajectoryPool);

            _map.AddLayer(new SimHostRoadLayer(road));

            _visualizer = new SimHostVehicleVisualizer(
                new Fdp.Toolkit.Vis2D.Shapes.DefaultEntityShapeLibrary());
            _map.AddLayer(new EntityRenderLayer(
                "Vehicles", 0, repo, _vehicleQuery, _visualizer, _inspector));

            _map.AddLayer(ProjectileLayerFactory.CreateLayer(repo, _inspector, _map));

            _map.AddLayer(new SimHostTrajectoryLayer(trajectoryPool, repo, _inspector));

            // ── Interaction tool ──────────────────────────────────────────────
            _interactionTool = new StandardInteractionTool(repo, _vehicleQuery, _visualizer);

            _interactionTool.OnEntitySelectRequest += (entity, augment) =>
            {
                if (!repo.IsAlive(entity))
                {
                    if (!augment) { _selection.Clear(); _fdpInspectorState.SelectedEntity = null; }
                    return;
                }
                if (augment) _selection.Add(entity);
                else         _selection.Set(entity);

                // Task 43: keep FDP entity inspector in sync with map selection
                if (!augment)
                    _fdpInspectorState.SelectedEntity = entity;
            };

            _interactionTool.OnEntityMoved += (entity, pos) =>
            {
                if (!repo.IsAlive(entity) || !repo.HasComponent<SimTransform>(entity)) return;
                ref var tf = ref repo.GetComponentRW<SimTransform>(entity);
                tf.Position = new Vector3(pos.X, pos.Y, 0);
                if (repo.HasComponent<VehicleState>(entity))
                {
                    ref var vs = ref repo.GetComponentRW<VehicleState>(entity);
                    vs.Speed = 0;
                }
                SmartEgressUtil.MarkDirty(repo, entity, _worldPosDescriptorId);
            };

            _interactionTool.OnRegionSelected += entities => _selection.SetMultiple(entities);

            _interactionTool.OnWorldClick += (pos, btn, shift, ctrl, hitEntity) =>
            {
                if (btn != MouseButton.Right) return;
                var entities = new List<Fdp.Core.Entity>(_selection.SelectedEntities);
                if (entities.Count == 0) return;

                var interp = _ui!.UIState.InterpolationMode;
                foreach (var e in entities)
                {
                    if (!repo.IsAlive(e)) continue;
                    HandleRightClickForEntity(
                        repo, e, pos, shift, interp,
                        (ent, p, i) => _scenario!.SetDestination(ent, p, i),
                        // Shift+right-click: route through the ECS personal-route system
                        // (PersonalRouteAuthoringSystem) instead of the legacy AddWaypoint path.
                        (ent, p, i) => repo.Bus.Publish(new CmdAppendPersonalWaypoint
                        {
                            VehicleEntity = ent,
                            WorldPosition = new System.Numerics.Vector3(p.X, 0f, p.Y),
                        }),
                        _missionSender);
                }
            };

            _map.SwitchTool(_interactionTool);

            // Route Delete key through the tool pipeline so ImGui keyboard capture
            // (e.g. editing a value in a component window) is always respected.
            _interactionTool.OnDeleteRequested += () =>
            {
                if (_selection == null || _repo == null) return;
                foreach (var e in new List<Fdp.Core.Entity>(_selection.SelectedEntities))
                {
                    if (!_repo.IsAlive(e)) continue;

                    if (_repo.HasComponent<NetworkIdentity>(e))
                    {
                        // Network-replicated entity -- route through NetworkSpawningSystem
                        // so the IG ghost is also removed via DDS EntityMaster DISPOSE.
                        ref readonly var netId = ref _repo.GetComponentRO<NetworkIdentity>(e);
                        _repo.Bus.PublishManaged(new DestroyEntityCommand
                        {
                            NetworkId = netId.Value,
                            Reason    = "user-deleted",
                        });
                    }
                    else
                    {
                        // Local-only entity -- destroy directly.
                        _repo.DestroyEntity(e);
                    }
                }
                _selection.Clear();
            };

            _mapPickBridge = new MapPickServiceBridge(new CanvasMapPickAdapter(_map, repo), repo);

            // Seed a small initial scenario so the window isn't empty
            //_scenario.SpawnFastOne();

            _initialized = true;
        }

        /// <summary>
        /// Processes a right-click interaction for a single entity using brain-aware routing.
        ///
        /// <list type="bullet">
        ///   <item><b>Brain-dead</b> (<c>DoctrineState</c> absent or
        ///         <c>ActiveDoctrineHash == DoctrineIds.None</c>): talks directly to the
        ///         muscle layer via <paramref name="setDestination"/> / <paramref name="addWaypoint"/>.</item>
        ///   <item><b>Brain-active</b> (<c>ActiveDoctrineHash != DoctrineIds.None</c>): sends a
        ///         <c>CMD_REPLACE_MISSION</c> via <paramref name="missionWriter"/>. The task
        ///         includes a <c>DoctrineFinished</c> trigger so <c>MissionDirectorSystem</c>
        ///         can advance and ultimately clear the doctrine when the plan exhausts.</item>
        /// </list>
        ///
        /// Extracted from the <c>OnWorldClick</c> lambda for unit-test accessibility.
        /// </summary>
        internal static void HandleRightClickForEntity(
            EntityRepository repo,
            Entity entity,
            Vector2 pos,
            bool shift,
            TrajectoryInterpolation interp,
            Action<Entity, Vector2, TrajectoryInterpolation> setDestination,
            Action<Entity, Vector2, TrajectoryInterpolation> addWaypoint,
            ISimHostMissionSender? missionSender)
        {
            // Determine if the entity has an active (non-zero) doctrine.
            bool brainActive = repo.HasComponent<DoctrineState>(entity)
                && repo.GetComponent<DoctrineState>(entity).ActiveDoctrineHash != DoctrineIds.None;

            if (!brainActive)
            {
                // Brain-dead path: bypass the mission machinery.
                if (shift)
                    addWaypoint(entity, pos, interp);
                else
                {
                    // TODO: TargetSpeed and ArrivalRadius should eventually be configurable.
                    EcsNavigationIntent intent = repo.HasComponent<EcsNavigationIntent>(entity)
                        ? repo.GetComponent<EcsNavigationIntent>(entity)
                        : new EcsNavigationIntent();
                    intent.IntentId++;
                    intent.Mode = EcsNavigationMode.DirectPoint;
                    intent.FinalDestination = pos;
                    intent.TargetSpeed = 15f;
                    intent.ArrivalRadius = 3.0f;
                    if (repo.HasComponent<EcsNavigationIntent>(entity))
                        repo.SetComponent(entity, intent);
                    else
                        repo.AddComponent(entity, intent);
                }
                return;
            }

            // Brain-active path: route through the mission pipeline.
            // Shift is not yet supported for brain-active entities — behaves like plain click.
            if (!repo.HasComponent<NetworkIdentity>(entity)) return;

            ref readonly var netId = ref repo.GetComponentRO<NetworkIdentity>(entity);

            float speed = repo.HasComponent<VehicleParams>(entity)
                ? repo.GetComponent<VehicleParams>(entity).MaxSpeedFwd * 0.8f
                : 15f;

            missionSender?.SendNavigateToPoint(netId.Value, pos, speed, 3.0f);
        }

        /// <summary>Advances input, tool state, and roaming AI each frame.</summary>
        public void Update(float dt)
        {
            if (!_initialized || _repo == null || _map == null || _ui == null) return;

            // Time-scale forwarding
            if (_kernel != null)
                _kernel.GetTimeController().SetTimeScale(_ui.TimeScale);

            _scenario?.Update();
            _map.Update(dt);

            _fdpFrameCount++;
            _fdpEventBrowser.Update(_fdpFrameCount);
        }

        /// <summary>Renders the 2-D map canvas.  Must be called inside Raylib BeginDrawing.</summary>
        public void DrawWorld()
        {
            if (!_initialized || _map == null) return;
            _map.Draw();
        }

        /// <summary>Renders ImGui panels.  Must be called inside rlImGui.Begin/End.</summary>
        public void DrawUI()
        {
            if (!_initialized || _repo == null || _kernel == null) return;

            // When panels are Window Manager managed, skip rendering them here.
            if (!_panelsWindowManaged && _ui != null)
                _ui.Render(_repo, _kernel, _scenario!, _inspector!);

            // NOTE: The old SimHost-specific perspective toggle toolbar has been removed.
            // Map perspective switching is now handled by the Window Manager's perspective
            // switcher (radio buttons in the main menu bar), which fires OnPerspectiveChanged
            // → PerspectiveCoordinatorSystem.SwitchMapOwner().

            if (!_panelsWindowManaged)
            {
                SimHostPanelColors.Push();
                _fdpEntityInspector.Draw(_fdpRepoAdapter!, _fdpInspectorState, "SimHost Entity Inspector");
                SimHostPanelColors.Pop();

                SimHostPanelColors.Push();
                _fdpEventBrowser.Draw("SimHost Event Browser");
                SimHostPanelColors.Pop();
            }
        }

        public void Dispose()
        {
            // EntityQuery is managed by EntityRepository; no dispose needed.
            _initialized = false;
        }

        /// <summary>Centres the map camera on the entity's SimTransform position.</summary>
        private void CenterCameraOnEntity(Fdp.Core.Entity entity)
        {
            if (_repo == null || !_repo.IsAlive(entity)) return;
            if (!_repo.HasComponent<SimTransform>(entity)) return;
            ref readonly var tf = ref _repo.GetComponentRO<SimTransform>(entity);
            _map?.Camera.FocusOn(new Vector2(tf.Position.X, tf.Position.Y));
        }
    }
}
