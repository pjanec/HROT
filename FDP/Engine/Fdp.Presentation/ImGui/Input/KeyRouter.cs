using System;
using System.Collections.Generic;
using Fdp.Toolkit.Input;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;

namespace Fdp.Presentation.Input;

/// <summary>
/// ⭐ B-10 K1 (docs/UX/DESIGN_Input_And_Commands.md §5 ⑤) — a window whose own commands answer keys WHILE IT IS FOCUSED (a graph canvas).
/// The router declares the commands' <see cref="EditorCommandDescriptor.DefaultKey"/>s into the keymap under <see cref="KeyScopeKind"/> and
/// invokes them only when this window holds ImGui's focus — ⛔ never because it was merely drawn (CE-4282).
/// </summary>
public interface IKeyScopeWindow
{
    /// <summary>The keymap's window kind for this window's bindings (one per window — two canvases of one asset kind share it).</summary>
    string KeyScopeKind { get; }

    /// <summary>The commands the keys run now (the active document's), or null when there are none.</summary>
    IEditorCommands? KeyCommands { get; }
}

/// <summary>
/// ⭐ B-10 K1 §5 ⑥ — whether the MAP has the keyboard. ⭐ Process-wide on purpose: it is the complement of ImGui's own focus, and ImGui's context
/// is process-wide too. Set by the <see cref="KeyRouter"/> once a frame (a click on the map with no ImGui window under the mouse takes it; a
/// click on any window gives it back); read by the map's input provider (<c>RaylibInputProvider.IsKeyboardCaptured</c>).
/// <para>Starts TRUE so a host that has drawn no frame yet keeps today's behaviour (the map has the keys).</para>
/// </summary>
public static class KeyboardFocus
{
    /// <summary>Whether the map has the keyboard.</summary>
    public static bool MapHasKeyboard { get; set; } = true;
}

/// <summary>What the router needs to know about one frame (read from ImGui in production; built by hand in rails).</summary>
/// <param name="TextInput">A text field has the keyboard (<c>io.WantTextInput</c>).</param>
/// <param name="PopupOpen">A popup or modal is open.</param>
/// <param name="Focused">The focused window when it answers keys, else null.</param>
/// <param name="MouseClicked">A mouse button went down this frame.</param>
/// <param name="ClickOnUi">…and it landed on an ImGui window (<c>io.WantCaptureMouse</c>) rather than on the map.</param>
public readonly record struct KeyFrame(bool TextInput, bool PopupOpen, IKeyScopeWindow? Focused, bool MouseClicked = false, bool ClickOnUi = false);

/// <summary>
/// ⭐⭐ B-10 K1 (docs/UX/DESIGN_Input_And_Commands.md §5) — the ONE place a key press becomes an action. Owned by the
/// <see cref="WindowManager.WindowManager"/>, so every host has it by construction, and run once a frame after every window drew.
/// <para>The scope chain, innermost first: text field (nothing fires) → popup (nothing fires; it handles its own Esc) → the FOCUSED window's
/// commands → Global (the shell's commands — Ctrl+S from anywhere). The map and its hats (K2) read the keyboard only while
/// <see cref="KeyboardFocus.MapHasKeyboard"/>.</para>
/// <para>⛔⛔ Hot path (R-2084): no LINQ, no allocation in a steady frame — index loops over the keymap and the command lists; a command set is
/// (re)declared only when its list object changes.</para>
/// </summary>
public sealed class KeyRouter
{
    private readonly IInputSource _input;
    private readonly List<KeyBinding> _consumed = new();   // chords a scope already took this frame (reused, never shrunk)
    private object? _lastGlobalAll;
    private readonly Dictionary<string, object> _lastWindowAll = new(StringComparer.Ordinal);

    /// <summary>Builds a router reading keys from <paramref name="input"/>.</summary>
    public KeyRouter(IInputSource input, Keymap? keymap = null)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        Keymap = keymap ?? new Keymap();
    }

    /// <summary>The keymap — every binding the router knows.</summary>
    public Keymap Keymap { get; }

    /// <summary>The Global scope's commands (the window manager passes its shell commands).</summary>
    public IEditorCommands? GlobalCommands { get; set; }

    /// <summary>
    /// The sink for <see cref="ActionRef.Global"/> bindings (host actions). ⚠ K1 declares none — the map's letters arrive with K2, which wires
    /// this to the host's <c>HostActions</c>.
    /// </summary>
    public Func<int, bool>? GlobalActions { get; set; }

    /// <summary>How many actions the last frame ran (diagnostics and rails).</summary>
    public int FiredLastFrame { get; private set; }

    /// <summary>
    /// One frame: updates the map's keyboard focus from this frame's click, then runs at most one action per pressed chord, innermost scope
    /// first. Call once a frame, after every window drew (so <see cref="KeyFrame.Focused"/> is this frame's).
    /// </summary>
    public void Dispatch(in KeyFrame frame)
    {
        FiredLastFrame = 0;
        if (frame.MouseClicked) KeyboardFocus.MapHasKeyboard = !frame.ClickOnUi;

        if (frame.Focused is { KeyCommands: { } own } focused) DeclareIfChanged(InputScope.FocusedWindow, focused.KeyScopeKind, own);
        if (GlobalCommands is { } global && !ReferenceEquals(global.All, _lastGlobalAll))
        {
            _lastGlobalAll = global.All;
            DeclareAll(InputScope.Global, null, global);
        }

        if (frame.TextInput || frame.PopupOpen) return;

        _consumed.Clear();
        var mods = _input.Modifiers;
        if (frame.Focused is { KeyCommands: { } commands } window)
            RunScope(InputScope.FocusedWindow, window.KeyScopeKind, commands, mods);
        if (GlobalCommands is { } shell)
            RunScope(InputScope.Global, null, shell, mods);
    }

    private void RunScope(InputScope scope, string? kind, IEditorCommands commands, NodeEditor.Primitives.KeyModifiers mods)
    {
        var map = Keymap;
        for (int i = 0; i < map.Count; i++)
        {
            var row = map[i];
            if (row.Scope != scope || row.Trigger != KeyTrigger.Press || row.Chord.Modifiers != mods) continue;
            if (!string.Equals(row.WindowKind, kind, StringComparison.Ordinal)) continue;
            if (IsConsumed(row.Chord) || !_input.IsKeyPressed(row.Chord.Key, allowRepeat: false)) continue;
            if (!Run(row.Action, commands)) continue;
            _consumed.Add(row.Chord);
            FiredLastFrame++;
        }
    }

    private bool Run(ActionRef action, IEditorCommands commands)
    {
        if (!action.IsCommand) return GlobalActions?.Invoke(action.GlobalId) ?? false;
        if (commands.Get(action.CommandId!) is not { } desc) return false;   // this document's set lacks it: the outer scope may have it
        if (!desc.IsEnabled()) return false;                                   // bound here but off: the first ENABLED binding wins (an outer one may fire)
        commands.Invoke(desc.Id, null);
        return true;
    }

    private bool IsConsumed(KeyBinding chord)
    {
        for (int i = 0; i < _consumed.Count; i++)
            if (_consumed[i] == chord) return true;
        return false;
    }

    private void DeclareIfChanged(InputScope scope, string kind, IEditorCommands commands)
    {
        var all = commands.All;
        if (_lastWindowAll.TryGetValue(kind, out var last) && ReferenceEquals(last, all)) return;
        _lastWindowAll[kind] = all;
        DeclareAll(scope, kind, commands);
    }

    private void DeclareAll(InputScope scope, string? kind, IEditorCommands commands)
    {
        var all = commands.All;
        for (int i = 0; i < all.Count; i++)
        {
            var d = all[i];
            if (d.DefaultKey is not { } chord) continue;
            var action = ActionRef.Command(d.Id);
            if (!Keymap.IsDeclared(scope, kind, action))
                Keymap.Declare(new KeymapEntry(chord, scope, kind, action));
        }
    }
}
