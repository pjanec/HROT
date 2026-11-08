using System;
using System.Collections.Generic;
using NodeEditor.Core.Action;   // KeyBinding — the ONE chord type (NodeEditor.Primitives, B-10 K1 §5 ①)

namespace Fdp.Toolkit.Input;

/// <summary>
/// ⭐ B-10 (docs/UX/DESIGN_Input_And_Commands.md §2, §5) — where a key is looked up, innermost first. The router walks them in this order and
/// the first scope with a binding for the chord wins (a focused graph canvas's Ctrl+Z shadows a global one).
/// </summary>
public enum InputScope
{
    /// <summary>A text field has the keyboard: no keymap action fires (ImGui's <c>WantTextInput</c>).</summary>
    TextField,
    /// <summary>A modal or popup is open: only its own keys (K1 declares none — the popup handles Esc itself).</summary>
    Modal,
    /// <summary>The focused window — bound per window kind (<see cref="KeymapEntry.WindowKind"/>).</summary>
    FocusedWindow,
    /// <summary>The map's active tool or hat (K2).</summary>
    Hat,
    /// <summary>The map, when it has the keyboard (K2).</summary>
    Map,
    /// <summary>Everywhere, whatever is focused (Ctrl+S).</summary>
    Global,
}

/// <summary>How a binding fires (K1 routes <see cref="Press"/> only; Hold and Axis are the map's movement keys, K2).</summary>
public enum KeyTrigger
{
    /// <summary>Once, on the frame the chord goes down (no repeat).</summary>
    Press,
    /// <summary>Every frame while held.</summary>
    Hold,
    /// <summary>A continuous value (WASD movement).</summary>
    Axis,
}

/// <summary>
/// ⭐ B-10 K1 §5 ③ — what a key runs. Today two id spaces exist: the host actions (<c>GlobalActionIds</c>, ints) and the editor commands
/// (<c>IEditorCommands</c>, strings). ⚠ Both are bound honestly until K5 folds the commands into the action catalogue (B-11).
/// </summary>
public readonly struct ActionRef : IEquatable<ActionRef>
{
    private ActionRef(int globalId, string? commandId) { GlobalId = globalId; CommandId = commandId; }

    /// <summary>A host action (<c>GlobalActionIds</c>).</summary>
    public static ActionRef Global(int id) => new(id, null);

    /// <summary>An editor command (<c>IEditorCommands</c> id).</summary>
    public static ActionRef Command(string id) => new(0, id ?? throw new ArgumentNullException(nameof(id)));

    /// <summary>The host action id, when <see cref="IsCommand"/> is false.</summary>
    public int GlobalId { get; }

    /// <summary>The command id, when <see cref="IsCommand"/> is true.</summary>
    public string? CommandId { get; }

    /// <summary>Whether this names an editor command (else a host action).</summary>
    public bool IsCommand => CommandId != null;

    /// <inheritdoc/>
    public bool Equals(ActionRef other) => GlobalId == other.GlobalId && string.Equals(CommandId, other.CommandId, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ActionRef o && Equals(o);

    /// <inheritdoc/>
    public override int GetHashCode() => CommandId != null ? StringComparer.Ordinal.GetHashCode(CommandId) : GlobalId;

    /// <inheritdoc/>
    public override string ToString() => IsCommand ? CommandId! : "action " + GlobalId;
}

/// <summary>
/// ⭐ B-10 K1 §5 ② — one row of the keymap: this chord, in this scope (and, for <see cref="InputScope.FocusedWindow"/>, this window kind),
/// runs this action. Named <c>KeymapEntry</c> because NodeEdit already owns <see cref="KeyBinding"/> for the chord itself.
/// </summary>
/// <param name="Chord">The key and its modifiers.</param>
/// <param name="Scope">Where it applies.</param>
/// <param name="WindowKind">For <see cref="InputScope.FocusedWindow"/>: the window kind it belongs to; null elsewhere.</param>
/// <param name="Action">What it runs.</param>
/// <param name="Trigger">How it fires.</param>
public readonly record struct KeymapEntry(KeyBinding Chord, InputScope Scope, string? WindowKind, ActionRef Action, KeyTrigger Trigger = KeyTrigger.Press);

/// <summary>A chord bound to two different actions where both apply at once.</summary>
/// <param name="First">The entry declared first.</param>
/// <param name="Second">The entry that collides with it.</param>
public readonly record struct KeymapConflict(KeymapEntry First, KeymapEntry Second)
{
    /// <inheritdoc/>
    public override string ToString() =>
        $"{First.Chord} in {First.Scope}{(First.WindowKind is { } k ? " (" + k + ")" : "")}: '{First.Action}' and '{Second.Action}'";
}

/// <summary>
/// ⭐⭐ B-10 K1 (docs/UX/DESIGN_Input_And_Commands.md §5) — the ONE table of which chord runs which action where. Defaults are declared in code
/// (by the commands' <c>DefaultKey</c> and, from K2, by the actions); an optional overrides file comes with K6.
/// <para>⭐ Hot path: <see cref="Count"/> / <see cref="this[int]"/> let the router walk the rows each frame with an index loop — no enumerator,
/// no allocation. A row is added once (on first declaration) and never per frame.</para>
/// </summary>
public sealed class Keymap
{
    /// <summary>
    /// ⭐ B-10 K6 (§6f) — THE keymap of this process: the ImGui <c>KeyRouter</c>, the map backend's gesture router and the Keyboard shortcuts window all read and write THIS one
    /// (⛔ SUPERSEDES one <see cref="Keymap"/> per <c>WindowManager</c> plus a separate map one).
    /// </summary>
    public static Keymap Process { get; } = new();

    private readonly List<KeymapEntry> _entries = new();
    private readonly Dictionary<ActionRef, string> _labels = new();

    /// <summary>Bumped on every change (a row, a label): a view rebuilds only when it moved.</summary>
    public int Version { get; private set; }

    /// <summary>Names <paramref name="action"/> for display (the Keyboard shortcuts window, a tooltip). Declarers call it when they declare.</summary>
    public void SetLabel(ActionRef action, string label)
    {
        if (_labels.TryGetValue(action, out var old) && string.Equals(old, label, StringComparison.Ordinal)) return;
        _labels[action] = label;
        Version++;
    }

    /// <summary>The label of <paramref name="action"/>, else its id.</summary>
    public string LabelOf(ActionRef action) => _labels.TryGetValue(action, out var l) ? l : action.ToString();
    private readonly HashSet<(InputScope, string?, ActionRef)> _declared = new();

    /// <summary>How many rows.</summary>
    public int Count => _entries.Count;

    /// <summary>The row at <paramref name="index"/>.</summary>
    public KeymapEntry this[int index] => _entries[index];

    /// <summary>
    /// Adds a row. ⭐ Idempotent per (scope, window kind, action): a second declaration of the same action in the same place is ignored, so a
    /// declarer may re-declare every time it sees a command set. Returns whether a row was added.
    /// </summary>
    public bool Declare(KeymapEntry entry)
    {
        if (!_declared.Add((entry.Scope, entry.WindowKind, entry.Action))) return false;
        _entries.Add(entry);
        Version++;
        return true;
    }

    // ── ⭐ B-10 K7 (§6b) — the mouse gestures: rows of the SAME keymap, in the same scope chain ────────────────────────────
    private readonly List<GestureEntry> _gestures = new();
    private readonly HashSet<(InputScope, string?, MapGesture)> _gestureDeclared = new();

    /// <summary>How many gesture rows.</summary>
    public int GestureCount => _gestures.Count;

    /// <summary>The gesture row at <paramref name="index"/>.</summary>
    public GestureEntry GestureAt(int index) => _gestures[index];

    /// <summary>Adds a gesture row; idempotent per (scope, qualifier, gesture). Returns whether a row was added.</summary>
    public bool DeclareGesture(GestureEntry entry)
    {
        if (!_gestureDeclared.Add((entry.Scope, entry.Qualifier, entry.Gesture))) return false;
        _gestures.Add(entry);
        Version++;
        return true;
    }

    /// <summary>
    /// The row of (<paramref name="scope"/>, <paramref name="qualifier"/>) that answers <paramref name="actual"/> (its <see cref="MapGesture.Target"/> the ONE thing hit):
    /// same button and kind, every modifier the row names held, the hit target inside the row's mask — the row naming the MOST modifiers wins (Shift+click
    /// beats click). An index loop, no allocation.
    /// </summary>
    public bool TryFindGesture(InputScope scope, string? qualifier, in MapGesture actual, out GestureEntry entry)
    {
        entry = default;
        int bestMods = -1;
        for (int i = 0; i < _gestures.Count; i++)
        {
            var e = _gestures[i];
            if (e.Scope != scope || e.Gesture.Button != actual.Button || e.Gesture.Kind != actual.Kind) continue;
            if (!string.Equals(e.Qualifier, qualifier, StringComparison.Ordinal)) continue;
            if ((e.Gesture.Target & actual.Target) == 0) continue;
            var need = e.Gesture.Modifiers;
            if ((need & ~actual.Modifiers) != 0) continue;
            int n = ((int)need & 1) + (((int)need >> 1) & 1) + (((int)need >> 2) & 1);
            if (n > bestMods) { bestMods = n; entry = e; }
        }
        return bestMods >= 0;
    }

    /// <summary>⭐ §6b — every gesture bound to two DIFFERENT actions where both apply at once: the same scope and qualifier, button, kind and modifiers, with overlapping target masks.</summary>
    public IReadOnlyList<GestureConflict> ValidateGestures()
    {
        var conflicts = new List<GestureConflict>();
        for (int i = 0; i < _gestures.Count; i++)
            for (int j = i + 1; j < _gestures.Count; j++)
            {
                var a = _gestures[i];
                var b = _gestures[j];
                if (a.Scope == b.Scope && string.Equals(a.Qualifier, b.Qualifier, StringComparison.Ordinal)
                    && a.Gesture.Button == b.Gesture.Button && a.Gesture.Kind == b.Gesture.Kind && a.Gesture.Modifiers == b.Gesture.Modifiers
                    && (a.Gesture.Target & b.Gesture.Target) != 0 && !a.Action.Equals(b.Action))
                    conflicts.Add(new GestureConflict(a, b));
            }
        return conflicts;
    }

    /// <summary>Whether (scope, window kind, action) already has a row — lets a per-frame declarer skip the work without allocating.</summary>
    public bool IsDeclared(InputScope scope, string? windowKind, ActionRef action) => _declared.Contains((scope, windowKind, action));

    /// <summary>
    /// The first row in <paramref name="scope"/> (and, for <see cref="InputScope.FocusedWindow"/>, <paramref name="windowKind"/>) whose chord is
    /// <paramref name="chord"/>. An index loop — no allocation.
    /// </summary>
    public bool TryFind(InputScope scope, string? windowKind, KeyBinding chord, out KeymapEntry entry)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            if (e.Scope == scope && e.Chord == chord && string.Equals(e.WindowKind, windowKind, StringComparison.Ordinal))
            {
                entry = e;
                return true;
            }
        }
        entry = default;
        return false;
    }

    /// <summary>
    /// ⭐ §5 acceptance ⑤ — every chord bound to two DIFFERENT actions where both apply at once: the same scope and the same window kind.
    /// ⚠ The same chord in two scopes is not a conflict — the inner scope shadowing the outer is the scope chain's whole point.
    /// </summary>
    public IReadOnlyList<KeymapConflict> Validate()
    {
        var conflicts = new List<KeymapConflict>();
        for (int i = 0; i < _entries.Count; i++)
            for (int j = i + 1; j < _entries.Count; j++)
            {
                var a = _entries[i];
                var b = _entries[j];
                if (a.Chord == b.Chord && a.Scope == b.Scope && a.Trigger == b.Trigger
                    && string.Equals(a.WindowKind, b.WindowKind, StringComparison.Ordinal) && !a.Action.Equals(b.Action))
                    conflicts.Add(new KeymapConflict(a, b));
            }
        return conflicts;
    }
}
