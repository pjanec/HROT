using Fdp.Presentation.Input;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Input;
using NodeEditor.Core.Action;
using NodeEditor.Primitives;

namespace Fdp.Toolkit.Vis2D.Gizmos;

/// <summary>
/// ⭐ B-17 P6 (📄 docs/UX/DESIGN_Map_Interaction.md §2.5 "editing a shape or route") — the keys of the shape and route EDITORS, declared as rows of the ONE keymap
/// (scope Map, qualifier <see cref="Qualifier"/>) so the Keyboard shortcuts window lists them and a rebind moves them. ⛔ No gizmo compares a key: it asks
/// <see cref="ShapeEditSession.IsDeleteKey"/>. (The drawing keys of P2 — Enter, Esc, Backspace — are the session's own and stay.)
/// </summary>
public static class ShapeEditKeys
{
    /// <summary>The keymap qualifier of the editors' rows.</summary>
    public const string Qualifier = "shape-edit";

    /// <summary>Delete the selected point of the shape or route being edited.</summary>
    public const string DeletePointId = "shape.delete.point";

    /// <summary>Declares the rows into <paramref name="keymap"/> (idempotent).</summary>
    public static void Declare(Keymap keymap)
    {
        var action = ActionRef.Command(DeletePointId);
        keymap.SetLabel(action, "Delete the selected point");
        if (!keymap.IsDeclared(InputScope.Map, Qualifier, action))
            keymap.Declare(new KeymapEntry(new KeyBinding(EditorKey.Delete, KeyModifiers.None), InputScope.Map, Qualifier, action));
    }

    /// <summary>The process keymap with the rows declared.</summary>
    public static Keymap Process()
    {
        Declare(Keymap.Process);
        return Keymap.Process;
    }

    /// <summary>Does <paramref name="key"/> run <paramref name="actionId"/> of the editors' scope in <paramref name="keymap"/>? (An index loop — no allocation.)</summary>
    public static bool Is(Keymap keymap, MapKeyboardKey key, string actionId)
    {
        int code = (int)key & 0xFFFF;   // the modifier masks ride in the high bits
        for (int i = 0; i < keymap.Count; i++)
        {
            var e = keymap[i];
            if (e.Scope != InputScope.Map || e.WindowKind != Qualifier || !e.Action.IsCommand || e.Action.CommandId != actionId) continue;
            if (RaylibKeyCodes.Of(e.Chord.Key) == code) return true;
        }
        return false;
    }
}
