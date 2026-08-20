using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Refactor;

namespace Hrot.Blueprints.Editor.Windows;

/// <summary>
/// <see cref="ManagedWindow"/> wrapper around <see cref="Variables.BlueprintVariablesWindow"/>
/// for the new AI editor shared-perspective infrastructure (AIE-048).
///
/// The composition root drives <see cref="EditorSelectionStore.SelectAsset"/> (via the legacy
/// bridge field) whenever the active Blueprint document changes, keeping the inner window
/// in sync without modifying it.
/// </summary>
public sealed class BlueprintVariablesManagedWindow : ManagedWindow
{
    private readonly Variables.BlueprintVariablesWindow _inner;

    // ── ctor ─────────────────────────────────────────────────────────────────

    /// <param name="legacySelectionStore">
    ///   Legacy <see cref="EditorSelectionStore"/> driven by the composition root's
    ///   <c>ActiveChanged</c> handler.
    /// </param>
    /// <param name="refactorService">Shared refactor service.</param>
    /// <param name="idOverride">Stable ImGui id; defaults to <c>"ai_variables_blueprint"</c>.</param>
    /// <param name="owningPerspective">Perspective name; defaults to <c>"Blueprint"</c>.</param>
    public BlueprintVariablesManagedWindow(
        EditorSelectionStore      legacySelectionStore,
        IRefactorService          refactorService,
        string?                   idOverride        = null,
        string?                   owningPerspective = null)
        : base(idOverride        ?? "ai_variables_blueprint",
               "Blueprint Variables",
               owningPerspective ?? "Blueprint",
               WindowScope.PerspectiveBound)
    {
        _inner = new Variables.BlueprintVariablesWindow(
            selectionStore:  legacySelectionStore ?? throw new ArgumentNullException(nameof(legacySelectionStore)),
            dirtyTracker:    new DirtyTracker(),
            refactorService: refactorService ?? throw new ArgumentNullException(nameof(refactorService)));
    }

    // ── ManagedWindow ─────────────────────────────────────────────────────────

    protected override void DrawClientArea() => _inner.DrawUI();
}
