using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.WindowManager;
using ImGuiNET;

namespace Hrot.Editor.AiShared.Comparison.UI;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — the whole of what <see cref="ComparisonSummaryPanel"/> shows, this frame.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example. ⭐ Mirrors <see cref="ComparisonSummaryPanelState"/>
/// field for field, but dumpable — the state class wraps a live <see cref="ComparisonSessionState"/> and
/// exposes a mutation method (<c>ToggleSeverity</c>), so it cannot itself be the snapshot's model.
/// </summary>
public sealed record ComparisonSummaryPanelViewModel(
    string PanelId,
    string PanelKind,
    bool HasSession,
    string AssetName,
    bool HasMigrationNotice,
    string? MigrationNotice,
    string? TopSummary,
    string? HumanSummary,
    IReadOnlyList<string> EnabledSeverities,
    IReadOnlyList<string> AllSeverities) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

// ---- State model ------------------------------------------------------------

/// <summary>
/// View-model for <see cref="ComparisonSummaryPanel"/>.
/// Wraps a <see cref="ComparisonSessionState"/> and exposes the data the panel needs.
/// See design section 6.3.
/// </summary>
public sealed class ComparisonSummaryPanelState
{
    private readonly ComparisonSessionState _session;

    /// <summary>Display name of the asset being compared.</summary>
    public string AssetName { get; }

    /// <summary>True when the session has a non-null migration notice.</summary>
    public bool HasMigrationNotice => _session.MigrationNotice != null;

    /// <summary>The migration notice text, or null when not present.</summary>
    public string? MigrationNotice => _session.MigrationNotice;

    /// <summary>One-sentence top-level summary from the LLM response.</summary>
    public string TopSummary => _session.Response.TopLevelSummary;

    /// <summary>Full human-readable prose summary from the LLM response.</summary>
    public string? HumanSummary => _session.Response.HumanSummary;

    /// <summary>Currently enabled severity filter set.</summary>
    public IReadOnlySet<string> EnabledSeverities => _session.EnabledSeverities;

    public ComparisonSummaryPanelState(ComparisonSessionState session, string assetName)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        AssetName = assetName ?? throw new ArgumentNullException(nameof(assetName));
    }

    /// <summary>Toggles the severity in the underlying session state.</summary>
    public void ToggleSeverity(string severity) => _session.ToggleSeverity(severity);
}

// ---- Panel ------------------------------------------------------------------

/// <summary>
/// Docked panel that shows the top-level comparison summary and severity filter controls.
/// See design section 6.3.
/// </summary>
public sealed class ComparisonSummaryPanel : ManagedWindow
{
    /// <summary>⭐ <c>U-obs-5</c> — THE KIND. ⛔ Single-host: stays a local literal.</summary>
    internal const string Kind = "comparison-summary";

    private static readonly Vector4 YellowColor = new(1f, 0.9f, 0.2f, 1f);

    private static readonly string[] Severities =
        { "cosmetic", "tuning", "feature", "removal", "behavior" };

    private readonly ComparisonSessionRegistry _registry;
    private readonly Selection.EditorSelectionStore? _store;
    private Guid _activeAssetId;
    private string _activeAssetName = "";

    /// <param name="registry">The shared, asset-id-keyed comparison session registry.</param>
    /// <param name="store">
    /// ⭐⭐⭐ <b><c>CE-071</c> `B3` — WHERE THE ACTIVE ASSET COMES FROM.</b>
    /// 📄 <c>docs/DESIGN_Comparison_Ui_Mounting.md</c> §7 `B3`.
    /// <para>⛔⛔ Before <c>CE-071</c> the only way to tell this panel which asset it was summarising was
    /// <see cref="SetActiveAsset"/>, and 📐 <b>nothing in production called it</b> — so a registered panel
    /// would have rendered <c>HasSession: false</c> forever, indistinguishable from *"no comparison
    /// running"*. ⭐⭐ Reading <c>store.ActiveAsset</c> instead is the ESTABLISHED pattern, not new
    /// machinery: <c>BlackboardAuthoringWindow:576</c> already resolves its comparison session exactly this
    /// way, and <see cref="Identity.IEditableAsset"/> exposes precisely the <c>AssetId</c> + <c>Name</c>
    /// this panel needs.</para>
    /// <para>⚠ Optional: <see langword="null"/> leaves the panel on the explicit
    /// <see cref="SetActiveAsset"/> path, which is what the unit rails use.</para>
    /// </param>
    /// <param name="idOverride">
    /// ⭐⭐ <b><c>CE-071</c> `B2`</b> — per-perspective window id. ⛔ Without it three per-perspective
    /// instances would share one <c>Id</c> AND declare the same panel id to
    /// <see cref="PanelSnapshot"/> three times. ⚠ Mirrors <c>TraceTimelineWindow</c>'s
    /// <c>ai_trace_timeline_{suffix}</c>.
    /// </param>
    /// <param name="owningPerspective">
    /// ⭐⭐⭐ <b><c>CE-071</c> `B1` — the bug this parameter exists to fix.</b> 📐 This panel used to hard-code
    /// <c>"Analysis"</c>, and <c>grep</c> found that string in production ONLY here and in
    /// <see cref="ComparisonSidebar"/> ⇒ ⛔⛔ it was <see cref="WindowScope.PerspectiveBound"/> to a
    /// perspective <b>nothing registers</b>, so even correctly registered it could never be shown.
    /// 📌 The second instance of the hazard <c>SharedAiEditorServiceCollectionExtensions</c> already
    /// documents for <c>"Authoring"</c>.
    /// </param>
    public ComparisonSummaryPanel(
        ComparisonSessionRegistry registry,
        Selection.EditorSelectionStore? store = null,
        string? idOverride = null,
        string owningPerspective = "Analysis")
        : base(idOverride ?? "ai_comparison_summary", "Comparison Summary",
               owningPerspective, WindowScope.PerspectiveBound)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _store    = store;

        // ⭐⭐⭐ U-obs-5 — DECLARED AT CONSTRUCTION, ALWAYS, ungated on CaptureEnabled.
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>
    /// Sets which asset this panel is summarising.
    /// <para>⚠ <c>CE-071</c>: superseded in production by the <c>EditorSelectionStore</c> passed to the
    /// constructor — ⛔ this remains for hosts with no selection store, and as the unit rails' seam.
    /// An explicit call WINS for the rest of the frame; the store is re-read on the next build.</para>
    /// </summary>
    public void SetActiveAsset(Guid assetId, string assetName)
    {
        _activeAssetId   = assetId;
        _activeAssetName = assetName;
    }

    /// <summary>
    /// ⭐⭐ Resolves the asset to summarise: the selection store when one was supplied, else whatever
    /// <see cref="SetActiveAsset"/> last set.
    /// <para>⚠ Reads the store EVERY build rather than subscribing — the same choice
    /// <c>BlackboardAuthoringWindow</c> makes, and it keeps the panel correct across perspective switches
    /// with no event plumbing to leak.</para>
    /// </summary>
    private (Guid Id, string Name) ResolveActiveAsset()
    {
        var active = _store?.ActiveAsset;
        return active is not null
            ? (active.AssetId, active.Name)
            : (_activeAssetId, _activeAssetName);
    }

    /// <summary>
    /// ⭐⭐⭐ BUILD · CAPTURE. ⛔⛔ No ImGui — reads the session state the same way the old draw did,
    /// published before any render call. ⚠ Returns the wrapped <see cref="ComparisonSummaryPanelState"/>
    /// too, since <c>ToggleSeverity</c> (a mutation, not a display value) still needs it.
    /// </summary>
    private (ComparisonSummaryPanelViewModel Vm, ComparisonSummaryPanelState? State) BuildAndPublish()
    {
        var (assetId, assetName) = ResolveActiveAsset();
        var session = _registry.GetSession(assetId);
        ComparisonSummaryPanelViewModel vm;
        ComparisonSummaryPanelState? state = null;

        if (session == null)
        {
            vm = new ComparisonSummaryPanelViewModel(
                Id, Kind, HasSession: false, assetName,
                HasMigrationNotice: false, MigrationNotice: null,
                TopSummary: null, HumanSummary: null,
                EnabledSeverities: Array.Empty<string>(), AllSeverities: Severities);
        }
        else
        {
            state = new ComparisonSummaryPanelState(session, assetName);
            vm = new ComparisonSummaryPanelViewModel(
                Id, Kind, HasSession: true, state.AssetName,
                state.HasMigrationNotice, state.MigrationNotice,
                state.TopSummary, state.HumanSummary,
                state.EnabledSeverities.ToList(), Severities);
        }

        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return (vm, state);
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion, callable with no live ImGui context.</summary>
    internal ComparisonSummaryPanelViewModel SimulateDrawClientArea() => BuildAndPublish().Vm;

    protected override void DrawClientArea()
    {
        var (vm, state) = BuildAndPublish();

        if (!vm.HasSession)
        {
            ImGui.TextDisabled("No comparison active.");
            return;
        }

        // Asset title.
        ImGui.Text(vm.AssetName);

        // Migration notice.
        if (vm.HasMigrationNotice)
            ImGui.TextColored(YellowColor, "Migration: " + vm.MigrationNotice);

        // One-sentence summary.
        ImGui.TextWrapped(vm.TopSummary);

        ImGui.Separator();

        // Full prose (scrollable).
        if (vm.HumanSummary != null)
            ImGui.TextWrapped(vm.HumanSummary);

        ImGui.Separator();

        // Severity filter checkboxes.
        foreach (var sev in vm.AllSeverities)
        {
            bool enabled = vm.EnabledSeverities.Contains(sev);
            if (ImGui.Checkbox(sev, ref enabled))
                state!.ToggleSeverity(sev);
        }
    }
}
