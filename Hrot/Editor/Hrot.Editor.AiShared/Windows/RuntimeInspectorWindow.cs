using System;
using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Selection;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — the window's OWN decision, dumped.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c>
/// §Example.
///
/// <para>⚠ <b>Deliberately narrower than the panel's full paint.</b> <see cref="RuntimeInspectorWindow"/>
/// delegates its content to an opaque <c>IRuntimeInspectorPane.Draw()</c> with no returned model — that
/// pane's own content is a separate subsystem's concern, not this shell's. ⭐ What IS this shell's own
/// state — which grey line is shown, or which pane kind claimed the selection — is captured whole.</para>
/// </summary>
public sealed record RuntimeInspectorPanelViewModel(
    string PanelId,
    string PanelKind,
    string? EmptyState,
    string? ActivePaneKind,
    int RegisteredPaneCount) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// Shell for the shared runtime inspector. Renders the entity-lifecycle status,
/// mode controls, scrub bar, and delegates the asset-specific pane to
/// the registered IRuntimeInspectorPane for the active asset kind.
/// Subsystems provide IRuntimeInspectorPane implementations; this window
/// selects the matching pane at draw time.
/// </summary>
public sealed class RuntimeInspectorWindow : ManagedWindow
{
    /// <summary>⭐ <c>U-obs-5</c> — THE KIND. ⛔ Single-host: stays a local literal.</summary>
    internal const string Kind = "runtime-inspector";

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
    /// <param name="detailsViews">
    /// ⭐⭐⭐ <b><c>L3.1</c> — this perspective's Details catalogue, so a registered pane becomes a
    /// Details VIEW as well as a pane.</b> 📄 §6 <c>L3</c>'s Runtime row · §4's <i>"3 panes → 3
    /// predicated views"</i>.
    /// <para>⚠ Optional <b>only</b> for the standalone-window tests that predate the shell; ⭐ the
    /// registrar always passes it — 📌 the <c>2026-08-16</c> rule, and the rail
    /// <c>TheProductionRegistrar_RegistersTheRuntimeView</c> asserts the production path rather than
    /// this parameter's default.</para>
    /// </param>
    public RuntimeInspectorWindow(
        EditorSelectionStore store,
        IDebugSessionRegistry registry,
        string? idOverride = null,
        string? owningPerspective = null,
        Shell.DetailsViewRegistry? detailsViews = null)
        : base(idOverride ?? "ai_runtime_inspector", "Runtime Inspector",
               owningPerspective ?? "Authoring", WindowScope.PerspectiveBound)
    {
        _store = store;
        _registry = registry;
        _detailsViews = detailsViews;

        // ⭐⭐⭐ U-obs-5 — DECLARED AT CONSTRUCTION, ALWAYS, ungated on CaptureEnabled.
        PanelSnapshot.DeclareInstrumented(Id);
    }

    private readonly Shell.DetailsViewRegistry? _detailsViews;

    /// <summary>
    /// Register a subsystem-provided pane. Called at editor startup.
    ///
    /// <para>⭐⭐⭐ <b><c>L3.1</c> — registering a pane ALSO contributes its Details view</b>, so
    /// <c>EditorSubsystem</c> gains <b>nothing to forget</b> *(📌 <c>R-67</c>; this is the same
    /// claim-chain principle <c>L1.2</c> used, applied to the seam that already exists)*. 📐 The three
    /// production call sites — <c>EditorSubsystem:2864</c>/<c>:2870</c>/<c>:2884</c> — are
    /// <b>unchanged</b>.</para>
    ///
    /// <para>⚠⚠ <b>Why this is not the claim chain itself:</b> panes are registered LONG AFTER the
    /// workspace is built, and <c>IDetailsViewSource</c> is read ONCE at registration. ⛔ Making the
    /// registry lazily re-read would turn a snapshot into a live query and cost every frame; ⭐ pushing
    /// at the moment of registration is the honest shape.</para>
    ///
    /// <para>⛔⛔ <b>A second pane for the same kind now THROWS</b>, via the registry's duplicate-id
    /// guard. 📐 It used to be silent: <c>_panes.Find</c> returned the FIRST and the second pane simply
    /// never drew. ⚠ That is a wiring bug wearing a working editor, and 📌 the <c>G4</c> precedent says
    /// it must fail where it is wired.</para>
    /// </summary>
    public void RegisterPane(IRuntimeInspectorPane pane)
    {
        ArgumentNullException.ThrowIfNull(pane);
        _panes.Add(pane);
        _detailsViews?.Add(Shell.RuntimeDetailsViewDescriptor.For(pane));
    }

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

    /// <summary>⭐⭐⭐ BUILD · CAPTURE. ⛔⛔ No ImGui — <see cref="EmptyState"/> was already pure.</summary>
    private RuntimeInspectorPanelViewModel BuildAndPublish()
    {
        var empty = EmptyState();
        var activePane = empty == null
            ? _panes.Find(p => p.TargetKind == _store.ActiveAsset!.Kind)
            : null;

        var vm = new RuntimeInspectorPanelViewModel(
            Id, Kind, empty, activePane?.TargetKind.ToString(), _panes.Count);

        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion, callable with no live ImGui context.</summary>
    internal RuntimeInspectorPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        // ⭐ A thin renderer over BuildAndPublish() — ⛔ it decides nothing the rail cannot check.
        var vm = BuildAndPublish();
        if (vm.EmptyState != null)
        {
            ImGuiNET.ImGui.TextDisabled(vm.EmptyState);
            return;
        }

        _panes.Find(p => p.TargetKind == _store.ActiveAsset!.Kind)!.Draw();
    }
}
