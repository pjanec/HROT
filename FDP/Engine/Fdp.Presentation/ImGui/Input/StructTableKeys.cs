using System;
using System.Collections.Generic;
using Fdp.Toolkit.Input;
using NodeEditor.Core.Action;
using NodeEditor.Primitives;

namespace Fdp.Presentation.Input;

/// <summary>The commands of the struct table's list scope (docs/DESIGN_Struct_Editor.md §10.3) — they act on the table's SELECTED item.</summary>
public enum StructTableCommand { Duplicate, MoveUp, MoveDown, Delete, Copy, Paste }

/// <summary>What a struct table offers the key scope: whether a command is allowed now, and running it (the table applies it as the ⋮ menu would).</summary>
public interface IStructTableTarget
{
    bool CanRun(StructTableCommand command);
    void Run(StructTableCommand command);
}

/// <summary>
/// ⭐⭐ §10.3 (R-4163, DESIGN_Input_And_Commands §2.2) — the struct table's KEY SCOPE: a focused SUB-scope of the keymap, window kind <see cref="Kind"/>, whose commands
/// (Ctrl+D · Alt+↑ / ↓ · Del · Ctrl+C / V) act on the table's selected list item. ⛔ No <c>IsKeyPressed</c> in the table or the kit: a table only SAYS it has the focus
/// (<see cref="Set"/>, any frame a list row of it is selected in the focused window); the <see cref="KeyRouter"/> runs this scope BEFORE the focused window's, so Ctrl+D in a table
/// duplicates and outside one it falls through unchanged.
/// </summary>
public static class StructTableFocus
{
    /// <summary>The keymap's window kind of the scope.</summary>
    public const string Kind = "StructTable";

    public const string DuplicateId = "structtable.duplicate", MoveUpId = "structtable.moveup", MoveDownId = "structtable.movedown",
                        DeleteId = "structtable.delete", CopyId = "structtable.copy", PasteId = "structtable.paste";

    private static readonly (string Id, string Name, StructTableCommand Command, EditorKey Key, KeyModifiers Mods)[] Table =
    {
        (DuplicateId, "Duplicate item", StructTableCommand.Duplicate, EditorKey.D, KeyModifiers.Ctrl),
        (MoveUpId, "Move item up", StructTableCommand.MoveUp, EditorKey.Up, KeyModifiers.Alt),
        (MoveDownId, "Move item down", StructTableCommand.MoveDown, EditorKey.Down, KeyModifiers.Alt),
        (DeleteId, "Delete item", StructTableCommand.Delete, EditorKey.Delete, KeyModifiers.None),
        (CopyId, "Copy item value", StructTableCommand.Copy, EditorKey.C, KeyModifiers.Ctrl),
        (PasteId, "Paste item value", StructTableCommand.Paste, EditorKey.V, KeyModifiers.Ctrl),
    };

    private static IStructTableTarget? _target;

    /// <summary>The table that has the keyboard this frame (set while it draws; the router takes it once the frame is drawn).</summary>
    public static void Set(IStructTableTarget target) => _target = target;

    /// <summary>The router's read: this frame's table, then cleared (a table that stops saying it has the focus loses the keys the next frame).</summary>
    internal static IStructTableTarget? Take() { var t = _target; _target = null; return t; }

    /// <summary>The scope the router runs (one instance — its command list object never changes, so it is declared into the keymap once).</summary>
    public static IKeyScopeWindow Scope { get; } = new ScopeImpl();

    private sealed class ScopeImpl : IKeyScopeWindow
    {
        private readonly EditorCommandsImpl _commands = new();
        public ScopeImpl()
        {
            foreach (var (id, name, cmd, key, mods) in Table)
            {
                var command = cmd;
                _commands.Register(new EditorCommandDescriptor(id, name, "Struct table", null, null, new KeyBinding(key, mods),
                    () => _current is { } t && t.CanRun(command)), _ => _current?.Run(command));
            }
        }
        /// <summary>The target the commands act on for the dispatch in progress (set by the router).</summary>
        internal IStructTableTarget? _current;
        public string KeyScopeKind => Kind;
        public IEditorCommands? KeyCommands => _commands;
    }

    /// <summary>Makes <paramref name="target"/> the one the commands act on (the router calls it before running the scope).</summary>
    internal static void Bind(IStructTableTarget? target) => ((ScopeImpl)Scope)._current = target;

    // ── the chords, for the ⋮ menu's labels: from the keymap once a router declared the scope, else the defaults ─────────────────────────────

    /// <summary>The router's keymap (set by the first <see cref="KeyRouter"/>; process-wide like <see cref="KeyboardFocus"/>), or null.</summary>
    public static Keymap? Keymap { get; set; }

    /// <summary>The chord label of a command (<c>Ctrl+D</c>, <c>Alt+↑</c>) — read from the keymap (a remapped key shows), else the declared default.</summary>
    public static string ChordLabel(StructTableCommand command)
    {
        foreach (var (id, _, cmd, key, mods) in Table)
        {
            if (cmd != command) continue;
            var chord = new KeyBinding(key, mods);
            if (Keymap is { } map)
                for (int i = 0; i < map.Count; i++)
                {
                    var e = map[i];
                    if (e.Scope == InputScope.FocusedWindow && e.WindowKind == Kind && e.Action.IsCommand && e.Action.CommandId == id) { chord = e.Chord; break; }
                }
            return Format(chord);
        }
        return "";
    }

    private static string Format(KeyBinding c)
    {
        string key = c.Key switch { EditorKey.Up => "↑", EditorKey.Down => "↓", EditorKey.Delete => "Del", _ => c.Key.ToString() };
        string mods = ((c.Modifiers & KeyModifiers.Ctrl) != 0 ? "Ctrl+" : "") + ((c.Modifiers & KeyModifiers.Shift) != 0 ? "Shift+" : "") + ((c.Modifiers & KeyModifiers.Alt) != 0 ? "Alt+" : "");
        return mods + key;
    }
}
