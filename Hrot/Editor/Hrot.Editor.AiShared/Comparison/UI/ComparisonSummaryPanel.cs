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
    private Guid _activeAssetId;
    private string _activeAssetName = "";

    public ComparisonSummaryPanel(ComparisonSessionRegistry registry)
        : base("ai_comparison_summary", "Comparison Summary", "Analysis", WindowScope.PerspectiveBound)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));

        // ⭐⭐⭐ U-obs-5 — DECLARED AT CONSTRUCTION, ALWAYS, ungated on CaptureEnabled.
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>Sets which asset this panel is summarising.</summary>
    public void SetActiveAsset(Guid assetId, string assetName)
    {
        _activeAssetId   = assetId;
        _activeAssetName = assetName;
    }

    /// <summary>
    /// ⭐⭐⭐ BUILD · CAPTURE. ⛔⛔ No ImGui — reads the session state the same way the old draw did,
    /// published before any render call. ⚠ Returns the wrapped <see cref="ComparisonSummaryPanelState"/>
    /// too, since <c>ToggleSeverity</c> (a mutation, not a display value) still needs it.
    /// </summary>
    private (ComparisonSummaryPanelViewModel Vm, ComparisonSummaryPanelState? State) BuildAndPublish()
    {
        var session = _registry.GetSession(_activeAssetId);
        ComparisonSummaryPanelViewModel vm;
        ComparisonSummaryPanelState? state = null;

        if (session == null)
        {
            vm = new ComparisonSummaryPanelViewModel(
                Id, Kind, HasSession: false, _activeAssetName,
                HasMigrationNotice: false, MigrationNotice: null,
                TopSummary: null, HumanSummary: null,
                EnabledSeverities: Array.Empty<string>(), AllSeverities: Severities);
        }
        else
        {
            state = new ComparisonSummaryPanelState(session, _activeAssetName);
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
