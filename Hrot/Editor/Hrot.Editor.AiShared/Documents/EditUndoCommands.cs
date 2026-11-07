using System;
using NodeEditor.Core.Action;
using NodeEditor.Primitives;

namespace Hrot.Editor.AiShared.Documents;

/// <summary>
/// ⭐ B-14 / Edit-mode undo U2 (📄 docs/UX/DESIGN_Edit_Mode_Undo.md §2.9, R-4133 Q2) — <i>Edit ▸ Undo &lt;step&gt;</i> / <i>Redo &lt;step&gt;</i>, Ctrl+Z / Ctrl+Y
/// (and Ctrl+Shift+Z), as shell commands over the editor's ONE history: the key router gives a focused graph canvas its own Ctrl+Z first, then these.
/// ⭐ Only in Edit mode — elsewhere greyed with the reason (no world steps outside Edit; a restore during a run would fight it).
/// </summary>
public static class EditUndoCommands
{
    public const string UndoId = "edit.undo";
    public const string RedoId = "edit.redo";
    public const string RedoAltId = "edit.redoAlt";

    /// <summary>The reason shown outside Edit mode.</summary>
    public const string OnlyInEdit = "only in Edit mode";

    /// <summary>The label of the Undo item: <c>Undo Move</c>, <c>Undo (nothing to undo)</c>, <c>Undo (only in Edit mode)</c>.</summary>
    public static string Label(string verb, string? step, bool inEdit, bool can)
        => !inEdit ? $"{verb} ({OnlyInEdit})"
         : step is null ? $"{verb} (nothing to {verb.ToLowerInvariant()})"
         : can ? $"{verb} {step}"
         : $"{verb} {step} (no longer available)";

    /// <summary>Registers the three commands.</summary>
    public static void Register(Action<EditorCommandDescriptor, Action<EditorCommandContext>> register, Hrot.Presentation.Edits.EditHistory history, Func<bool> inEdit)
    {
        ArgumentNullException.ThrowIfNull(register);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(inEdit);
        register(new EditorCommandDescriptor(UndoId, "Undo", "Edit", "Undo the last change (Ctrl+Z)", null,
                     new KeyBinding(EditorKey.Z, KeyModifiers.Ctrl),
                     IsEnabled: () => inEdit() && history.CanUndo,
                     DynamicDisplayName: () => Label("Undo", history.UndoLabel, inEdit(), history.CanUndo)),
                 _ => { if (inEdit()) history.Undo(); });
        register(new EditorCommandDescriptor(RedoId, "Redo", "Edit", "Redo the change undone last (Ctrl+Y)", null,
                     new KeyBinding(EditorKey.Y, KeyModifiers.Ctrl),
                     IsEnabled: () => inEdit() && history.CanRedo,
                     DynamicDisplayName: () => Label("Redo", history.RedoLabel, inEdit(), history.CanRedo)),
                 _ => { if (inEdit()) history.Redo(); });
        register(new EditorCommandDescriptor(RedoAltId, "Redo", "Edit", "Redo (Ctrl+Shift+Z)", null,
                     new KeyBinding(EditorKey.Z, KeyModifiers.Ctrl | KeyModifiers.Shift),
                     IsEnabled: () => inEdit() && history.CanRedo),
                 _ => { if (inEdit()) history.Redo(); });
    }
}
