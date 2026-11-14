using System.Collections.Generic;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Action;

// ⭐ B-10 K1 (docs/UX/DESIGN_Input_And_Commands.md §5 ①) — moved here from NodeEditor.Core (IEditorCommands.cs), namespace KEPT, so the one
// chord type is reachable from the keymap (Fdp.Toolkits.Input) and the action declarations without a reference to ImGui.

/// <summary>Default key binding for a command.</summary>
public readonly record struct KeyBinding(EditorKey Key, KeyModifiers Modifiers)
{
    public override string ToString()
    {
        var parts = new List<string>(4);
        if ((Modifiers & KeyModifiers.Ctrl) != 0)  parts.Add("Ctrl");
        if ((Modifiers & KeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((Modifiers & KeyModifiers.Alt) != 0)   parts.Add("Alt");
        if ((Modifiers & KeyModifiers.Super) != 0) parts.Add("Super");
        parts.Add(Key.ToString());
        return string.Join('+', parts);
    }
}
