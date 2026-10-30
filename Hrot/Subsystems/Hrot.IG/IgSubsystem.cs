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
        Hrot.Common.EntityCreation.IEntityCreationHost,
        Hrot.Presentation.DebugApi.IProvidesDebugSurface,
        Hrot.UI.Common.RunMode.IProvidesRunControls
    {
        /// <inheritdoc/>
        public string Name => "IG";

        /// <summary>
        /// ⭐⭐ <b><c>Q54</c> — IG's debug surface: a world to READ, and NO drive facade.</b>
        /// 📄 <c>Architect_Question_54</c> Q54-2 + charter <c>D3</c>.
        ///
        /// <para>⚠ <b>CORRECTED <c>2026-09-03</c> (<c>CE-162</c>):</b> this paragraph used to justify BOTH
        /// <c>drive: null</c> AND <c>entityMap: null</c> as measured absences. ⛔ Only the TIME half was
        /// true — IG holds a <c>NetworkEntityMap</c> and now passes it. See the note at the argument.</para>
        ///
        /// <para>⚠⚠ <b><c>drive: null</c> is MEASURED, not an oversight.</b> 📐 `2026-08-24`: a repo-wide grep
        /// for <c>new ClusterTimeTransportAdapter</c> finds it in <b>CGF and SimHost only</b> — IG builds
        /// none. ⇒ ⭐ the manifest reports <c>time.drive</c> ABSENT for the IG perspective and a step issued
        /// there answers <c>NOT_SUPPORTED_HERE</c>, which is exactly what <c>D4</c> asks for: absence that is
        /// declared and assertable. ⛔ Fabricating an adapter here would invent a control path the operator's
        /// UI does not have.</para>
        ///
        /// <para>📐 <b>CE-1445 (R-1044) update:</b> IG now holds a <c>ClusterTimeTransportAdapter</c> too — built by <c>NodeRunControls.For</c> with the Player profile for the GATE / waiting screen only
        /// (<c>IProvidesRunControls</c>, below). ⛔ It is still NOT offered as a drive: this debug surface keeps <c>drive: null</c>, and the profile suppresses the toolbar time control.</para>
        ///
        /// <para>⭐ IG is still a full ACK participant in the roster — 📌 <c>PARTICIPATE ≠ OBSERVE</c>: it
        /// executes the master's ticks, it just does not ISSUE them.</para>
        /// </summary>
        public Hrot.Presentation.DebugApi.ISubsystemDebugProvider? CreateDebugProvider()
            // ⭐⭐⭐ the ONE ECS-node debug surface (EcsNodeDebugProvider) — IG adds its map feed; ⛔ no drive: IG cannot issue ticks
            //    (it executes the master's — PARTICIPATE ≠ OBSERVE), so stepping from its perspective answers NOT_SUPPORTED_HERE.
            => Hrot.Presentation.DebugApi.EcsNodeDebugProvider.For(Name, "IG", () => _app?.NodeContext, new Hrot.Presentation.DebugApi.EcsNodeDebugExtras
            {
                GizmoBuffer = () => _app?.GizmoBuffer,
            });

        /// <inheritdoc/>
        /// <remarks>
        /// Forest green — distinct from SimHost (red) and ExCon (violet).
        /// ⭐⭐⭐ <c>CE-083</c> (user ruling, <c>2026-08-27</c>: <i>"each subsystem still needs its own
        /// different titlebar color, for each its window"</i>) — this RETURNS
        /// <c>IgWindowColor.TitleBar</c> rather than repeating a literal. 📐 It used to be its own
        /// <c>(0.08,0.40,0.08)</c> while every IG WINDOW used <c>(0.07,0.30,0.07)</c>, so the spawned
        /// "Inspect…" watch windows were a different shade from the windows they came from. ⇒ ⭐ one
        /// value per subsystem, applied to each of its windows, and now true BY CONSTRUCTION — ⛔ there
        /// is no second literal left to drift.
        /// </remarks>
        public System.Numerics.Vector4 TitleBarColor => Hrot.IG.Windows.IgWindowColor.TitleBar;

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

        /// <summary>
        /// ⭐⭐⭐ <c>CE-515</c> (was <c>CE-271</c> seam ⑤, IG-only) — this node's entity-creation pack. The debug API's
        /// <c>POST /entities/create-request</c> resolves the ACTIVE perspective's pack through
        /// <see cref="Hrot.Common.EntityCreation.IEntityCreationHost"/>, the same on every node. Null until initialised.
        /// </summary>
        public Hrot.Common.EntityCreation.EntityCreation? EntityCreation => _app?.EntityCreation;

        /// <inheritdoc/>
        public MapCameraView? GetCameraView() => _app?.GetMapCamera()?.GetCameraView();

        /// <inheritdoc/>
        public void ApplyCameraView(MapCameraView view) => _app?.GetMapCamera()?.ApplyCameraView(view);

        // Non-interface helper kept for backward-compat with tests.
        public MapCamera? GetMapCamera() => _app?.GetMapCamera();

        // GZH-014: expose the gizmo controller for perspective-aware listener switching.
        public Fdp.Toolkit.Diagnostics.Gizmos.GizmoExecutionController? GizmoController => _app?.GizmoController;

        // ⭐⭐⭐ CE-1445 (R-1044, DESIGN_Cluster_Load_Phase §11.8) — the IG OFFERS its run controls like every ECS host: the Player gate + the waiting screen (the composer draws it over this choice).
        //   ⛔ Not a drive: `CreateDebugProvider` above still has none (Q54/D4 — the IG issues no ticks); the toolbar time control is suppressed by the role's profile.
        Hrot.UI.Common.RunMode.RunHostKind Hrot.UI.Common.RunMode.IProvidesRunControls.HostKind => Hrot.UI.Common.RunMode.RunHostKind.Ig;
        Hrot.UI.Common.Facades.ITimeTransportFacade? Hrot.UI.Common.RunMode.IProvidesRunControls.Transport => _app?.RunControls;
        Hrot.UI.Common.RunMode.RunModeChoice? Hrot.UI.Common.RunMode.IProvidesRunControls.RunChoice => _app?.RunControls?.RunChoice;
        bool Hrot.UI.Common.RunMode.IProvidesRunControls.SuppressTimeControl => true;

        /// <summary>⭐ CE-1446 (R-1046) — the IG's 3-D map build as the load chain polls it (<c>IsBuilt</c> per load, <c>Failure</c>, <c>Step</c>); null before the IG is initialised. The one place the node exposes it.</summary>
        public Fdp.Toolkit.Orchestration.IMapBuildStatus? MapBuildStatus => _app?.MapBuildStatus;

        /// <summary>⭐ CE-1445 — the IG's run choice, for the wiring rail.</summary>
        internal Hrot.UI.Common.RunMode.RunModeChoice? RunChoiceForTest => _app?.RunControls?.RunChoice;

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
            // ⭐ CE-1289 (+ CE-3151) — the point under the map cursor, flat and lat/long/alt, on the 2-D and the 3-D map.
            Hrot.UI.Common.Panels.MapCursorStatusBarSection.Register(windowManager.StatusBar, () => _app?.Canvas, "IG");
            windowManager.RegisterWindow(new Hrot.Presentation.Windows.WaypointEditorWindow(   // ⭐ CE-1096 — the shared window
                _app.WaypointEditorPanel, Hrot.Presentation.Windows.ScenarioPanelWindowIds.IgWaypoint, "IG", IgWindowColor.TitleBar));
            windowManager.RegisterWindow(new IgMiniExConWindow(_app.MiniExConPanel));
            _app.RegisterMainMenu(windowManager.GlobalMenu, "IG");   // ⭐ CE-1243 MB-4 — the map's menus, in the one bar
            windowManager.RegisterWindow(new IgPerformanceWindow(_app.PerformanceOverlay));
            // ⭐⭐⭐ PHASE 2 SLICE ② — the FIVE diagnostics sites this host used to spell out by hand are
            //    now ONE shared bundle, `Hrot.Presentation.Windows.DiagnosticsWindowsBundle`. 📐 The same
            //    five were copy-pasted across FOUR hosts (IG, SimHost, CGF, Editor) = 20 sites; the ids
            //    and titles are DERIVED from `IdPrefix`/`TitlePrefix`, so they cannot drift apart again.
            // ⭐ This is also this host's FIRST `UiBundleHost.Compose` call — the phase-1 seam's real
            //   adoption. ⛔ A throwing bundle is NAMED, never swallowed.
            // ⭐⭐ CE-083 (user ruling) — ONE colour per subsystem, applied to each of its windows.
            //   📐 This host used to pass a SECOND shade to the "Inspect…" helper; its `TitleBarColor`
            //   property now RETURNS the window constant, so there is one value and no way to drift.
            // 📄 docs/DESIGN_Subsystem_Composition_Unification.md §5c.7.
            Fdp.Toolkit.Runner.UiBundleHost.Compose(
                new Fdp.Toolkit.Runner.IUiBundle[]
                {
                    new DiagnosticsWindowsBundle(new DiagnosticsHostServices(
                        IdPrefix:       "ig_",
                        TitlePrefix:    "IG",
                        Perspective:    "IG",
                        Inspector:      _app.FdpEntityInspector,
                        RepoAdapter:    () => _app.GetFdpRepoAdapter(),
                        InspectorState: () => _app.FdpInspectorState,
                        EventBrowser:   _app.FdpEventBrowser,
                        TitleBarColor:  IgWindowColor.TitleBar,
                        Host:           () => _app.HostActions,   // ⭐ CE-1150 — the inspector's map menu requests on the host's actions
                        World:          () => _app.World,
                        // ⭐ This host builds its OWN architecture service, exactly as before — the
                        //   bundle takes the finished panel, so the lazy `() => _app.Kernel` binding
                        //   this host has always used is untouched (design §5c.7 F2).
                        ArchitecturePanel: new Fdp.Presentation.Panels.ArchitectureDiagnosticsPanel(
                            new Fdp.ModuleHost.Diagnostics.ArchitectureDiagnosticsService(() => _app.Kernel)),
                        // BP-327 — the module/system execution-stats profiler.
                        ExecutionStats: () => _app.Kernel?.GetExecutionStats(),
                        // ⭐ CE-083 — no second colour: TitleBarColor IS IgWindowColor.TitleBar now.
                        PickBroker:     _app.GetMapPickBroker())),
                    // ⭐⭐⭐ CE-1150 (canvas-context-menu-design §12 HA-4) — THE Details panel, the same shared one every ECS host builds:
                    //   the Components view, and Properties… / View ▸ Details on this host's actions. 📐 Before: none on the IG.
                    new Hrot.Editor.AiShared.Windows.EntityDetailsBundle(new Hrot.Editor.AiShared.Windows.EntityDetailsHostServices(
                        WindowId:    "ig_details",
                        Perspective: "IG",
                        World:       () => _app.World,
                        Inspector:   () => _app.FdpEntityInspector,
                        Session:     () => _app.GetFdpRepoAdapter(),
                        RunState:    Hrot.Editor.AiShared.Variables.RunStateSource.For(
                                         () => _app.World != null,
                                         () => Fdp.Toolkit.Time.SimClock.Of(_app.World).IsHalted),
                        Host:        () => _app.HostActions)),
                },
                new Fdp.Toolkit.Runner.UiBundleContext(windowManager));
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
