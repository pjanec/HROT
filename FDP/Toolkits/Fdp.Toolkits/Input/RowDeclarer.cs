using System.Collections.Generic;
using NodeEditor.Core.Action;
using NodeEditor.Primitives;

namespace Fdp.Toolkit.Input;

/// <summary>
/// ⭐ B-10 K4 (docs/UX/DESIGN_Input_And_Commands.md §6g) — how a surface declares its Press / Hold / Axis rows into ONE place of the keymap (a scope and its qualifier), in one pass.
/// ⭐ An action that is already declared there — by an earlier pass, or REBOUND (<see cref="Keymap.Unbind"/> keeps the action declared) — keeps what it has, so the defaults never come
/// back over a rebind and a surface may declare on every start. A pass holds the action's several chords together (W and S are one Axis; Q and Space both climb).
/// </summary>
public sealed class RowDeclarer
{
    private readonly Keymap _keymap;
    private readonly InputScope _scope;
    private readonly string? _qualifier;
    private readonly Dictionary<ActionRef, bool> _owned = new();

    /// <param name="keymap">Where the rows go.</param>
    /// <param name="scope">The scope they belong to.</param>
    /// <param name="qualifier">The window kind / hat / target class; null for none.</param>
    public RowDeclarer(Keymap keymap, InputScope scope, string? qualifier)
    {
        _keymap = keymap;
        _scope = scope;
        _qualifier = qualifier;
    }

    private bool Free(ActionRef action)
    {
        if (!_owned.TryGetValue(action, out var skip)) _owned[action] = skip = _keymap.IsDeclared(_scope, _qualifier, action);
        return !skip;
    }

    private void Row(KeyBinding chord, ActionRef action, string label, KeyTrigger trigger, float scale)
    {
        _keymap.SetLabel(action, label);
        if (Free(action)) _keymap.Declare(new KeymapEntry(chord, _scope, _qualifier, action, trigger, scale));
    }

    /// <summary>A one-shot row.</summary>
    public RowDeclarer Press(EditorKey key, ActionRef action, string label, KeyModifiers mods = KeyModifiers.None)
    { Row(new KeyBinding(key, mods), action, label, KeyTrigger.Press, 1f); return this; }

    /// <summary>A held row (<paramref name="key"/> <see cref="EditorKey.Unknown"/> + modifiers: the modifier alone).</summary>
    public RowDeclarer Hold(EditorKey key, ActionRef action, string label, KeyModifiers mods = KeyModifiers.None)
    { Row(new KeyBinding(key, mods), action, label, KeyTrigger.Hold, 1f); return this; }

    /// <summary>A value row: a key adds <paramref name="scale"/> while down; a mouse source is multiplied by it.</summary>
    public RowDeclarer Axis(EditorKey key, ActionRef action, string label, float scale = 1f, KeyModifiers mods = KeyModifiers.None)
    { Row(new KeyBinding(key, mods), action, label, KeyTrigger.Axis, scale); return this; }
}
