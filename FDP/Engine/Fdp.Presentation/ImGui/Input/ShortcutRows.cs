using System;
using System.Collections.Generic;
using Fdp.Toolkit.Input;

namespace Fdp.Presentation.Input;

/// <summary>⭐ B-10 K6 — one line of the Keyboard shortcuts window: where it applies, the chord or gesture, and what it runs.</summary>
/// <param name="Scope">Where it applies ("Everywhere", "Map · unit", "In Graph canvas", "Command hat" …).</param>
/// <param name="Binding">The chord (<c>Ctrl+Z</c>) or the gesture (<c>Shift + Left click on an entity</c>).</param>
/// <param name="Action">The action's label.</param>
/// <param name="IsGesture">A mouse gesture row (K7) rather than a key.</param>
public readonly record struct ShortcutRow(string Scope, string Binding, string Action, bool IsGesture);

/// <summary>
/// ⭐⭐ B-10 K6 (📄 docs/UX/DESIGN_Input_And_Commands.md §6f) — the Keyboard shortcuts window's MODEL: a read-only VIEW of the ONE keymap (<see cref="Keymap.Process"/>), keys AND gesture rows,
/// grouped by scope and filtered by a search text. It owns no list: a binding that is declared cannot be missing, one that is not declared cannot be shown.
/// <para>⭐ Hot path: the rows are rebuilt only when the keymap's <see cref="Keymap.Version"/> or the filter changed — a steady frame returns the cached list and allocates nothing.</para>
/// </summary>
public sealed class ShortcutRows
{
    private readonly Keymap _keymap;
    private readonly List<ShortcutRow> _rows = new();
    private int _version = -1;
    private string _filter = "";

    /// <param name="keymap">The keymap to show (production: <see cref="Keymap.Process"/>).</param>
    public ShortcutRows(Keymap keymap) => _keymap = keymap ?? throw new ArgumentNullException(nameof(keymap));

    /// <summary>The rows matching <paramref name="filter"/> (any column, case-insensitive; empty = all), in scope order.</summary>
    public IReadOnlyList<ShortcutRow> Rows(string filter)
    {
        filter ??= "";
        if (_version == _keymap.Version && string.Equals(_filter, filter, StringComparison.Ordinal)) return _rows;
        _version = _keymap.Version;
        _filter = filter;
        _rows.Clear();
        for (int i = 0; i < _keymap.Count; i++)
        {
            var e = _keymap[i];
            Add(new ShortcutRow(ScopeText(e.Scope, e.WindowKind), BindingText(e), _keymap.LabelOf(e.Action), false), e.Scope, filter);
        }
        for (int i = 0; i < _keymap.GestureCount; i++)
        {
            var g = _keymap.GestureAt(i);
            Add(new ShortcutRow(ScopeText(g.Scope, g.Qualifier), GestureText(g.Gesture), _keymap.LabelOf(g.Action), true), g.Scope, filter);
        }
        _rows.Sort(Compare);
        return _rows;
    }

    private void Add(ShortcutRow row, InputScope scope, string filter)
    {
        if (filter.Length > 0
            && row.Binding.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
            && row.Action.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
            && row.Scope.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) return;
        _rows.Add(row);
    }

    /// <summary>
    /// ⭐ B-10 K4 (§6g) — a row's chord as the user says it, with its KIND: a Press is the chord alone (<c>Ctrl+Z</c>); a Hold says <c>(hold)</c> (<c>Shift (hold)</c>, <c>Right button (hold)</c>);
    /// an Axis says what it adds (<c>W (axis +1)</c>, <c>Wheel (axis)</c>, <c>Mouse X (axis −1)</c>).
    /// </summary>
    public static string BindingText(KeymapEntry e)
    {
        string chord = ChordWords(e.Chord);
        return e.Trigger switch
        {
            KeyTrigger.Hold => chord + " (hold)",
            KeyTrigger.Axis => chord + (e.Scale == 1f ? " (axis)" : e.Scale > 0f ? " (axis +" + e.Scale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + ")"
                                                                                  : " (axis \u2212" + (-e.Scale).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + ")"),
            _ => chord,
        };
    }

    private static string ChordWords(NodeEditor.Core.Action.KeyBinding chord)
    {
        string key = chord.Key switch
        {
            NodeEditor.Primitives.EditorKey.Unknown => "",
            NodeEditor.Primitives.EditorKey.MouseLeft => "Left button",
            NodeEditor.Primitives.EditorKey.MouseRight => "Right button",
            NodeEditor.Primitives.EditorKey.MouseMiddle => "Middle button",
            NodeEditor.Primitives.EditorKey.MouseWheel => "Wheel",
            NodeEditor.Primitives.EditorKey.MouseWheelH => "Horizontal wheel",
            NodeEditor.Primitives.EditorKey.MouseDeltaX => "Mouse X",
            NodeEditor.Primitives.EditorKey.MouseDeltaY => "Mouse Y",
            NodeEditor.Primitives.EditorKey.KpAdd => "Keypad +",
            NodeEditor.Primitives.EditorKey.KpSubtract => "Keypad -",
            _ => chord.Key.ToString(),
        };
        string mods = ModifierWords(chord.Modifiers);   // Ctrl+Shift+ — the order KeyBinding.ToString uses
        return key.Length == 0 ? mods.TrimEnd('+') : mods + key;
    }

    private static string ModifierWords(NodeEditor.Primitives.KeyModifiers m)
        => ((m & NodeEditor.Primitives.KeyModifiers.Ctrl) != 0 ? "Ctrl+" : "") + ((m & NodeEditor.Primitives.KeyModifiers.Shift) != 0 ? "Shift+" : "")
         + ((m & NodeEditor.Primitives.KeyModifiers.Alt) != 0 ? "Alt+" : "") + ((m & NodeEditor.Primitives.KeyModifiers.Super) != 0 ? "Super+" : "");

    /// <summary>The scope's wording: <c>Everywhere</c> · <c>In &lt;window kind&gt;</c> · <c>Map</c> / <c>Map · unit</c> · <c>&lt;Hat&gt; hat</c>.</summary>
    public static string ScopeText(InputScope scope, string? qualifier) => scope switch
    {
        InputScope.Global => qualifier == "debug" ? "Debug keys" : "Everywhere",
        InputScope.FocusedWindow => "In " + (qualifier ?? "a window"),
        InputScope.Map => qualifier == null ? "Map" : "Map · " + qualifier,
        InputScope.Hat => (qualifier ?? "the") + " hat",
        InputScope.Modal => "In a dialog",
        _ => "Typing in a field",
    };

    // Everywhere first, then the focused windows, the map, the hats; then by binding.
    private static int Order(string scope) => scope == "Everywhere" || scope == "Debug keys" ? 0 : scope.StartsWith("In ", StringComparison.Ordinal) ? 1 : scope.StartsWith("Map", StringComparison.Ordinal) ? 2 : 3;
    private static int Compare(ShortcutRow a, ShortcutRow b)
    {
        int c = Order(a.Scope).CompareTo(Order(b.Scope));
        if (c != 0) return c;
        c = string.CompareOrdinal(a.Scope, b.Scope);
        if (c != 0) return c;
        c = a.IsGesture.CompareTo(b.IsGesture);
        return c != 0 ? c : string.CompareOrdinal(a.Binding, b.Binding);
    }

    /// <summary>A gesture as the user says it: <c>Ctrl + Right click on a hostile</c>, <c>Left drag from the ground</c>.</summary>
    public static string GestureText(MapGesture g)
    {
        string mods = (g.Modifiers & GestureModifiers.Ctrl) != 0 ? "Ctrl + " : "";
        if ((g.Modifiers & GestureModifiers.Shift) != 0) mods += "Shift + ";
        if ((g.Modifiers & GestureModifiers.Alt) != 0) mods += "Alt + ";
        string button = g.Button + (g.Kind == GestureKind.Drag ? " drag" : " click");
        return mods + button + (g.Kind == GestureKind.Drag ? " from " : " on ") + TargetText(g.Target);
    }

    private static string TargetText(GestureTarget t)
        => t == GestureTarget.Any ? "anything"
         : t == GestureTarget.AnyEntity ? "an entity"
         : t == GestureTarget.Ground ? "the ground"
         : t == GestureTarget.Selected ? "a selected entity"
         : t == GestureTarget.Hostile ? "a hostile"
         : t == (GestureTarget.Entity | GestureTarget.Hostile) ? "an unselected entity"
         : t == (GestureTarget.Ground | GestureTarget.Entity | GestureTarget.Selected) ? "the ground or a friendly"
         : t.ToString().ToLowerInvariant();
}
