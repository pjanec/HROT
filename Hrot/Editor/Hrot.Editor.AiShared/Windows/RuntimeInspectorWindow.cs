using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Selection;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// Shell for the shared runtime inspector. Renders the entity-lifecycle status,
/// mode controls, scrub bar, and delegates the asset-specific pane to
/// the registered IRuntimeInspectorPane for the active asset kind.
/// Subsystems provide IRuntimeInspectorPane implementations; this window
/// selects the matching pane at draw time.
/// </summary>
public sealed class RuntimeInspectorWindow : ManagedWindow
{
    private readonly EditorSelectionStore _store;
    private readonly IDebugSessionRegistry _registry;
    private readonly List<IRuntimeInspectorPane> _panes = new();

    /// <param name="store">Editor selection store for this perspective.</param>
    /// <param name="registry">Debug session registry.</param>
    /// <param name="idOverride">
    ///   Optional stable ImGui id override (e.g. <c>"ai_runtime_inspector_btree"</c>)
    ///   for per-perspective instances with independent dock layouts.
    /// </param>
    /// <param name="owningPerspective">
    ///   Perspective that owns this instance. Defaults to <c>"Authoring"</c>.
    /// </param>
    public RuntimeInspectorWindow(
        EditorSelectionStore store,
        IDebugSessionRegistry registry,
        string? idOverride = null,
        string? owningPerspective = null)
        : base(idOverride ?? "ai_runtime_inspector", "Runtime Inspector",
               owningPerspective ?? "Authoring", WindowScope.PerspectiveBound)
    {
        _store = store;
        _registry = registry;
    }

    /// <summary>Register a subsystem-provided pane. Called at editor startup.</summary>
    public void RegisterPane(IRuntimeInspectorPane pane) => _panes.Add(pane);

    /// <summary>Number of registered panes. Exposed for test verification.</summary>
    internal int RegisteredPaneCount => _panes.Count;

    /// <summary>
    /// ⭐⭐⭐ <b><c>L2.3</c> — WHICH grey line this window shows, as a value.</b>
    /// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §6 <c>L2.3</c>, which names <b>these two
    /// sites</b> *(<c>:54</c> and <c>:67</c>)* by line number.
    ///
    /// <para>⛔⛔ <b>Both used to say <i>"No active session."</i> — and they are not the same fact.</b>
    /// 📐 Measured: the first fires when <c>ActiveAsset</c> is <see langword="null"/> *(nothing is open
    /// — fixed by OPENING a document)*; the second when a document IS open and no pane claims its kind
    /// *(fixed by selecting something else, or by the missing pane being registered)*. ⚠ Neither is
    /// about a <i>session</i> at all. ⇒ ⭐ 📌 <c>R-118</c>'s lesson applied to prose: one sentence
    /// standing for several facts sends the designer to the wrong place.</para>
    ///
    /// <para>⭐ Returns a STRING so this is railable without a draw — 📌 §6: <i>"every task's rail
    /// asserts on a store or a returned model."</i></para>
    ///
    /// <para>⚠ <b>This window is on borrowed time and that is fine.</b> 📌 §4: its pane registry keys on
    /// <c>AssetKind</c>, which <c>R-112</c> rules is never a view key ⇒ <c>L3</c> turns its three panes
    /// into three predicated views and the window DISSOLVES. ⭐ Until then it must not lie.</para>
    /// </summary>
    internal string? EmptyState()
    {
        var activeAsset = _store.ActiveAsset;
        if (activeAsset == null) return Shell.DetailsEmptyState.NoDocument;

        // Find the pane that matches the currently active asset's kind (Blueprint, BTree, or HSM)
        return _panes.Find(p => p.TargetKind == activeAsset.Kind) == null
            ? Shell.DetailsEmptyState.NothingForThisSelection
            : null;
    }

    protected override void DrawClientArea()
    {
        // ⭐ A thin renderer over EmptyState() — ⛔ it decides nothing the rail cannot check.
        var empty = EmptyState();
        if (empty != null)
        {
            ImGuiNET.ImGui.TextDisabled(empty);
            return;
        }

        _panes.Find(p => p.TargetKind == _store.ActiveAsset!.Kind)!.Draw();
    }
}
