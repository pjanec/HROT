using ImGuiNET;
using NodeEditor.Core.Interfaces;
using System.Numerics;

namespace NodeEditor.UI.Panels;

/// <summary>
/// The Details panel. Render once per frame inside any ImGui window region.
/// Picks the best registered <see cref="IDetailsViewProvider"/> for the
/// current target and delegates rendering. Persists section collapse state
/// globally per section-name key.
/// </summary>
public sealed class DetailsPanel
{
    private readonly IDetailsViewRegistry _registry;
    private readonly IDetailsContext _ctx;
    private readonly IDetailsRenderContext _renderCtx;

    private DetailsTarget _target = new DetailsTarget.None();
    private IDetailsView? _currentView;

    /// <summary>The current details target (drives view selection).</summary>
    public DetailsTarget Target
    {
        get => _target;
        set
        {
            if (value == _target) return;

            // Flush pending changes before switching.
            if (_currentView?.IsDirty == true)
                _currentView.Commit();

            _target      = value;
            _currentView = BuildView(value);
        }
    }

    /// <summary>Whether to show advanced (host-marked) sections.</summary>
    public bool ShowAdvanced
    {
        get => ((DetailsRenderContext)_renderCtx).ShowAdvanced;
        set => ((DetailsRenderContext)_renderCtx).ShowAdvanced = value;
    }

    /// <summary>Whether to show help tooltips on property labels.</summary>
    public bool ShowHelpTooltips
    {
        get => ((DetailsRenderContext)_renderCtx).ShowHelpTooltips;
        set => ((DetailsRenderContext)_renderCtx).ShowHelpTooltips = value;
    }

    /// <summary>
    /// Construct the panel.
    /// </summary>
    /// <param name="registry">Provider registry.</param>
    /// <param name="context">Details context passed to providers at build time.</param>
    public DetailsPanel(IDetailsViewRegistry registry, IDetailsContext context)
    {
        _registry  = registry;
        _ctx       = context;
        _renderCtx = new DetailsRenderContext
        {
            Icons          = context.Icons,
            Theme          = context.Theme,
            ShowAdvanced   = false,
            ShowHelpTooltips = true,
        };
    }

    /// <summary>Draw inside the current ImGui region.</summary>
    public void Draw()
    {
        DrawHeader();
        ImGui.Separator();
        DrawBreadcrumb();
        ImGui.Separator();

        if (_currentView is not null)
            _currentView.Draw(_renderCtx);
        else
            DrawFallback();
    }

    // ── header ────────────────────────────────────────────────────────────────

    private void DrawHeader()
    {
        ImGui.TextColored(_ctx.Theme.TextDefault, "Details");
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 16f);
        if (ImGui.SmallButton("\u22ee"))  // ⋮
            ImGui.OpenPopup("##details_overflow");

        if (ImGui.BeginPopup("##details_overflow"))
        {
            if (ImGui.MenuItem("Collapse All Sections"))
                CollapseAll();
            if (ImGui.MenuItem("Expand All Sections"))
                ExpandAll();
            if (ImGui.MenuItem("Reset This Item to Defaults"))
                ResetToDefaults();
            ImGui.Separator();
            bool adv = ShowAdvanced;
            if (ImGui.MenuItem("Show Advanced Properties", null, ref adv))
                ShowAdvanced = adv;
            bool tips = ShowHelpTooltips;
            if (ImGui.MenuItem("Show Help Tooltips", null, ref tips))
                ShowHelpTooltips = tips;
            ImGui.EndPopup();
        }
    }

    // ── breadcrumb ────────────────────────────────────────────────────────────

    private void DrawBreadcrumb()
    {
        string label = _target switch
        {
            DetailsTarget.None            => "(nothing selected)",
            DetailsTarget.SingleNode s    => $"Node {s.Id}",
            DetailsTarget.MultipleNodes m => $"{m.Ids.Count} nodes selected",
            DetailsTarget.Variable v      => $"Variable: {v.VariableId}",
            DetailsTarget.Function f      => $"Function: {f.FunctionId}",
            DetailsTarget.Macro m         => $"Macro: {m.MacroId}",
            DetailsTarget.CustomEvent e   => $"Event: {e.EventId}",
            DetailsTarget.EventDispatcher d => $"Dispatcher: {d.DispatcherId}",
            DetailsTarget.Comment c       => $"Comment {c.Id}",
            DetailsTarget.Asset           => "Asset",
            _                             => _target.GetType().Name,
        };
        ImGui.TextColored(_ctx.Theme.TextMuted, label);
    }

    // ── fallback ──────────────────────────────────────────────────────────────

    private void DrawFallback()
    {
        ImGui.TextColored(_ctx.Theme.TextMuted, "(no provider registered for this target)");
    }

    // ── section collapse ──────────────────────────────────────────────────────

    private void CollapseAll()  { /* section state persisted in view */ }
    private void ExpandAll()    { }
    private void ResetToDefaults() => _currentView?.Revert();

    // ── helpers ───────────────────────────────────────────────────────────────

    private IDetailsView? BuildView(DetailsTarget target)
    {
        // Try registry first.
        var view = _registry.GetViewFor(target, _ctx);
        if (view is not null) return view;

        // Built-in fallbacks.
        return target switch
        {
            DetailsTarget.Comment c       => new Views.CommentDetailsView(c.Id, _ctx),
            DetailsTarget.MultipleNodes m => new Views.MultipleNodesDetailsView(m.Ids),
            _                             => new Views.FallbackDetailsView(null),
        };
    }
}
