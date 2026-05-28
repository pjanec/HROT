using System.Numerics;
using Fdp.Presentation.WindowManager;
using ImGuiNET;

namespace Hrot.Editor.AiShared.Comparison.UI;

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
    }

    /// <summary>Sets which asset this panel is summarising.</summary>
    public void SetActiveAsset(Guid assetId, string assetName)
    {
        _activeAssetId   = assetId;
        _activeAssetName = assetName;
    }

    protected override void DrawClientArea()
    {
        var session = _registry.GetSession(_activeAssetId);
        if (session == null)
        {
            ImGui.TextDisabled("No comparison active.");
            return;
        }

        var state = new ComparisonSummaryPanelState(session, _activeAssetName);

        // Asset title.
        ImGui.Text(state.AssetName);

        // Migration notice.
        if (state.HasMigrationNotice)
            ImGui.TextColored(YellowColor, "Migration: " + state.MigrationNotice);

        // One-sentence summary.
        ImGui.TextWrapped(state.TopSummary);

        ImGui.Separator();

        // Full prose (scrollable).
        if (state.HumanSummary != null)
            ImGui.TextWrapped(state.HumanSummary);

        ImGui.Separator();

        // Severity filter checkboxes.
        foreach (var sev in Severities)
        {
            bool enabled = state.EnabledSeverities.Contains(sev);
            if (ImGui.Checkbox(sev, ref enabled))
                state.ToggleSeverity(sev);
        }
    }
}
