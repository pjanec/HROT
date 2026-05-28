using System.Linq;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Comparison.Rendering;
using ImGuiNET;

namespace Hrot.Editor.AiShared.Comparison.UI;

// ---- State model ------------------------------------------------------------

/// <summary>
/// View-model for <see cref="ComparisonSidebar"/>.
/// Exposes a filtered list of changes and a focus callback.
/// See design section 6.6.
/// </summary>
public sealed class ComparisonSidebarState
{
    private readonly ComparisonSessionState _session;
    private readonly Action<string>? _onFocusNode;

    /// <summary>Changes whose severity is currently enabled in the session filter.</summary>
    public IReadOnlyList<ComparisonChange> VisibleChanges =>
        _session.Response.Changes
            .Where(c => _session.EnabledSeverities.Contains(c.Severity))
            .ToList();

    public ComparisonSidebarState(ComparisonSessionState session, Action<string>? onFocusNode = null)
    {
        _session      = session ?? throw new ArgumentNullException(nameof(session));
        _onFocusNode  = onFocusNode;
    }

    /// <summary>
    /// Invoked when the user clicks a change row.
    /// Fires <see cref="_onFocusNode"/> with the change's ElementId if non-null.
    /// </summary>
    public void FocusChange(ComparisonChange change)
    {
        if (change.ElementId != null)
            _onFocusNode?.Invoke(change.ElementId);
    }
}

// ---- Panel ------------------------------------------------------------------

/// <summary>
/// Docked sidebar listing all visible comparison changes with glyph + severity coloring.
/// See design section 6.6.
/// </summary>
public sealed class ComparisonSidebar : ManagedWindow
{
    private readonly ComparisonSessionRegistry _registry;
    private Guid _activeAssetId;

    public ComparisonSidebar(ComparisonSessionRegistry registry)
        : base("ai_comparison_sidebar", "Comparison Changes", "Analysis", WindowScope.PerspectiveBound)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>Sets which asset this sidebar is showing changes for.</summary>
    public void SetActiveAsset(Guid assetId) => _activeAssetId = assetId;

    protected override void DrawClientArea()
    {
        var session = _registry.GetSession(_activeAssetId);
        if (session == null)
        {
            ImGui.TextDisabled("No comparison active.");
            return;
        }

        var state = new ComparisonSidebarState(session);

        foreach (var change in state.VisibleChanges)
        {
            var glyph = ComparisonStyleMap.GlyphForKind(change.Kind);
            var color = ComparisonStyleMap.ColorForSeverity(change.Severity);

            // First line: glyph + element description on left, severity on right.
            ImGui.Text($"[{glyph}] {change.ElementDescription}");
            ImGui.SameLine();
            ImGui.TextColored(color, change.Severity);

            // Detail line.
            ImGui.TextWrapped(change.Description);
        }
    }
}
