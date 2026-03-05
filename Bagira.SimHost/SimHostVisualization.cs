using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Components;
using FdpEntityInspectorPanel = FDP.Toolkit.ImGui.Panels.EntityInspectorPanel;
using FdpEventBrowserPanel    = FDP.Toolkit.ImGui.Panels.EventBrowserPanel;
using FdpRepositoryAdapter    = FDP.Toolkit.ImGui.Adapters.RepositoryAdapter;
using FdpInspectorState       = FDP.Toolkit.ImGui.Abstractions.InspectorState;
using FDP.Toolkit.ImGui.Utils;
using FDP.Toolkit.Vis2D.Layers;
using FDP.Toolkit.Vis2D.Tools;
using CarKinem.Commands;
using CarKinem.Core;
using CarKinem.Trajectory;
using ModuleHost.Core;
using Bagira.SimHost.UI;
using Bagira.SimHost.Visualization;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Components;

namespace Bagira.SimHost
{
    /// <summary>
    /// Self-contained graphical visualization layer for the SimHost subsystem.
    ///
    /// <para>Lifecycle (called by <see cref="Bagira.Runner.Services.SimHostSubsystem"/>):
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

        private bool _initialized;

        // ── Public access (tests / other subsystems) ──────────────────────────
        public SimHostSelectionManager? Selection => _selection;

        /// <summary>
        /// Returns the map camera for this visualization, or <see langword="null"/> when
        /// not yet initialised.  Used by the Runner orchestrator to synchronise camera
        /// state when switching between IG and SimHost map perspectives.
        /// </summary>
        public MapCamera? GetMapCamera() => _map?.Camera;

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
            CarKinem.Formation.FormationTemplateManager formationTemplates)
        {
            _repo    = repo    ?? throw new ArgumentNullException(nameof(repo));
            _kernel  = kernel  ?? throw new ArgumentNullException(nameof(kernel));

            // ── Selection & inspector ─────────────────────────────────────────
            _selection = new SimHostSelectionManager();
            _inspector = new SimHostInspectorAdapter(_selection, repo);
            _fdpRepoAdapter = new FdpRepositoryAdapter(repo);

            // Task 47: register context menu handlers for the FDP entity inspector.
            _fdpEntityInspector.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
            {
                builder.AddItem("Center on entity", () => CenterCameraOnEntity(entity));
                builder.AddItem("Select entity", () =>
                {
                    _selection!.Set(entity);
                    _fdpInspectorState.SelectedEntity = entity;
                });
            }));

            // ── Scenario manager ──────────────────────────────────────────────
            _scenario = new SimHostScenarioManager(repo, road, trajectoryPool, formationTemplates);

            // ── UI ────────────────────────────────────────────────────────────
            _ui = new SimHostMainUI();

            // ── Entity query (vehicles) ───────────────────────────────────────
            _vehicleQuery = repo.Query().With<VehicleState>().With<VehicleParams>().Build();

            // ── Map canvas & layers ───────────────────────────────────────────
            _map       = new MapCanvas();
            _map.AddResource(trajectoryPool);

            _map.AddLayer(new SimHostRoadLayer(road));

            _visualizer = new SimHostVehicleVisualizer();
            _map.AddLayer(new EntityRenderLayer(
                "Vehicles", 0, repo, _vehicleQuery, _visualizer, _inspector));

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
            };

            _interactionTool.OnRegionSelected += entities => _selection.SetMultiple(entities);

            _interactionTool.OnWorldClick += (pos, btn, shift, ctrl, hitEntity) =>
            {
                if (btn != MouseButton.Right) return;
                var entities = new List<Fdp.Kernel.Entity>(_selection.SelectedEntities);
                if (entities.Count == 0) return;

                foreach (var e in entities)
                {
                    if (!repo.IsAlive(e)) continue;
                    if (shift)
                        _scenario!.AddWaypoint(e, pos, _ui!.UIState.InterpolationMode);
                    else
                        repo.Bus.Publish(new CmdNavigateToPoint
                        {
                            Entity        = e,
                            Destination   = pos,
                            ArrivalRadius = 3.0f,
                            Speed         = repo.GetComponentRO<VehicleParams>(e).MaxSpeedFwd * 0.8f,
                        });
                }
            };

            _map.SwitchTool(_interactionTool);

            // Seed a small initial scenario so the window isn't empty
            //_scenario.SpawnFastOne();

            _initialized = true;
        }

        /// <summary>Advances input, tool state, and roaming AI each frame.</summary>
        public void Update(float dt)
        {
            if (!_initialized || _repo == null || _map == null || _ui == null) return;

            // Delete selected entities with the Delete key.
            // Publish DestroyEntityCommand so NetworkSpawningSystem tears down
            // the network layer properly and IG removes the ghost entity.
            if (Raylib.IsKeyPressed(KeyboardKey.Delete) && _selection != null)
            {
                foreach (var e in new List<Fdp.Kernel.Entity>(_selection.SelectedEntities))
                {
                    if (!_repo.IsAlive(e)) continue;

                    if (_repo.HasComponent<NetworkIdentity>(e))
                    {
                        // Network-replicated entity — route through NetworkSpawningSystem
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
                        // Local-only entity — destroy directly.
                        _repo.DestroyEntity(e);
                    }
                }
                _selection.Clear();
            }

            // Time-scale forwarding
            if (_kernel != null)
                _kernel.GetTimeController().SetTimeScale(_ui.TimeScale);

            _scenario?.Update();
            _map.Update(dt);

            _fdpFrameCount++;
            _fdpEventBrowser.Update(_repo.Bus, _fdpFrameCount);
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
            if (!_initialized || _ui == null || _repo == null || _kernel == null) return;
            _ui.Render(_repo, _kernel, _scenario!, _inspector!);

            SimHostPanelColors.Push();
            _fdpEntityInspector.Draw(_fdpRepoAdapter!, _fdpInspectorState, "SimHost Entity Inspector");
            SimHostPanelColors.Pop();

            SimHostPanelColors.Push();
            _fdpEventBrowser.Draw("SimHost Event Browser");
            SimHostPanelColors.Pop();
        }

        public void Dispose()
        {
            // EntityQuery is managed by EntityRepository; no dispose needed.
            _initialized = false;
        }

        /// <summary>Centres the map camera on the entity's SimTransform position.</summary>
        private void CenterCameraOnEntity(Fdp.Kernel.Entity entity)
        {
            if (_repo == null || !_repo.IsAlive(entity)) return;
            if (!_repo.HasComponent<SimTransform>(entity)) return;
            ref readonly var tf = ref _repo.GetComponentRO<SimTransform>(entity);
            _map?.Camera.FocusOn(new Vector2(tf.Position.X, tf.Position.Y));
        }
    }
}
