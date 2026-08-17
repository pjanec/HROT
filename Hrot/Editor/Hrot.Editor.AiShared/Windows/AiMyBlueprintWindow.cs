using System;
using System.Collections.Generic;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Variables;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Panels;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// ⭐⭐⭐ <b><c>C-outline</c>, HOSTED.</b> The BTree and HSM perspectives get the same My Blueprint
/// outline the blueprint perspective has had since <c>C-sections</c>.
///
/// <para><b>Why this type is thin.</b> <see cref="BlackboardMyBlueprintModel"/> already builds the
/// per-host section list, and <c>MyBlueprintPanel</c> lives in <c>NodeEditor.UI</c> over
/// <c>IMyBlueprintModel</c> in <c>NodeEditor.Core</c> — nothing about either is blueprint-specific.
/// ⛔ The gap was never a missing component; it was that <see cref="BlackboardMyBlueprintModel"/> was
/// <b>constructed by nothing</b>. This window is that constructor.</para>
///
/// <para><b>The panel is lazy, and deliberately.</b> <c>MyBlueprintPanel</c> requires
/// <see cref="IEditorHostServices"/> and <see cref="IEditorCommands"/>, which the AI perspectives do
/// not have at boot — the same reason <c>BlueprintMyBlueprintWindow</c> defers its own panel. Until
/// they arrive the window draws a plain reason, ⛔ not an empty frame that reads as a broken panel.</para>
///
/// <para>⭐⭐⭐ <b>Where they arrive from (Batch 81).</b> They are <b>per DOCUMENT</b>, built by
/// <c>BTreeDocumentFactory</c> / <c>HsmDocumentFactory</c>, and reachable as
/// <c>AiCanvasContext.View.Host</c> + <c>AiCanvasContext.Commands</c>. The window reads them through
/// <see cref="SetCanvasContextResolver"/>, which the registrar installs from the canvas window it is
/// already handed. ⛔ <b>Batch 79/80 left <see cref="Retarget"/> as the only supplier and
/// <c>SyncToSelection</c> as its only caller — so the window passed itself its own nulls and the
/// panel never existed.</b> The user saw that as <i>"Editor host service not available for this
/// perspective yet"</i> on both AI perspectives.</para>
///
/// <para>⚠ <b>Purely additive.</b> <c>BlackboardAuthoringWindow</c> is untouched and still registered;
/// the user's ruling is that the two variable surfaces coexist until
/// <c>Architect_Question_38</c> decides the merge.</para>
/// </summary>
public sealed class AiMyBlueprintWindow : ManagedWindow
{
    private readonly BlackboardHostKind    _host;
    private readonly EditorSelectionStore? _store;
    private object?                        _lastAsset;

    private BlackboardMyBlueprintModel? _model;
    private MyBlueprintPanel?           _panel;

    // ⭐⭐⭐ Batch 81 — TWO sources, kept apart on purpose.
    //
    //   _explicit*  — what a host handed us through Retarget.
    //   _derived*   — what the active document's canvas context currently offers.
    //
    // ⛔ Batch 79/80 kept ONE pair, and Retarget was its only writer while SyncToSelection was its
    //    only reader — so the window fed itself its own nulls and _panel was null forever. Merging
    //    them again reintroduces exactly that loop, and a Retarget(vars, null, null) would erase
    //    services the resolver had just supplied.
    private IEditorHostServices? _explicitHostServices;
    private IEditorCommands?     _explicitCommands;
    private IEditorHostServices? _derivedHostServices;
    private IEditorCommands?     _derivedCommands;

    private Func<AiCanvasContext?>? _canvasContext;

    /// <summary>⭐ Effective services: an explicit host wins, otherwise the active document's.</summary>
    private IEditorHostServices? HostServices => _explicitHostServices ?? _derivedHostServices;

    /// <summary>⭐ Effective commands: an explicit host wins, otherwise the active document's.</summary>
    private IEditorCommands? Commands => _explicitCommands ?? _derivedCommands;

    /// <param name="id">Unique ImGui window id.</param>
    /// <param name="owningPerspective">Perspective key (e.g. <c>"BTree"</c>).</param>
    /// <param name="host">Which AI host this outline describes.</param>
    /// <param name="store">
    /// ⭐⭐ The selection store, so the outline FOLLOWS the active document by itself.
    ///
    /// <para>⛔ <b>This is the durable half of the Batch-80 fix.</b> Batch 79 left retargeting to the
    /// host, and the host never did it — the same failure mode as the five unhosted surfaces, one
    /// level up. <c>BlackboardAuthoringWindow</c> has always read <c>store.ActiveAsset</c> every
    /// frame; so does this now, and there is nothing left for a caller to forget.</para>
    /// </param>
    public AiMyBlueprintWindow(
        string id, string owningPerspective, BlackboardHostKind host,
        EditorSelectionStore? store = null)
        : base(id, "My Blueprint", owningPerspective, WindowScope.PerspectiveBound)
    {
        _host  = host;
        _store = store;
        IsOpen = false;
    }

    /// <summary>
    /// ⭐⭐ Supplies the ACTIVE DOCUMENT's canvas context, from which the panel's
    /// <see cref="IEditorHostServices"/> and <see cref="IEditorCommands"/> are read every frame.
    ///
    /// <para>⭐ <b>The registrar installs this itself</b> from the canvas window it is already handed
    /// via <c>RegisterExtraWindow</c> — ⛔ <b>there is no new argument at the composition root.</b>
    /// The services are per-DOCUMENT and do not exist when the registrar is constructed, which is why
    /// they cannot be a constructor parameter; a resolver read per frame is what makes a document
    /// switch land instead of leaving a stale panel.</para>
    /// </summary>
    public void SetCanvasContextResolver(Func<AiCanvasContext?> resolver)
        => _canvasContext = resolver ?? throw new ArgumentNullException(nameof(resolver));

    /// <summary>True once a canvas-context resolver is installed. ⭐ A rail surface.</summary>
    public bool HasCanvasContextResolver => _canvasContext != null;

    /// <summary>
    /// ⭐ Re-reads the active asset <b>and the active document's services</b>, rebuilding whatever
    /// changed. Called every frame from <see cref="DrawClientArea"/>, and directly by rails — ⛔ the
    /// draw path goes through ImGui, which no headless test can drive.
    ///
    /// <para>⚠ <b>Both inputs, not just the asset.</b> The services arrive later than the window and
    /// change on every document switch, so a one-shot read would leave the panel null forever (the
    /// Batch-81 defect) or stale after the first switch (its obvious sequel).</para>
    /// </summary>
    public void SyncToSelection()
    {
        var context = _canvasContext?.Invoke();
        var host    = context?.View.Host;
        var cmds    = context?.Commands;

        bool servicesChanged = !ReferenceEquals(host, _derivedHostServices)
                            || !ReferenceEquals(cmds, _derivedCommands);
        _derivedHostServices = host;
        _derivedCommands     = cmds;

        // ⛔ Without a store the model is the host's to set through Retarget; only the panel is ours.
        if (_store == null)
        {
            if (servicesChanged) RebuildPanel();
            return;
        }

        var asset = _store.ActiveAsset as IBlackboardManagedAsset;
        if (ReferenceEquals(asset, _lastAsset))
        {
            if (servicesChanged) RebuildPanel();
            return;
        }
        _lastAsset = asset;

        if (asset == null) { _model = null; _panel = null; return; }
        _model = new BlackboardMyBlueprintModel(_host, () => asset.BlackboardVariables);
        RebuildPanel();
    }

    /// <summary>Which host kind this outline is built for. ⭐ Read by the section rails.</summary>
    public BlackboardHostKind Host => _host;

    /// <summary>
    /// The live model, or null before the first <see cref="Retarget"/>. ⭐ Exposed so a rail can
    /// assert on the CONSTRUCTED object rather than on the registrar's source.
    /// </summary>
    public BlackboardMyBlueprintModel? Model => _model;

    /// <summary>True once the panel has host services and can draw. ⭐ Also a rail surface.</summary>
    public bool HasPanel => _panel != null;

    /// <summary>
    /// ⭐ The services the panel is currently built over, or null. ⛔ Exposed so a rail can prove the
    /// panel FOLLOWS a document switch rather than keeping the first document's services — the
    /// obvious sequel to the defect this window's Batch-81 change fixes.
    /// </summary>
    public IEditorHostServices? ActiveHostServices => HostServices;

    /// <summary>
    /// ⭐⭐ Fired when a section is selected in the outline, so the variables table can re-filter to
    /// it. 📄 design §1c: <i>"selection yields a SECTION, not a variable… the routing key is
    /// <c>(asset, section)</c> + a highlight."</i>
    /// </summary>
    public event Action<string>? SectionSelected;

    /// <summary>
    /// Points the outline at an asset's blackboard. Passing <c>null</c> variables clears it.
    /// </summary>
    /// <param name="variables">
    /// Reads the asset's entries. ⭐ A DELEGATE rather than a snapshot, so the outline follows edits
    /// without further wiring — the same reason the blueprint side takes one.
    /// </param>
    public void Retarget(
        Func<IReadOnlyList<BlackboardVariableEntry>>? variables,
        IEditorHostServices? hostServices,
        IEditorCommands?     commands)
    {
        if (variables == null)
        {
            _model = null;
            _panel = null;
            return;
        }

        _model = new BlackboardMyBlueprintModel(_host, variables);

        // ⭐ A non-null argument is an OVERRIDE and sticks; ⛔ a null does NOT erase what the canvas
        //   context derived. Clearing on null is how the closed loop stayed closed.
        if (hostServices != null) _explicitHostServices = hostServices;
        if (commands     != null) _explicitCommands     = commands;

        RebuildPanel();
    }

    /// <summary>
    /// ⭐ Rebuilds <see cref="MyBlueprintPanel"/> over the current model and effective services, or
    /// clears it when either is missing. ⛔ One place, so the model path and the services path cannot
    /// disagree about when a panel exists.
    /// </summary>
    private void RebuildPanel()
    {
        var host = HostServices;
        var cmds = Commands;

        _panel = _model != null && host != null && cmds != null
            ? new MyBlueprintPanel(
                _model, host, cmds,
                navigateToGraph: _ => { },                      // AI hosts have one graph per asset
                navigateToItem:  (sectionId, _) => SelectSection(sectionId))
            : null;
    }

    /// <summary>
    /// ⭐ The routing entry point, callable headlessly. ⛔ The panel's own click path goes through
    /// ImGui, which no test can drive — this is the same call it makes.
    /// </summary>
    public void SelectSection(string sectionId)
    {
        if (string.IsNullOrEmpty(sectionId)) return;
        SelectedSection = sectionId;
        SectionSelected?.Invoke(sectionId);
    }

    /// <summary>The section last selected, or null. ⭐ The routing key's first half.</summary>
    public string? SelectedSection { get; private set; }

    /// <summary>Tells the outline its asset's blackboard changed.</summary>
    public void RaiseChanged() => _model?.RaiseChanged();

    protected override void DrawClientArea()
    {
        SyncToSelection();

        if (_panel == null)
        {
            ImGuiNET.ImGui.TextDisabled(_model == null
                ? "No asset selected."
                : "Editor host services not available for this perspective yet.");
            return;
        }
        _panel.Draw();
    }
}
