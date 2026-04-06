using Hrot.NED.Descriptors;
using Hrot.ExCon.Adapters;
using Hrot.ExCon.Panels;
using Hrot.UI.Common.Panels;
using Hrot.UI.Common.Facades;
using FDP.Toolkit.ImGui.Panels;
using FDP.Toolkit.ImGui.Utils;
using ImGuiNET;

namespace Hrot.ExCon;

/// <summary>
/// Application-shell orchestrator that wires <see cref="ExConLogic"/> to the
/// five ExCon UI panels and drives the per-frame update/draw cycle.
///
/// <para><b>Lifetime:</b> <see cref="ExConMock"/> owns the
/// <see cref="ExConLogic"/> instance and disposes it in <see cref="Dispose"/>.
/// Panel instances are not owned here — they must outlive the mock or be
/// disposed by the caller.</para>
///
/// <para><b>Raylib integration:</b>
/// <see cref="Update"/> must be called once per frame before Raylib's
/// <c>BeginDrawing</c>.  <see cref="DrawUI"/> must be called inside an
/// <c>rlImGui.Begin / rlImGui.End</c> block.</para>
///
/// <para><b>Testing:</b> All Raylib/ImGui code lives in <see cref="DrawUI"/>
/// which is stubbed out (commented) for Phase P9.  Tests exercise only
/// <see cref="Update"/> and the injected <see cref="Logic"/> without
/// requiring a window or an active ImGui context.</para>
/// </summary>
public sealed class ExConMock : IDisposable
{
    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly ExConLogic                    _logic;
    private readonly ConfigPanel                 _configPanel;
    private readonly OrbatPanel                  _orbatPanel;
    private readonly MissionPanel                _missionPanel;
    private readonly InteractionPanel            _interactionPanel;
    private readonly SpawnerPanel                _spawnerPanel;
    private readonly DiagnosticsPanel            _diagnosticsPanel;
    private readonly DerEntityInspectorPanel     _derEntityInspectorPanel;
    private readonly bool                        _useDockSpace;

    // ── Phase 1 shims (kept for backward compat; superseded by Phase 6 adapters below) ──
    private readonly ExConMapPickShim            _mapPickShim;
    private readonly ExConMissionShim            _missionShim;

    // ── Phase 6 proper adapters ───────────────────────────────────────────────
    private readonly ExConMapConfigAdapter       _mapConfigAdapter;
    private readonly ExConOrbatAdapter           _orbatAdapter;
    private readonly SharedOrbatPanel            _sharedOrbatPanel;

    private bool _disposed;

    /// <summary>Exposes the underlying logic for testing and diagnostics.</summary>
    public ExConLogic Logic => _logic;

    // ── Panel accessors (used by RegisterWindows when running under Window Manager) ──

    public ConfigPanel       GetConfigPanel()      => _configPanel;
    public OrbatPanel        GetOrbatPanel()       => _orbatPanel;
    public MissionPanel      GetMissionPanel()     => _missionPanel;
    public InteractionPanel  GetInteractionPanel() => _interactionPanel;
    public SpawnerPanel      GetSpawnerPanel()     => _spawnerPanel;
    public DiagnosticsPanel  GetDiagnosticsPanel() => _diagnosticsPanel;

    // ── Adapter accessors (Phase 6 — used by RegisterWindows) ─────────────────────────

    public IMapConfigController  MapConfigAdapter => _mapConfigAdapter;
    public IMissionEditorService MissionShim      => _missionShim;
    public IMapPickService       MapPickShim       => _mapPickShim;
    /// <summary><see cref="ExConLogic"/> directly implements <see cref="ISpawnController"/>.</summary>
    public ISpawnController      SpawnController   => _logic;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the orchestrator with the given logic and panel instances.
    /// </summary>
    public ExConMock(
        ExConLogic                   logic,
        ConfigPanel                configPanel,
        OrbatPanel                 orbatPanel,
        MissionPanel               missionPanel,
        InteractionPanel           interactionPanel,
        SpawnerPanel               spawnerPanel,
        DiagnosticsPanel?          diagnosticsPanel        = null,
        DerEntityInspectorPanel?   derEntityInspectorPanel = null,
        bool                       useDockSpace            = true)
    {
        _logic                   = logic            ?? throw new ArgumentNullException(nameof(logic));
        _configPanel             = configPanel      ?? throw new ArgumentNullException(nameof(configPanel));
        _orbatPanel              = orbatPanel       ?? throw new ArgumentNullException(nameof(orbatPanel));
        _missionPanel            = missionPanel     ?? throw new ArgumentNullException(nameof(missionPanel));
        _interactionPanel        = interactionPanel ?? throw new ArgumentNullException(nameof(interactionPanel));
        _spawnerPanel            = spawnerPanel     ?? throw new ArgumentNullException(nameof(spawnerPanel));
        _diagnosticsPanel        = diagnosticsPanel ?? new DiagnosticsPanel();
        _derEntityInspectorPanel = derEntityInspectorPanel ?? new DerEntityInspectorPanel();
        _useDockSpace            = useDockSpace;

        // Phase 1 shims — still used for MissionPanel and MapPickService.
        _missionShim   = new ExConMissionShim(_logic.MissionEditorService);
        _mapPickShim   = new ExConMapPickShim(_logic.MapPickService);

        // Phase 6 proper adapters.
        _mapConfigAdapter = new ExConMapConfigAdapter(_logic);
        _orbatAdapter     = new ExConOrbatAdapter(_logic.Repo, _logic);
        _sharedOrbatPanel = new SharedOrbatPanel();

        // Register the Hrot-specific "Edit Overlay" context menu action.
        // The handler checks HasDescriptor/GetDescriptor at runtime; it is safe
        // to register unconditionally — the lambda is never invoked unless ImGui
        // is active and the user right-clicks an entity.
        _derEntityInspectorPanel.RegisterContextMenuHandler(
            new LambdaDerContextMenuHandler((entity, builder) =>
            {
                if (entity.HasDescriptor<MapVisualOverlay>())
                {
                    var overlay = entity.GetDescriptor<MapVisualOverlay>()!;
                    if (overlay.IsEditable)
                        builder.AddItem("Edit Overlay", () => _logic.StartEditingMode(entity.EntityId));
                }
            }));
    }

    // ── Per-frame update ──────────────────────────────────────────────────────

    /// <summary>
    /// Drives one frame of logic.  Must be called from the main thread before
    /// <c>Raylib.BeginDrawing</c>.
    ///
    /// <para>Responsibilities:
    /// <list type="number">
    ///   <item>Call <see cref="ExConLogic.Update"/> (polls DDS, processes events,
    ///   drains the interaction-log queue).</item>
    ///   <item>Mirror the current selection from <c>ExConLogic</c> into the
    ///   <see cref="MissionPanel"/> so it always shows the right entity.</item>
    ///   <item>Forward any <see cref="ExConLogic.SpawnerRequested"/> flag to the
    ///   spawner panel (consumed immediately).</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="dt">Frame delta time in seconds (from <c>Raylib.GetFrameTime</c>).</param>
    public void Update(float dt)
    {
        ThrowIfDisposed();

        // 1. Run network ingress, event processing, timeout checks.
        _logic.Update();

        // 2. Propagate selected entity to the mission panel.
        _missionPanel.SelectedEntityId = _logic.SelectedEntityId;

        // 3. If the ORBAT panel requested the spawner, bubble the flag.
        //    The spawner panel would bring itself to the foreground in DrawUI.
        //    We consume the flag here to avoid re-triggering every frame.
        if (_logic.SpawnerRequested)
            _logic.ConsumeSpawnerRequest();

        // 4. Advance the diagnostics event-rate sample window.
        _diagnosticsPanel.Update(dt);
    }

    // ── Per-frame render ──────────────────────────────────────────────────────

    private bool _panelsWindowManaged;

    /// <summary>
    /// Signals that all ExCon panels are hosted by the FDP Window Manager.
    /// After this call <see cref="DrawUI"/> skips panel draws and the standalone
    /// main menu bar so only the global alert modal remains (it cannot be a
    /// managed window because it is a modal popup, not a regular window).
    /// </summary>
    public void SetPanelsWindowManaged() => _panelsWindowManaged = true;

    /// <summary>
    /// Renders all ExCon UI panels via ImGui.  Must be called inside an
    /// <c>rlImGui.Begin / rlImGui.End</c> block on the main thread.
    ///
    /// <para>Actual ImGui calls are wired in Phase P9.  The method signature
    /// and call-delegation structure are in place so that adding the Raylib
    /// dependency and uncommenting the draw bodies is the only change
    /// required.</para>
    /// </summary>
    public void DrawUI()
    {
        ThrowIfDisposed();

        // Guard against headless/test environments where no ImGui context is active.
        if (ImGui.GetCurrentContext() == IntPtr.Zero) return;

        if (!_panelsWindowManaged)
        {
            if (ImGui.BeginMainMenuBar())
            {
                ImGui.Text($"ExCon Mock (Node {_logic.Repo?.LocalNodeId ?? 0})");
                if (ImGui.Button("EXIT")) Environment.Exit(0);
                ImGui.EndMainMenuBar();
            }

            if (_useDockSpace)
                ImGui.DockSpaceOverViewport(0, ImGui.GetMainViewport(), ImGuiDockNodeFlags.PassthruCentralNode);

            _configPanel.Draw(_mapConfigAdapter);
            _sharedOrbatPanel.DrawContent(_orbatAdapter, _orbatAdapter);
            _missionPanel.Draw(_missionShim, _mapPickShim);
            _interactionPanel.Draw(_logic);
            _spawnerPanel.Draw(_logic);
            _diagnosticsPanel.Draw(_logic);
            _derEntityInspectorPanel.Draw(_logic.Repo, "ExCon Entity Inspector");
        }

        // Two-ACK global alert modal: surface Phase-2 error ACKs to the operator.
        if (_logic.GlobalAlert != null)
        {
            ImGui.OpenPopup("Entity Error");
        }

        bool alertOpen = true;
        if (ImGui.BeginPopupModal("Entity Error", ref alertOpen,
                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove))
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                _logic.DismissAlert();
                ImGui.CloseCurrentPopup();
            }
            ImGui.TextUnformatted(_logic.GlobalAlert ?? string.Empty);
            ImGui.Spacing();
            if (ImGui.Button("OK", new System.Numerics.Vector2(80, 0)))
            {
                _logic.DismissAlert();
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _logic.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ExConMock));
    }
}
