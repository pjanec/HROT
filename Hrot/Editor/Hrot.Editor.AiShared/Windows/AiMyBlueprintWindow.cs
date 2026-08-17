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
/// <see cref="Retarget"/> supplies them the window draws a plain reason, ⛔ not an empty frame that
/// reads as a broken panel.</para>
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
    private IEditorHostServices?        _hostServices;
    private IEditorCommands?            _commands;

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
    /// ⭐ Re-reads the active asset and rebuilds the model when it changed. Called every frame from
    /// <see cref="DrawClientArea"/>, and directly by rails — ⛔ the draw path goes through ImGui,
    /// which no headless test can drive.
    /// </summary>
    public void SyncToSelection()
    {
        if (_store == null) return;

        var asset = _store.ActiveAsset as IBlackboardManagedAsset;
        if (ReferenceEquals(asset, _lastAsset)) return;
        _lastAsset = asset;

        if (asset == null) { _model = null; _panel = null; return; }
        Retarget(() => asset.BlackboardVariables, _hostServices, _commands);
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

        if (!ReferenceEquals(_hostServices, hostServices) || !ReferenceEquals(_commands, commands))
        {
            _hostServices = hostServices;
            _commands     = commands;
        }

        _panel = _hostServices != null && _commands != null
            ? new MyBlueprintPanel(
                _model, _hostServices, _commands,
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
