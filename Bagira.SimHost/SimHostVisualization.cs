using System;
using System.Collections.Generic;
using System.Numerics;
using CycloneDDS.Runtime;
using ImGuiNET;
using Raylib_cs;
using Fdp.Kernel;
using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
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
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;

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

        // ── Mission control (right-click navigate via doctrine) ───────────────
        private DdsWriter<MissionControlRequest>? _missionWriter;

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
            CarKinem.Formation.FormationTemplateManager formationTemplates,
            DdsWriter<MissionControlRequest> missionWriter)
        {
            _repo          = repo         ?? throw new ArgumentNullException(nameof(repo));
            _kernel        = kernel        ?? throw new ArgumentNullException(nameof(kernel));
            _missionWriter = missionWriter ?? throw new ArgumentNullException(nameof(missionWriter));

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

                var interp = _ui!.UIState.InterpolationMode;
                foreach (var e in entities)
                {
                    if (!repo.IsAlive(e)) continue;
                    HandleRightClickForEntity(
                        repo, e, pos, shift, interp,
                        (ent, p, i) => _scenario!.SetDestination(ent, p, i),
                        (ent, p, i) => _scenario!.AddWaypoint(ent, p, i),
                        _missionWriter);
                }
            };

            _map.SwitchTool(_interactionTool);

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
        ///         includes a <c>ReachedDestination</c> trigger so <c>MissionDirectorSystem</c>
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
            DdsWriter<MissionControlRequest>? missionWriter)
        {
            // Determine if the entity has an active (non-zero) doctrine.
            bool brainActive = repo.HasComponent<DoctrineState>(entity)
                && repo.GetComponent<DoctrineState>(entity).ActiveDoctrineHash != DoctrineIds.None;

            if (!brainActive)
            {
                // Brain-dead path: bypass the mission machinery and talk directly to the
                // muscle layer.  Restores the pre-CQRS-split behaviour for roamers, local
                // collision-test entities, and entities that have been brought into brain-dead
                // state by a completed or aborted mission.
                if (shift)
                    addWaypoint(entity, pos, interp);
                else
                    setDestination(entity, pos, interp);
                return;
            }

            // Brain-active path: route through the mission pipeline.
            // Shift is not yet supported for brain-active entities — behaves like plain click.
            if (!repo.HasComponent<NetworkIdentity>(entity)) return;

            ref readonly var netId = ref repo.GetComponentRO<NetworkIdentity>(entity);

            float speed = repo.HasComponent<VehicleParams>(entity)
                ? repo.GetComponent<VehicleParams>(entity).MaxSpeedFwd * 0.8f
                : 15f;

            var paramsJson = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{{\"X\":{0},\"Y\":{1},\"Speed\":{2},\"ArrivalRadius\":3.0}}",
                pos.X, pos.Y, speed);

            var taskId = Guid.NewGuid();
            var task = new MissionTask
            {
                TaskId          = taskId,
                ExecutingEngine = "CGFX",
                BehaviorId      = "MoveToLocation",
                BehaviorParams  = paramsJson,
                // ReachedDestination trigger is required so MissionDirectorSystem fires
                // task completion and the doctrine is cleared when the queue exhausts.
                Triggers        = new List<Bagira.BDC.SSTD.MissionTrigger>
                {
                    new Bagira.BDC.SSTD.MissionTrigger { Type = "ReachedDestination" },
                },
                State           = eTaskState.TASK_PLANNED,
            };

            var plan = new MissionPlan
            {
                ActiveTaskId = taskId,
                Tasks        = new List<MissionTask> { task },
            };

            missionWriter?.Write(new MissionControlRequest
            {
                RequestId      = Guid.NewGuid(),
                TargetEntityId = netId.Value,
                BaseVersion    = 0,
                Payload        = new MissionCommandUnion
                {
                    _d              = eMissionCommandType.CMD_REPLACE_MISSION,
                    FullMissionData = plan,
                },
            });
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

            // ── Perspective toggle toolbar — DB-MOD1-11 ────────────────────────
            if (_repo.HasSingleton<Components.ActivePerspective>())
            {
                var perspective = _repo.GetSingletonUnmanaged<Components.ActivePerspective>();
                string label = perspective.Current == Components.PerspectiveType.IG
                    ? "View: IG  (click → Sim)"
                    : "View: Sim (click → IG)";

                ImGui.SetNextWindowPos(new Vector2(10, 560), ImGuiCond.FirstUseEver);
                ImGui.SetNextWindowSize(new Vector2(220, 48), ImGuiCond.FirstUseEver);
                ImGui.Begin("##PerspectiveToggle",
                    ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
                    ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar);

                if (ImGui.Button(label))
                {
                    _repo.Bus.Publish(new Events.TogglePerspectiveEvent());
                    _repo.Bus.SwapBuffers();
                }

                ImGui.End();
            }

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
