using ImGuiNET;
using Bagira.IOS.Panels;

namespace Bagira.IOS;

/// <summary>
/// Application-shell orchestrator that wires <see cref="IosLogic"/> to the
/// five IOS UI panels and drives the per-frame update/draw cycle.
///
/// <para><b>Lifetime:</b> <see cref="IosMock"/> owns the
/// <see cref="IosLogic"/> instance and disposes it in <see cref="Dispose"/>.
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
public sealed class IosMock : IDisposable
{
    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly IosLogic         _logic;
    private readonly ConfigPanel      _configPanel;
    private readonly OrbatPanel       _orbatPanel;
    private readonly MissionPanel     _missionPanel;
    private readonly InteractionPanel _interactionPanel;
    private readonly SpawnerPanel     _spawnerPanel;
    private readonly InspectorPanel   _inspectorPanel;
    private readonly DiagnosticsPanel _diagnosticsPanel;

    private bool _disposed;

    /// <summary>Exposes the underlying logic for testing and diagnostics.</summary>
    public IosLogic Logic => _logic;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the orchestrator with the given logic and panel instances.
    /// </summary>
    public IosMock(
        IosLogic         logic,
        ConfigPanel      configPanel,
        OrbatPanel       orbatPanel,
        MissionPanel     missionPanel,
        InteractionPanel interactionPanel,
        SpawnerPanel     spawnerPanel,
        InspectorPanel?  inspectorPanel   = null,
        DiagnosticsPanel? diagnosticsPanel = null)
    {
        _logic            = logic            ?? throw new ArgumentNullException(nameof(logic));
        _configPanel      = configPanel      ?? throw new ArgumentNullException(nameof(configPanel));
        _orbatPanel       = orbatPanel       ?? throw new ArgumentNullException(nameof(orbatPanel));
        _missionPanel     = missionPanel     ?? throw new ArgumentNullException(nameof(missionPanel));
        _interactionPanel = interactionPanel ?? throw new ArgumentNullException(nameof(interactionPanel));
        _spawnerPanel     = spawnerPanel     ?? throw new ArgumentNullException(nameof(spawnerPanel));
        _inspectorPanel   = inspectorPanel   ?? new InspectorPanel();
        _diagnosticsPanel = diagnosticsPanel ?? new DiagnosticsPanel();
    }

    // ── Per-frame update ──────────────────────────────────────────────────────

    /// <summary>
    /// Drives one frame of logic.  Must be called from the main thread before
    /// <c>Raylib.BeginDrawing</c>.
    ///
    /// <para>Responsibilities:
    /// <list type="number">
    ///   <item>Call <see cref="IosLogic.Update"/> (polls DDS, processes events,
    ///   drains the interaction-log queue).</item>
    ///   <item>Mirror the current selection from <c>IosLogic</c> into the
    ///   <see cref="MissionPanel"/> so it always shows the right entity.</item>
    ///   <item>Forward any <see cref="IosLogic.SpawnerRequested"/> flag to the
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

        // 4. Notify the inspector panel of any selection change.
        //    Entity lookup is O(1); the panel skips work when the ID is unchanged.
        var selectedEntity = _logic.SelectedEntityId != PanelConstants.InspectorNoSelection
            ? _logic.Repo.GetEntity(_logic.SelectedEntityId)
            : null;
        _inspectorPanel.NotifySelectionChanged(selectedEntity);

        // 5. Advance the diagnostics event-rate sample window.
        _diagnosticsPanel.Update(dt);
    }

    // ── Per-frame render ──────────────────────────────────────────────────────

    /// <summary>
    /// Renders all IOS UI panels via ImGui.  Must be called inside an
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

        if (ImGui.BeginMainMenuBar())
        {
            ImGui.Text($"IOS Mock (Node {_logic.Repo?.LocalNodeId ?? 0})");
            if (ImGui.Button("EXIT")) Environment.Exit(0);
            ImGui.EndMainMenuBar();
        }

        ImGui.DockSpaceOverViewport(0, ImGui.GetMainViewport(), ImGuiDockNodeFlags.PassthruCentralNode);

        _configPanel.Draw(_logic);
        _orbatPanel.Draw(_logic);
        _missionPanel.Draw(_logic);
        _interactionPanel.Draw(_logic);
        _spawnerPanel.Draw(_logic);
        _inspectorPanel.Draw(_logic);
        _diagnosticsPanel.Draw(_logic);
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
            throw new ObjectDisposedException(nameof(IosMock));
    }
}
