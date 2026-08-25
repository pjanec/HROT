using Hrot.IG;
using Fdp.Presentation.Panels;
using Fdp.Presentation.Utils;
using Fdp.Toolkit.Runner;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.IG.Windows;
using Hrot.Presentation.Windows;
using Hrot.Presentation.Facades;
using System;

namespace Hrot.IG
{
    /// <summary>
    /// <see cref="ISubsystem"/> implementation that embeds the Image Generator (IG).
    ///
    /// <para>Lifecycle:
    /// <list type="number">
    ///   <item><see cref="Initialize"/> — creates <see cref="IgApplication"/> and calls
    ///   <see cref="IgApplication.InitializeEmbedded"/>; the orchestrator owns the window.</item>
    ///   <item><see cref="Update"/> — delegates to <see cref="IgApplication.Update"/>.</item>
    ///   <item><see cref="DrawWorld"/> — delegates to <see cref="IgApplication.DrawWorld"/>
    ///   (2-D map canvas + debug overlay, inside <c>Raylib.BeginDrawing</c>).</item>
    ///   <item><see cref="DrawUI"/> — delegates to <see cref="IgApplication.DrawUI"/>
    ///   (ImGui panels, inside <c>rlImGui.Begin</c>).</item>
    ///   <item><see cref="Shutdown"/> — releases IG resources without closing the window
    ///   (the orchestrator manages window lifetime).</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class IgSubsystem : ISubsystem, IMapCameraProvider, IWindowRegistrar, Hrot.Common.Diagnostics.Gizmos.IGizmoControllable,
        Hrot.Presentation.DebugApi.IProvidesDebugSurface
    {
        /// <inheritdoc/>
        public string Name => "IG";

        /// <summary>
        /// ⭐⭐ <b><c>Q54</c> — IG's debug surface: a world to READ, and NO drive facade.</b>
        /// 📄 <c>Architect_Question_54</c> Q54-2 + charter <c>D3</c>.
        ///
        /// <para>⚠⚠ <b><c>drive: null</c> is MEASURED, not an oversight.</b> 📐 `2026-08-24`: a repo-wide grep
        /// for <c>new ClusterTimeTransportAdapter</c> finds it in <b>CGF and SimHost only</b> — IG builds
        /// none. ⇒ ⭐ the manifest reports <c>time.drive</c> ABSENT for the IG perspective and a step issued
        /// there answers <c>NOT_SUPPORTED_HERE</c>, which is exactly what <c>D4</c> asks for: absence that is
        /// declared and assertable. ⛔ Fabricating an adapter here would invent a control path the operator's
        /// UI does not have.</para>
        ///
        /// <para>⭐ IG is still a full ACK participant in the roster — 📌 <c>PARTICIPATE ≠ OBSERVE</c>: it
        /// executes the master's ticks, it just does not ISSUE them.</para>
        /// </summary>
        public Hrot.Presentation.DebugApi.ISubsystemDebugProvider? CreateDebugProvider()
            => new Hrot.Presentation.DebugApi.SubsystemDebugProvider(
                subsystemName: Name,
                perspective:   "IG",
                world:         () => _app?.World,
                entityMap:     null,
                drive:         null,
                // ⭐⭐ HN-029 — IG cannot DRIVE time (no facade) but it CAN request a cluster transition; see
                //    IgApplication.OrchestrationBus.
                requestTransition: Hrot.Presentation.DebugApi.SubsystemDebugProvider
                                       .TransitionsVia(() => _app?.OrchestrationBus),
                // ⭐⭐ MD-002 — IG's own kernel snapshot (it already builds one for its window, line ~169).
                architecture:  () => _app?.Kernel is null
                                     ? null
                                     : new Fdp.ModuleHost.Diagnostics.ArchitectureDiagnosticsService(
                                           () => _app?.Kernel));

        /// <inheritdoc/>
        /// <remarks>Forest green — distinct from SimHost (red) and ExCon (violet).</remarks>
        public System.Numerics.Vector4 TitleBarColor =>
            new System.Numerics.Vector4(0.08f, 0.40f, 0.08f, 1f);

        private IgApplication? _app;
        private bool _headless;
        private readonly Hrot.Core.Network.INetworkFactory? _networkFactory;

        /// <summary>Creates IgSubsystem without a network factory (legacy / headless path).</summary>
        public IgSubsystem() { }

        /// <summary>Creates IgSubsystem with an injected protocol factory from the composition root.</summary>
        public IgSubsystem(Hrot.Core.Network.INetworkFactory networkFactory)
        {
            _networkFactory = networkFactory;
        }

        /// <summary>
        /// Internal test hook for integration tests.
        /// </summary>
        internal IgApplication App => _app ?? throw new InvalidOperationException("Not initialized");

        /// <inheritdoc/>
        public MapCameraView? GetCameraView() => _app?.GetMapCamera()?.GetCameraView();

        /// <inheritdoc/>
        public void ApplyCameraView(MapCameraView view) => _app?.GetMapCamera()?.ApplyCameraView(view);

        // Non-interface helper kept for backward-compat with tests.
        public MapCamera? GetMapCamera() => _app?.GetMapCamera();

        // GZH-014: expose the gizmo controller for perspective-aware listener switching.
        public Fdp.Toolkit.Diagnostics.Gizmos.GizmoExecutionController? GizmoController => _app?.GizmoController;

        /// <inheritdoc/>
        public void Initialize(SubsystemConfig config)
        {
            _headless = config.Headless;
            _app = new IgApplication();
            // Orchestrator owns the Raylib window; IG sets up ECS + DDS only.
            int? domainOverride = config.DomainId;
            _app.InitializeEmbedded(
                headless: config.Headless,
                domainIdOverride: domainOverride,
                nodeIdOverride: config.NodeId,
                networkFactory: _networkFactory);
            // GZH-016: store active-map-owner predicate injected by SubsystemOrchestrator.
            _app.IsActiveMapOwner = config.IsActiveMapOwner;
        }

        /// <inheritdoc/>
        public void Update(float deltaTime)
        {
            _app?.Update(deltaTime);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Renders the 2-D map canvas and debug text overlay.
        /// Called inside <c>Raylib.BeginDrawing()</c> by the orchestrator.
        /// No-op in headless mode.
        /// </remarks>
        public void DrawWorld()
        {
            if (!_headless)
                _app?.DrawWorld();
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Registers IG ImGui panels as <c>ManagedWindow</c> instances with the
        /// application Window Manager.  After this call the panels are owned by
        /// the Window Manager and <see cref="DrawUI"/> only handles popup windows
        /// that cannot be wrapped as managed windows (context menus, vertex menus).
        /// </remarks>
        public void RegisterWindows(Fdp.Presentation.WindowManager.WindowManager windowManager)
        {
            if (_app == null) return;
            windowManager.RegisterWindow(new IgDebugWindow(_app.DebugPanel));
            windowManager.RegisterWindow(new IgEntityPropertiesWindow(_app.EntityPropertiesPanel));
            windowManager.RegisterWindow(new IgWaypointEditorWindow(_app.WaypointEditorPanel));
            windowManager.RegisterWindow(new IgMiniExConWindow(_app.MiniExConPanel));
            windowManager.RegisterWindow(new IgPerformanceWindow(_app.PerformanceOverlay));
            windowManager.RegisterWindow(new FdpEntityInspectorWindow(
                "ig_fdp_inspector", "IG Entity Inspector", "IG",
                _app.FdpEntityInspector,
                () => _app.GetFdpRepoAdapter(),
                () => _app.FdpInspectorState,
                IgWindowColor.TitleBar));

            // Wire component-editor reflector and "Inspect..." context menu.
            var igPickBridge = _app.GetMapPickBridge();
            FdpEntityInspectorHelper.WireInspectorWithInspectContextMenu(
                _app.FdpEntityInspector,
                windowManager,
                "IG",
                () => _app.GetFdpRepoAdapter(),
                igPickBridge,
                TitleBarColor);
            windowManager.RegisterWindow(new FdpEventBrowserWindow(
                "ig_fdp_events", "IG Event Browser", "IG",
                _app.FdpEventBrowser,
                IgWindowColor.TitleBar));
            windowManager.RegisterWindow(new ArchitectureDiagnosticsWindow(
                "ig_architecture_diagnostics", "IG Architecture Diagnostics", "IG",
                new Fdp.Presentation.Panels.ArchitectureDiagnosticsPanel(
                    new Fdp.ModuleHost.Diagnostics.ArchitectureDiagnosticsService(() => _app.Kernel)),
                IgWindowColor.TitleBar));
            // BP-327 — global window: the module/system execution-stats profiler.
            windowManager.RegisterWindow(new SystemProfilerWindow(
                "ig_system_profiler", "IG System Profiler", "IG",
                () => _app.Kernel?.GetExecutionStats(),
                IgWindowColor.TitleBar));
            // Signal IgApplication that these panels must not be double-rendered.
            _app.SetPanelsWindowManaged();
        }

        /// <inheritdoc/>
        /// <remarks>
        /// After <see cref="RegisterWindows"/>, the main IG panels are rendered by
        /// the Window Manager. This method only handles ImGui popups that cannot be
        /// wrapped as managed windows (context menus, vertex context menus).
        /// </remarks>
        public void DrawUI()
        {
            if (!_headless)
                _app?.DrawUI();
        }

        /// <inheritdoc/>
        public void Shutdown()
        {
            // ownsWindow=false: orchestrator manages Raylib window/ImGui teardown.
            _app?.Shutdown(ownsWindow: false);
            _app = null;
        }
    }
}
