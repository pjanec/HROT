using System;
using System.Collections.Generic;
using NodeEditor.Core.Action;
using NodeEditor.Primitives;

namespace Fdp.Toolkit.Input;

/// <summary>
/// ⭐ B-10 K4 (docs/UX/DESIGN_Input_And_Commands.md §6g) — what a <see cref="KeymapReader"/> asks of a device: a key or a mouse button (<see cref="EditorKey"/>
/// names both), a mouse VALUE (the wheel, the movement), and the modifiers. Adapters sit over ImGui, over the map's <c>IInputProvider</c> and over a rail's hand.
/// </summary>
public interface IChordInput
{
    /// <summary>The modifiers held now.</summary>
    KeyModifiers Modifiers { get; }

    /// <summary>Whether the key / mouse button is held.</summary>
    bool IsDown(EditorKey source);

    /// <summary>Whether the key / mouse button went down this frame; <paramref name="allowRepeat"/>: also on the held key's repeats (a zoom, an arrow).</summary>
    bool IsPressed(EditorKey source, bool allowRepeat = false);

    /// <summary>A mouse value this frame (<see cref="EditorKey.MouseWheel"/>, <see cref="EditorKey.MouseDeltaX"/> …); 0 for anything else.</summary>
    float Value(EditorKey source);
}

/// <summary>
/// ⭐⭐ B-10 K4 (§6g) — how a SURFACE reads its input through the keymap: ask for an ACTION, never for a key. One reader looks at one place of the scope chain
/// (<see cref="InputScope"/> + the window kind / hat / target class it was declared under), so the FPS station reads the Embodied hat's rows, the timeline its panel's,
/// the Player's map its own — and a row the user rebinds moves the effect with no code change.
/// <list type="bullet">
/// <item><see cref="Pressed"/> — a <see cref="KeyTrigger.Press"/> row's chord went down this frame.</item>
/// <item><see cref="Held"/> — a <see cref="KeyTrigger.Hold"/> row's chord is held (a chord with no key is its modifier alone).</item>
/// <item><see cref="Axis"/> — a <see cref="KeyTrigger.Axis"/> action's value: the keys' pair (any positive − any negative) plus the mouse values times their scale.</item>
/// </list>
/// ⭐ A row fires when the modifiers it NAMES are held (extra modifiers do not stop it — Shift+W still walks), and the row naming the MOST modifiers for a key wins, so
/// <c>J</c> and <c>Shift+J</c> are two different actions and Shift+J is never also J.
/// <para>⛔⛔ Hot path (R-2084): the rows of an action are found once per keymap <see cref="Keymap.Version"/> and kept; a steady frame is dictionary lookups and index loops — 0 B.</para>
/// </summary>
public sealed class KeymapReader
{
    private readonly Keymap _keymap;
    private readonly InputScope _scope;
    private readonly string? _qualifier;
    private readonly Dictionary<ActionRef, KeymapEntry[]> _rows = new();
    private int _version = -1;

    /// <param name="keymap">The keymap to read (production: <see cref="Keymap.Process"/>).</param>
    /// <param name="scope">Which scope of the chain this reader belongs to.</param>
    /// <param name="qualifier">The window kind / hat name / target class the rows were declared under; null for none.</param>
    public KeymapReader(Keymap keymap, InputScope scope, string? qualifier)
    {
        _keymap = keymap ?? throw new ArgumentNullException(nameof(keymap));
        _scope = scope;
        _qualifier = qualifier;
    }

    /// <summary>The device being read. A surface sets it each frame (or once) before asking; null reads nothing.</summary>
    public IChordInput? Input { get; set; }

    /// <summary>Whether a Press row of <paramref name="action"/> fired this frame (<paramref name="allowRepeat"/>: also on a held key's repeats).</summary>
    public bool Pressed(ActionRef action, bool allowRepeat = false) => Any(action, KeyTrigger.Press, allowRepeat);

    /// <summary>Whether a Hold row of <paramref name="action"/> is held.</summary>
    public bool Held(ActionRef action) => Any(action, KeyTrigger.Hold, false);

    /// <summary>The value of the Axis <paramref name="action"/> this frame (0 at rest).</summary>
    public float Axis(ActionRef action)
    {
        var input = Input;
        if (input == null) return 0f;
        var rows = RowsOf(action);
        var held = input.Modifiers;
        float positive = 0f, negative = 0f, mouse = 0f;
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            if (row.Trigger != KeyTrigger.Axis || !ModifiersMet(row.Chord, held)) continue;
            var key = row.Chord.Key;
            if (IsValueSource(key))
            {
                float v = input.Value(key);
                if (v != 0f && !Shadowed(row, held)) mouse += v * row.Scale;
            }
            else if (key != EditorKey.Unknown && input.IsDown(key) && !Shadowed(row, held))
            {
                if (row.Scale > 0f) positive = MathF.Max(positive, row.Scale);
                else if (row.Scale < 0f) negative = MathF.Max(negative, -row.Scale);
            }
        }
        return positive - negative + mouse;
    }

    /// <summary>
    /// The chord of the first row of <paramref name="action"/> as text (<c>L</c>, <c>Shift+J</c>, <c>Shift</c> for a modifier alone) — for a HUD hint that must follow a rebind; null when the action
    /// has no row here. ⚠ Allocates the text: call it where a string is built anyway.
    /// </summary>
    public string? ChordOf(ActionRef action)
    {
        var rows = RowsOf(action);
        if (rows.Length == 0) return null;
        var c = rows[0].Chord;
        if (c.Key != EditorKey.Unknown) return c.ToString();
        var text = c.ToString();
        return text.EndsWith("+Unknown", StringComparison.Ordinal) ? text.Substring(0, text.Length - "+Unknown".Length) : text;
    }

    private bool Any(ActionRef action, KeyTrigger trigger, bool allowRepeat)
    {
        var input = Input;
        if (input == null) return false;
        var rows = RowsOf(action);
        var held = input.Modifiers;
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            if (row.Trigger != trigger || !ModifiersMet(row.Chord, held)) continue;
            var key = row.Chord.Key;
            bool active = key == EditorKey.Unknown
                ? trigger == KeyTrigger.Hold && row.Chord.Modifiers != KeyModifiers.None          // a modifier alone is a held thing, never a press
                : trigger == KeyTrigger.Press ? input.IsPressed(key, allowRepeat) : input.IsDown(key);
            if (active && !Shadowed(row, held)) return true;
        }
        return false;
    }

    /// <summary>The modifiers the chord names are all held (extra ones do not matter).</summary>
    private static bool ModifiersMet(KeyBinding chord, KeyModifiers held) => (chord.Modifiers & ~held) == 0;

    private static bool IsValueSource(EditorKey key)
        => key == EditorKey.MouseWheel || key == EditorKey.MouseWheelH || key == EditorKey.MouseDeltaX || key == EditorKey.MouseDeltaY;

    /// <summary>Another action's row in this place names the SAME key with MORE modifiers, all held: that row owns the input (Shift+J is not also J).</summary>
    private bool Shadowed(in KeymapEntry row, KeyModifiers held)
    {
        for (int i = 0; i < _keymap.Count; i++)
        {
            var other = _keymap[i];
            if (other.Scope != _scope || other.Trigger != row.Trigger || other.Chord.Key != row.Chord.Key) continue;
            if (!string.Equals(other.WindowKind, _qualifier, StringComparison.Ordinal) || other.Action.Equals(row.Action)) continue;
            var mods = other.Chord.Modifiers;
            if ((mods & ~held) != 0 || (mods & row.Chord.Modifiers) != row.Chord.Modifiers || mods == row.Chord.Modifiers) continue;
            return true;
        }
        return false;
    }

    private KeymapEntry[] RowsOf(ActionRef action)
    {
        if (_version != _keymap.Version) { _rows.Clear(); _version = _keymap.Version; }
        if (_rows.TryGetValue(action, out var rows)) return rows;
        var list = new List<KeymapEntry>();
        for (int i = 0; i < _keymap.Count; i++)
        {
            var e = _keymap[i];
            if (e.Scope == _scope && string.Equals(e.WindowKind, _qualifier, StringComparison.Ordinal) && e.Action.Equals(action)) list.Add(e);
        }
        rows = list.ToArray();
        _rows[action] = rows;
        return rows;
    }
}

/// <summary>
/// ⭐ B-10 K4 §6g — a hand-made device for a rail or a host with no mouse: the keys / buttons held and pressed are sets the caller fills, the mouse values are fields.
/// </summary>
public sealed class ManualChordInput : IChordInput
{
    private readonly HashSet<EditorKey> _down = new();
    private readonly HashSet<EditorKey> _pressed = new();

    /// <summary>The modifiers held.</summary>
    public KeyModifiers Modifiers { get; set; }
    /// <summary>The wheel this frame.</summary>
    public float Wheel { get; set; }
    /// <summary>The horizontal wheel this frame.</summary>
    public float WheelH { get; set; }
    /// <summary>The movement this frame.</summary>
    public float DeltaX { get; set; }
    /// <summary>The movement this frame.</summary>
    public float DeltaY { get; set; }

    /// <summary>Holds <paramref name="key"/> (it reads as down until <see cref="Release"/>).</summary>
    public ManualChordInput Hold(EditorKey key) { _down.Add(key); return this; }
    /// <summary>Lets go of <paramref name="key"/>.</summary>
    public ManualChordInput Release(EditorKey key) { _down.Remove(key); return this; }
    /// <summary>Presses <paramref name="key"/> for THIS frame (held too); <see cref="NextFrame"/> clears it.</summary>
    public ManualChordInput Press(EditorKey key) { _pressed.Add(key); _down.Add(key); return this; }
    /// <summary>Ends the frame: presses are forgotten (held keys stay held).</summary>
    public void NextFrame() { _pressed.Clear(); Wheel = WheelH = DeltaX = DeltaY = 0f; }

    /// <inheritdoc/>
    public bool IsDown(EditorKey source) => _down.Contains(source);
    /// <inheritdoc/>
    public bool IsPressed(EditorKey source, bool allowRepeat = false) => _pressed.Contains(source);
    /// <inheritdoc/>
    public float Value(EditorKey source) => source switch
    {
        EditorKey.MouseWheel => Wheel, EditorKey.MouseWheelH => WheelH, EditorKey.MouseDeltaX => DeltaX, EditorKey.MouseDeltaY => DeltaY, _ => 0f,
    };
}
