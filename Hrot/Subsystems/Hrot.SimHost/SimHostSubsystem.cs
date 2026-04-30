using Hrot.Core.Mission;
using Hrot.Common;
using Hrot.Core.Network;
using Hrot.Map.Common;
using Hrot.SimHost;
using Hrot.SimHost.Utilities;
using Fdp.Presentation.Panels;
using Fdp.Presentation.Utils;
using Fdp.Toolkit.Replication.Services;
using Fdp.Core;
using Fdp.Toolkit.Vis2D.Components;
using System;
using System.Collections.Generic;
using System.Threading;
using Fdp.Toolkit.Runner;
using Hrot.SimHost.Windows;
using Hrot.Presentation.Windows;
using Hrot.Presentation.Facades;

using NetworkEntityMap = Fdp.Toolkit.Replication.Services.NetworkEntityMap;

namespace Hrot.SimHost
{
    /// <summary>
    /// Thin <see cref="ISubsystem"/> adapter that embeds the SimHost simulation engine.
    ///
    /// <para>All initialization, doctrine registration, ECS wiring, and network setup
    /// live in <see cref="SimHostApp"/>.  This class simply owns a <see cref="SimHostApp"/>
    /// instance, delegates the subsystem lifecycle to it, and adds the runner-specific
    /// background loop (<see cref="Start"/> / <see cref="Stop"/>).</para>
    ///
    /// <para>This follows the same "thin adapter" pattern as <see cref="IgSubsystem"/>:
    /// the core application class is the single source of truth for its own wiring.</para>
    /// </summary>
    public sealed class SimHostSubsystem : ISubsystem, IMapCameraProvider, IWindowRegistrar
    {
        // ── Subsystem identity ────────────────────────────────────────────────

        /// <inheritdoc/>
        public string Name => "SimHost";

        /// <inheritdoc/>
        /// <remarks>Dark red — distinct from IG (green) and ExCon (violet).</remarks>
        public System.Numerics.Vector4 TitleBarColor =>
            new System.Numerics.Vector4(0.40f, 0.08f, 0.08f, 1f);

        // ── Core application ──────────────────────────────────────────────────

        private readonly NodeRole _role = NodeRole.MuscleGround | NodeRole.Perception;
        private readonly INetworkFactory? _networkFactory;
        private SimHostApp? _app;
        private bool _headless;
        private int _nodeId;

        /// <summary>
        /// Initialises SimHost with the specified node role.
        /// </summary>
        /// <param name="role">
        ///   Node role to pass to <see cref="SimHostApp"/>.
        ///   Defaults to <c>MuscleGround | Perception</c>. SimHost is always the Muscle tier;
        ///   the Brain role belongs exclusively to CGF.
        /// </param>
        public SimHostSubsystem()
        {
        }

        /// <summary>
        /// Initialises SimHost with a protocol factory injected by the composition root.
        /// </summary>
        public SimHostSubsystem(INetworkFactory networkFactory)
        {
            _networkFactory = networkFactory;
        }

        /// <summary>
        /// Internal accessor used by unit tests to inspect the underlying
        /// <see cref="SimHostApp"/> (e.g. to access the kernel via reflection).
        /// </summary>
        internal SimHostApp App => _app ?? throw new InvalidOperationException("SimHostSubsystem is not initialized.");

        // ── Background loop (standalone mode) ────────────────────────────────

        private CancellationTokenSource? _cts;
        private Thread?                  _loopThread;

        // ── Public ECS access ─────────────────────────────────────────────────

        /// <summary>
        /// Provides access to the ECS <see cref="EntityRepository"/> after
        /// <see cref="Initialize"/> has been called.  Returns <see langword="null"/>
        /// when the subsystem has not yet been initialized.
        /// </summary>
        public EntityRepository? World => _app?.WorldOrNull;

        /// <inheritdoc/>
        public MapCameraView? GetCameraView() => _app?.GetMapCamera()?.GetCameraView();

        /// <inheritdoc/>
        public void ApplyCameraView(MapCameraView view) => _app?.GetMapCamera()?.ApplyCameraView(view);

        // Non-interface helper kept for backward-compat with tests.
        public MapCamera? GetMapCamera() => _app?.GetMapCamera();

        // ── TestHook delegates ────────────────────────────────────────────────

        /// <summary>TestHook: exposes the <see cref="NetworkEntityMap"/>.</summary>
        internal NetworkEntityMap TestHook_EntityMap => App.TestHook_EntityMap;

        /// <summary>
        /// TestHook: exposes the <see cref="DoctrineRegistry"/> so integration tests can
        /// register scenario-specific doctrines before transitioning the cluster to OperatingLive.
        /// </summary>
        internal Fdp.Toolkit.Behavior.DoctrineRegistry TestHook_DoctrineRegistry
            => App.TestHook_DoctrineRegistry;

        /// <summary>TestHook: spawns an entity and returns its network ID.</summary>
        internal long TestHook_SpawnEntity(long tkbType, GeoPoint position)
            => App.TestHook_SpawnEntity(tkbType, position);

        /// <summary>TestHook: current kernel simulation time in seconds.</summary>
        internal double TestHook_CurrentSimTime => App.TestHook_CurrentSimTime;

        /// <summary>
        /// TestHook: runtime type of the currently active time controller in the SimHost kernel.
        /// Used by integration tests to verify that controller type is SlaveSyncController.
        /// </summary>
        internal Type? TestHook_TimeControllerType => App.TestHook_TimeControllerType;

        /// <summary>
        /// TestHook: current <see cref="Fdp.ModuleHost.Time.TimeMode"/> of the SimHost
        /// kernel's time controller. Used to verify Pause → Deterministic → Resume → Continuous transitions.
        /// </summary>
        internal Fdp.ModuleHost.Time.TimeMode? TestHook_TimeControllerMode => App.TestHook_TimeControllerMode;

        /// <summary>TestHook: teleports entity to <paramref name="worldPos"/> (simulates IG drag).</summary>
        internal void TestHook_SimulateDrag(long networkId, System.Numerics.Vector2 worldPos)
            => App.TestHook_SimulateDrag(networkId, worldPos);

        /// <summary>TestHook: attaches a MoveTo NavigationIntent to the entity, triggering movement.</summary>
        internal void TestHook_SetMovementIntent(long networkId, System.Numerics.Vector2 destination, float speed = 15f)
            => App.TestHook_SetMovementIntent(networkId, destination, speed);

        /// <summary>TestHook: returns the current <see cref="SimTransform"/>, or default.</summary>
        internal SimTransform TestHook_GetSimTransform(long networkId)
            => App.TestHook_GetSimTransform(networkId);

        /// <summary>TestHook: returns child entities that reference the given parent.</summary>
        internal List<Entity> TestHook_GetChildEntities(Entity parentEntity)
            => App.TestHook_GetChildEntities(parentEntity);

        /// <summary>
        /// TestHook: registers a custom ECS system on the kernel after initialization.
        /// For use by in-process E2E test fixtures only.
        /// </summary>
        internal void TestHook_AddSystem(Fdp.ModuleHost.Abstractions.IEcsModuleSystem system)
            => App.TestHook_AddSystem(system);

        // ── ISubsystem ────────────────────────────────────────────────────────

        /// <summary>
        /// Creates the <see cref="SimHostApp"/> instance and calls
        /// <see cref="SimHostApp.InitializeEmbedded"/> — all ECS/DDS wiring happens there.
        /// </summary>
        public void Initialize(SubsystemConfig config)
        {
            _headless = config.Headless;
            int? domainOverride = config.DomainId;
            _nodeId = config.NodeId;
            _app = new SimHostApp(domainOverride, _role);
            _app.InitializeEmbedded(headless: config.Headless, domainIdOverride: domainOverride, nodeIdOverride: config.NodeId, networkFactory: _networkFactory);
        }

        /// <summary>
        /// Ticks the kernel and simulation-logic group by <paramref name="deltaTime"/> seconds.
        /// </summary>
        public void Update(float deltaTime)
        {
            _app?.Tick(deltaTime);
        }

        /// <summary>Renders the 2-D map canvas. No-op in headless mode.</summary>
        public void DrawWorld()
        {
            if (!_headless) _app?.DrawWorld();
        }

        /// <summary>Renders ImGui control panels. No-op in headless mode.</summary>
        public void DrawUI()
        {
            if (!_headless) _app?.DrawUI();
        }

        /// <inheritdoc/>
        public void RegisterWindows(Fdp.Presentation.WindowManager.WindowManager windowManager)
        {
            var vis = _app?.Visualization;
            if (vis == null) return;

            if (vis.UI != null)
            {
                windowManager.RegisterWindow(new SimHostControlsWindow(
                    vis.UI,
                    () => vis.GetRepo(),
                    () => vis.GetKernel(),
                    () => vis.GetScenario()));
            }

            windowManager.RegisterWindow(new FdpEntityInspectorWindow(
                "simhost_fdp_inspector", "SimHost Entity Inspector", "SimHost",
                vis.FdpEntityInspector,
                () => vis.GetFdpRepoAdapter(),
                () => vis.FdpInspectorState,
                SimHostWindowColor.TitleBar));

            // Wire component-editor reflector and "Inspect..." context menu.
            var simhostPickBridge = vis.GetMapPickBridge();
            FdpEntityInspectorHelper.WireInspectorWithInspectContextMenu(
                vis.FdpEntityInspector,
                windowManager,
                "SimHost",
                () => vis.GetFdpRepoAdapter(),
                simhostPickBridge,
                TitleBarColor);

            windowManager.RegisterWindow(new FdpEventBrowserWindow(
                "simhost_fdp_events", "SimHost Event Browser", "SimHost",
                vis.FdpEventBrowser,
                SimHostWindowColor.TitleBar));

            windowManager.RegisterWindow(new ArchitectureDiagnosticsWindow(
                "simhost_architecture_diagnostics", "SimHost Architecture Diagnostics", "SimHost",
                new Fdp.Presentation.Panels.ArchitectureDiagnosticsPanel(),
                () => _app?.Kernel,
                SimHostWindowColor.TitleBar));

            vis.SetPanelsWindowManaged();
        }

        /// <summary>Disposes all kernel resources.</summary>
        public void Shutdown()
        {
            Stop();
            _app?.Shutdown(ownsWindow: false);
            _app = null;
        }

        // ── Standalone helpers ────────────────────────────────────────────────

        /// <summary>
        /// Starts a background simulation thread (~60 Hz).
        /// Use this when running SimHost standalone (outside the orchestrator update loop).
        /// The orchestrator calls <see cref="Update"/> directly and does not use this method.
        /// </summary>
        public void Start()
        {
            if (_cts != null) return; // already running
            _cts        = new CancellationTokenSource();
            _loopThread = new Thread(() => RunLoop(_cts.Token))
            {
                IsBackground = true,
                Name         = "SimHost-Loop"
            };
            _loopThread.Start();
            Logger.Info($"[Node-{_nodeId}] Background loop started.");
        }

        /// <summary>
        /// Signals the background simulation thread to stop and waits for it to exit.
        /// Safe to call even when <see cref="Start"/> was never called.
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
            _loopThread?.Join(TimeSpan.FromSeconds(3));
            _cts?.Dispose();
            _cts        = null;
            _loopThread = null;
        }

        // ── Private ───────────────────────────────────────────────────────────

        private void RunLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                _app?.Tick(0f); // dt managed internally by time controller
                Thread.Sleep(1); // ~1 ms yield; time controller manages dt
            }
            Logger.Info($"[Node-{_nodeId}] Background loop exited.");
        }
    }}
