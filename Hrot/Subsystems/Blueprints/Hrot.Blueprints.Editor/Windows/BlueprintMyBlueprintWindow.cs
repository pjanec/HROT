using Fdp.Presentation.WindowManager;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using Hrot.Editor.AiShared;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Action;
using NodeEditor.UI.Panels;

namespace Hrot.Blueprints.Editor.Windows;

/// <summary>
/// <see cref="ManagedWindow"/> that hosts the NodeEdit <see cref="MyBlueprintPanel"/>
/// for the Blueprint perspective (AIE-047).
///
/// <para>
/// The window owns a <see cref="BlueprintMyBlueprintModel"/> and retargets it
/// (via <see cref="Retarget"/>) whenever the active document changes.
/// </para>
/// </summary>
public sealed class BlueprintMyBlueprintWindow : ManagedWindow
{
    private readonly BlueprintMyBlueprintModel _model = new();

    // Panel is lazy — requires host services, which may be null at boot if no canvas context exists.
    private MyBlueprintPanel? _panel;

    // Last known host services (updated on Retarget when AiCanvasContext is present).
    private IEditorHostServices? _hostServices;
    private IEditorCommands? _commands;

    // BCP-BATCH-02-FIX2 Task 5: variable-create modal (name + type). Rebuilt per active
    // asset so its confirm callback targets the current asset.
    private VariableCreateModal? _createVariableModal;

    // BP-12c: custom-event-create modal (name + parameters). Rebuilt per active asset for the
    // same reason as the variable modal — its confirm callback closes over the target asset.
    private CustomEventCreateModal? _createCustomEventModal;

    // BP-12b: the rename prompt for My Blueprint items. Shared by variables and custom events —
    // the per-kind validity rules live in BlueprintDocumentFactory.RenameItem, not here.
    private readonly ItemRenameModal _renameItemModal = new();

    // ── ctor ─────────────────────────────────────────────────────────────────

    /// <param name="idOverride">Stable ImGui id; defaults to <c>"ai_my_blueprint_blueprint"</c>.</param>
    /// <param name="owningPerspective">Perspective name; defaults to <c>"Blueprint"</c>.</param>
    public BlueprintMyBlueprintWindow(
        string? idOverride        = null,
        string? owningPerspective = null)
        : base(idOverride        ?? "ai_my_blueprint_blueprint",
               "My Blueprint",
               owningPerspective ?? "Blueprint",
               WindowScope.PerspectiveBound)
    {
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Retarget to a different active Blueprint asset (or null to clear).
    /// Also receives host services so the panel can be (re)created.
    /// </summary>
    /// <param name="view">
    /// BP-12b — the document's canvas view, when one is open. Item rename/delete/duplicate are
    /// recorded on its undo stack; without it they still work, just unrecorded.
    /// </param>
    public void Retarget(
        IEditableAsset?      editableAsset,
        BlueprintAsset?      blueprintAsset,
        IEditorHostServices? hostServices,
        IEditorCommands?     commands,
        NodeEditor.Core.View.GraphView? view = null)
    {
        _model.Retarget(editableAsset, blueprintAsset);

        // If host services changed (or panel not yet built), rebuild the panel.
        if (!ReferenceEquals(_hostServices, hostServices) || _panel == null)
        {
            _hostServices = hostServices;
            _commands     = commands;
            _panel        = null; // will be created lazily in DrawClientArea
        }

        // Build the variable-create modal for the active asset and route the My Blueprint
        // "+" command (editor.create-variable) to open it. On confirm the modal calls the
        // headless-tested create path (BlueprintDocumentFactory.CreateVariable).
        if (blueprintAsset != null && commands is EditorCommandsImpl cmdImpl)
        {
            var markDirty = editableAsset is Catalog.BlueprintFileAsset bpFile
                ? (Action)bpFile.MarkDirty
                : null;
            _createVariableModal = new VariableCreateModal(
                (name, typeId, capacity, initialLength) => BlueprintDocumentFactory.CreateVariable(
                    blueprintAsset, name, typeId, markDirty, capacity, initialLength),
                blueprintAsset);

            BlueprintDocumentFactory.RegisterCreateVariableCommand(
                cmdImpl, _createVariableModal.Open);

            // BP-12c: same shape for "Custom Events +". Until this, the section declared
            // editor.create-custom-event and nothing registered it, so the button was inert —
            // and BP-07's CallCustomEvent picker had nothing it could ever list.
            _createCustomEventModal = new CustomEventCreateModal(
                (name, parameters) => BlueprintDocumentFactory.CreateCustomEvent(
                    blueprintAsset, name, parameters, markDirty),
                blueprintAsset);

            BlueprintDocumentFactory.RegisterCreateCustomEventCommand(
                cmdImpl, _createCustomEventModal.Open);

            // BP-12b: rename / delete / duplicate. The context menu has always invoked these three
            // and nothing ever handled them, so a variable could be created but never renamed or
            // removed.
            BlueprintDocumentFactory.RegisterMyBlueprintItemCommands(
                cmdImpl, blueprintAsset, view, markDirty,
                promptForName: (current, onConfirm) => _renameItemModal.Open(current, onConfirm));
        }
        else
        {
            _createVariableModal    = null;
            _createCustomEventModal = null;
        }
    }

    /// <summary>
    /// Exposes the underlying model for tests that need to verify projection
    /// without going through ImGui.
    /// </summary>
    public BlueprintMyBlueprintModel Model => _model;

    // ── ManagedWindow ─────────────────────────────────────────────────────────

    protected override void DrawClientArea()
    {
        if (_model == null || _hostServices == null || _commands == null)
        {
            ImGuiNET.ImGui.TextDisabled("No blueprint open.");
            return;
        }

        // Lazy panel creation (needs host services).
        if (_panel == null)
        {
            _panel = new MyBlueprintPanel(
                model:           _model,
                host:            _hostServices,
                commands:        _commands,
                navigateToGraph: _ => { },
                navigateToItem:  (_, _) => { });
        }

        _panel.Draw();

        // Draw the create modals (opened by the section "+" commands). No-op when closed.
        _createVariableModal?.Draw();
        _createCustomEventModal?.Draw();
        _renameItemModal.Draw();
    }
}
