using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.View;

namespace NodeEditor.UI.Find;

/// <summary>
/// The slim find bar inserted above the canvas (Ctrl+F).
/// Owns its own visibility, search text, and active-match index.
/// On query change, asks the <see cref="FindEngine"/> to enumerate
/// matches in the current graph and exposes them as a navigable list.
/// </summary>
public sealed class FindBar
{
    private readonly GraphView  _view;
    private readonly FindEngine _engine;
    private string              _searchText = string.Empty;
    private bool                _needsFocus;

    /// <summary>Show/hide the bar. Ctrl+F sets true; Esc on empty bar sets false.</summary>
    public bool IsVisible { get; set; }

    /// <summary>Active scope.</summary>
    public FindScope Scope { get; set; } = FindScope.CurrentGraph;

    /// <summary>Case-sensitive search.</summary>
    public bool CaseSensitive { get; set; }

    /// <summary>Regex mode.</summary>
    public bool RegexMode { get; set; }

    /// <summary>Current match index within <see cref="Results"/>.</summary>
    public int ActiveIndex { get; private set; }

    /// <summary>All matches in the current graph.</summary>
    public IReadOnlyList<FindResult> Results { get; private set; } = Array.Empty<FindResult>();

    /// <summary>Create a find bar bound to the given view and engine.</summary>
    public FindBar(GraphView view, FindEngine engine)
    {
        _view   = view;
        _engine = engine;
    }

    /// <summary>
    /// Draw the bar inside the current ImGui region (above the canvas).
    /// Returns true while the bar is visible.
    /// </summary>
    public void Draw()
    {
        if (!IsVisible) return;

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.12f, 0.12f, 0.12f, 1f));
        if (ImGui.BeginChild("##find_bar", new Vector2(0, 28), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar))
        {
            DrawContents();
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    /// <summary>Advance to the next match (F3).</summary>
    public void Next()
    {
        if (Results.Count == 0) return;
        ActiveIndex = (ActiveIndex + 1) % Results.Count;
        CenterOnActive();
    }

    /// <summary>Retreat to the previous match (Shift+F3).</summary>
    public void Previous()
    {
        if (Results.Count == 0) return;
        ActiveIndex = (ActiveIndex - 1 + Results.Count) % Results.Count;
        CenterOnActive();
    }

    /// <summary>Open the bar and focus the search field.</summary>
    public void Open()
    {
        IsVisible    = true;
        _needsFocus  = true;
    }

    // ── private ──────────────────────────────────────────────────────────────

    private void DrawContents()
    {
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        ImGui.SetCursorPosY((ImGui.GetWindowHeight() - ImGui.GetFrameHeight()) * 0.5f);

        // Search input
        ImGui.PushItemWidth(220);
        if (_needsFocus) { ImGui.SetKeyboardFocusHere(); _needsFocus = false; }

        // Snapshot whether we had text BEFORE ImGui processes the input and potentially reverts it.
        bool hasTextBefore = !string.IsNullOrEmpty(_searchText);

        string searchBuf = _searchText;
        if (ImGui.InputText("##find-search", ref searchBuf, 256))
        {
            _searchText  = searchBuf;
            ActiveIndex  = 0;
            RefreshResults();
        }

        // Esc handling.
        // We check window focus instead of item focus because ImGui.InputText instantly
        // drops its active focus state on the frame Escape is pressed.
        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            // Use the snapshot to determine if we should clear text or close the bar.
            if (hasTextBefore)
            {
                _searchText = string.Empty;
                RefreshResults();
                _needsFocus = true;
            }
            else
            {
                IsVisible = false;
                Results   = Array.Empty<FindResult>();
            }
        }

        // F3 / Shift+F3 when focused in this child
        if (ImGui.IsKeyPressed(ImGuiKey.F3))
        {
            if (ImGui.GetIO().KeyShift) Previous(); else Next();
        }

        ImGui.PopItemWidth();
        ImGui.SameLine(0, spacing);

        // Scope dropdown
        var scopeLabel = Scope switch
        {
            FindScope.CurrentGraph  => "Graph",
            FindScope.Asset         => "Asset",
            FindScope.OpenTabs      => "Tabs",
            FindScope.WholeProject  => "Project",
            _                       => "Graph",
        };
        ImGui.PushItemWidth(70);
        if (ImGui.BeginCombo("##find-scope", scopeLabel, ImGuiComboFlags.NoArrowButton))
        {
            foreach (FindScope s in Enum.GetValues<FindScope>())
            {
                bool sel = Scope == s;
                var label = s switch
                {
                    FindScope.CurrentGraph  => "Graph",
                    FindScope.Asset         => "Asset",
                    FindScope.OpenTabs      => "Tabs",
                    FindScope.WholeProject  => "Project",
                    _                       => s.ToString(),
                };
                if (ImGui.Selectable(label, sel)) { Scope = s; RefreshResults(); }
                if (sel) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        ImGui.PopItemWidth();
        ImGui.SameLine(0, spacing);

        // Previous button (▲)
        if (ImGui.SmallButton("##find-prev")) Previous();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Previous match (Shift+F3)");
        ImGui.SameLine(0, 2);

        // Next button (▼)
        if (ImGui.SmallButton("##find-next")) Next();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Next match (F3)");
        ImGui.SameLine(0, spacing);

        // Case-sensitive toggle [Aa]
        if (DrawToggleButton("##find-case", "Aa", CaseSensitive, "Case sensitive"))
        {
            CaseSensitive = !CaseSensitive;
            RefreshResults();
        }
        ImGui.SameLine(0, 2);

        // Regex toggle [.*]
        if (DrawToggleButton("##find-regex", ".*", RegexMode, "Regex mode"))
        {
            RegexMode = !RegexMode;
            RefreshResults();
        }
        ImGui.SameLine(0, spacing);

        // Match count label
        var countText = Results.Count == 0
            ? "No results"
            : $"{ActiveIndex + 1}/{Results.Count}";
        ImGui.TextDisabled(countText);
        ImGui.SameLine(0, spacing);

        // Close button [✕]
        if (ImGui.SmallButton("##find-close"))
        {
            IsVisible   = false;
            _searchText = string.Empty;
            Results     = Array.Empty<FindResult>();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Close (Esc)");
    }

    private static bool DrawToggleButton(string id, string label, bool active, string tooltip)
    {
        if (active)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.5f, 0.8f, 1f));
        }
        var clicked = ImGui.SmallButton($"{label}##{id}");
        if (active) ImGui.PopStyleColor();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
        return clicked;
    }

    private void RefreshResults()
    {
        if (string.IsNullOrEmpty(_searchText))
        {
            Results     = Array.Empty<FindResult>();
            ActiveIndex = 0;
            return;
        }

        var query = FindQueryParser.Parse(_searchText);
        Results     = _engine.Search(query, Scope, _view).ToList();
        ActiveIndex = 0;
        if (Results.Count > 0) CenterOnActive();
    }

    private void CenterOnActive()
    {
        if (Results.Count == 0) return;
        var result = Results[ActiveIndex];
        if (result.Node is { } nodeId)
        {
            var node = _view.Model.FindNode(nodeId);
            if (node is not null)
            {
                // Calculate the actual center of the node.
                var nodeSize = node.SizeOverride ?? new Vector2(160, 64);
                var nodeCenter = node.Position + nodeSize * 0.5f;

                var canvasCenterGraph = _view.Viewport.ScreenToGraph(
                    _view.Viewport.CanvasScreenOrigin + _view.Viewport.CanvasScreenSize * 0.5f);

                var delta = canvasCenterGraph - nodeCenter;
                var targetPan = _view.Viewport.PanGraph - delta;

                // Trigger smooth camera animation.
                _view.Interaction.BeginViewportTween(targetPan, _view.Viewport.Zoom, 180);
            }
        }
    }
}
