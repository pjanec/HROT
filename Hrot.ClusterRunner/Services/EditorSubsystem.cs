using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Kernel;
using FDP.Framework.Runner;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Time.Controllers;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Components;
using FDP.Toolkit.Vis2D.Defaults;
using Hrot.CGF;
using Hrot.ClusterRunner.Windows;
using Hrot.Editor;
using Hrot.Editor.Adapters;
using Hrot.Editor.Modules;
using Hrot.Editor.UI;
using Hrot.Map.Common;
using Hrot.Orchestrator;
using Hrot.ScenarioEditor;
using Hrot.ScenarioEditor.Services;
using Hrot.SimHost;
using ModuleHost.Core;
using ModuleHost.Core.Abstractions;

namespace Hrot.ClusterRunner.Services
{
    /// <summary>
    /// <see cref="ISubsystem"/> implementation that embeds the standalone HROT Editor.
    ///
    /// <para>Lifecycle:
    /// <list type="number">
    ///   <item><see cref="Initialize"/> — builds the offline ECS composition root
    ///   (entities, kernel, logic packs, adapters, UI panels) without DDS.</item>
    ///   <item><see cref="Update"/> — steps the time controller and ticks the kernel.</item>
    ///   <item><see cref="DrawWorld"/> — renders the 2-D map canvas (skipped in headless).</item>
    ///   <item><see cref="DrawUI"/> — renders ImGui panels not registered as managed windows
    ///   (skipped in headless).</item>
    ///   <item><see cref="RegisterWindows"/> — registers editor panels with the Window Manager
    ///   so they participate in the shared docking layout.</item>
    ///   <item><see cref="Shutdown"/> — disposes the kernel and ECS world.</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class EditorSubsystem : ISubsystem, IMapCameraProvider, IWindowRegistrar
    {
        // ── Subsystem identity ────────────────────────────────────────────────

        /// <inheritdoc/>
        public string Name => "Editor";

        /// <inheritdoc/>
        /// <remarks>Slate blue — distinct from IG (green), SimHost (red) and ExCon (violet).</remarks>
        public Vector4 TitleBarColor => new(0.15f, 0.22f, 0.48f, 1f);

        // ── Core state ────────────────────────────────────────────────────────

        private EntityRepository?       _world;
        private ModuleHostKernel?       _kernel;
        private SteppingTimeController? _stepping;
        private IEditorLogic?           _editorLogic;
        private MapCanvas?              _canvas;
        private MapCamera?              _camera;
        private bool                    _headless;

        // ── UI panels ─────────────────────────────────────────────────────────

        private ScenarioBrowserPanel? _browserPanel;
        private EditorToolbarPanel?   _toolbarPanel;
        private EditorOrbatPanel?     _orbatPanel;

        // ── Internal test accessors ───────────────────────────────────────────

        /// <summary>Internal test hook: direct access to the ECS world.</summary>
        internal EntityRepository World =>
            _world ?? throw new InvalidOperationException("EditorSubsystem is not initialized.");

        /// <summary>Internal test hook: direct access to the kernel.</summary>
        internal ModuleHostKernel Kernel =>
            _kernel ?? throw new InvalidOperationException("EditorSubsystem is not initialized.");

        /// <summary>Internal test hook: direct access to the editor logic facade.</summary>
        internal IEditorLogic EditorLogic =>
            _editorLogic ?? throw new InvalidOperationException("EditorSubsystem is not initialized.");

        /// <inheritdoc/>
        public MapCamera? GetMapCamera() => _camera;

        // ── ISubsystem lifecycle ──────────────────────────────────────────────

        /// <inheritdoc/>
        public void Initialize(SubsystemConfig config)
        {
            _headless = config.Headless;

            // ── 1. ECS world ─────────────────────────────────────────────────
            _world = new EntityRepository();
            var accumulator = new EventAccumulator();
            _kernel = new ModuleHostKernel(_world, accumulator);

            // ── 2. Time controller (stepping — no DDS sync partner) ──────────
            _stepping = new SteppingTimeController(new GlobalTime { TimeScale = 1.0f });
            _kernel.SetTimeController(_stepping);

            // ── 3. Shared services ────────────────────────────────────────────
            var entityMap        = new NetworkEntityMap();
            var doctrineRegistry = new DoctrineRegistry();
            var clusterSlave     = new ClusterSlave(0, "Editor", _world.Bus);
            var fileService      = EditorBootstrap.CreateFileService();

            // ── 4. Module registration (offline — no translator packs) ────────
            var simHostCorePack  = new SimHostCoreLogicPack(entityMap);
            var cgfLogicPackInst = new CgfLogicPack(doctrineRegistry, entityMap);
            var orchPack         = new OrchestrationLogicPack(clusterSlave);
            var scenarioMod      = new ScenarioEditorModule(fileService);

            _kernel.RegisterModule(simHostCorePack);
            _kernel.RegisterModule(cgfLogicPackInst);
            _kernel.RegisterModule(orchPack);
            _kernel.RegisterModule(scenarioMod);

            // Must be registered before kernel.Initialize() so SimHostComponentRegistry
            // components (PassengerBuffer, TargetMemory, etc.) exist when systems are created.
            SimHostComponentRegistry.RegisterAll(_world);
            _kernel.RegisterModule(new EditorSystemsModule(_world));

            // ── 4b. Logic-pack list used by EditorApplication.SwitchToExternalAsync ──
            var logicPacks = new List<IEcsModule> { simHostCorePack, cgfLogicPackInst };

            // ── 5. Kernel initialization ──────────────────────────────────────
            _kernel.Initialize();

            // ── 6. Editor application (IEditorLogic facade) ──────────────────
            var app = new EditorApplication(fileService, _world.Bus, _world, _kernel, logicPacks);
            _editorLogic = app;

            // ── 7. Map canvas + camera (skipped in headless) ──────────────────
            if (!_headless)
            {
                _camera = new MapCamera();
                _canvas = new MapCanvas(new RaylibInputProvider());
                _canvas.Camera = _camera;
            }

            // ── 8. UI panels ─────────────────────────────────────────────────
            _browserPanel = new ScenarioBrowserPanel();
            _toolbarPanel = new EditorToolbarPanel();
            _orbatPanel   = new EditorOrbatPanel();
        }

        /// <inheritdoc/>
        public void Update(float deltaTime)
        {
            _stepping?.Step(deltaTime);
            _kernel?.Update();
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Renders the 2-D map canvas.
        /// Called inside <c>Raylib.BeginDrawing()</c> by the orchestrator.
        /// No-op in headless mode.
        /// </remarks>
        public void DrawWorld()
        {
            if (_headless) return;
            _canvas?.Draw();
        }

        /// <inheritdoc/>
        /// <remarks>
        /// After <see cref="RegisterWindows"/>, the main editor panels are rendered by
        /// the Window Manager.  This method is a no-op for the editor as all panels are
        /// registered as managed windows.
        /// </remarks>
        public void DrawUI()
        {
            // All editor panels are registered as managed windows in RegisterWindows().
            // Nothing extra to render here.
        }

        /// <inheritdoc/>
        public void RegisterWindows(FDP.Toolkit.ImGui.WindowManager.WindowManager windowManager)
        {
            if (_editorLogic == null) return;
            windowManager.RegisterWindow(new EditorToolbarWindow(_toolbarPanel!, _editorLogic));
            windowManager.RegisterWindow(new EditorBrowserWindow(_browserPanel!, _editorLogic));
            windowManager.RegisterWindow(new EditorOrbatWindow(_orbatPanel!, _editorLogic));
        }

        /// <inheritdoc/>
        public void Shutdown()
        {
            _kernel?.Dispose();
            _kernel = null;
            _world?.Dispose();
            _world = null;
            _editorLogic = null;
            _stepping = null;
            _canvas = null;
            _camera = null;
        }
    }
}
