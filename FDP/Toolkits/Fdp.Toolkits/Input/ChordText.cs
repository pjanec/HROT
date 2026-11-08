using System;
using NodeEditor.Core.Action;
using NodeEditor.Primitives;

namespace Fdp.Toolkit.Input;

/// <summary>⭐ B-10 K6 — reads the chord text an action declares as its shortcut (<c>"C"</c>, <c>"Space"</c>, <c>"Ctrl+C"</c>) into the keymap's one chord type.</summary>
public static class ChordText
{
    /// <summary>Parses <paramref name="text"/>; false for text that is not a chord.</summary>
    public static bool TryParse(string? text, out KeyBinding chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var mods = KeyModifiers.None;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) mods |= KeyModifiers.Ctrl;
            else if (parts[i].Equals("Shift", StringComparison.OrdinalIgnoreCase)) mods |= KeyModifiers.Shift;
            else if (parts[i].Equals("Alt", StringComparison.OrdinalIgnoreCase)) mods |= KeyModifiers.Alt;
            else return false;
        }
        var key = parts[^1];
        if (key.Equals("Esc", StringComparison.OrdinalIgnoreCase)) key = "Escape";
        else if (key.Equals("Del", StringComparison.OrdinalIgnoreCase)) key = "Delete";
        else if (key.Length == 1 && char.IsDigit(key[0])) key = "D" + key;
        if (!Enum.TryParse<EditorKey>(key, ignoreCase: true, out var k) || k == EditorKey.Unknown) return false;
        chord = new KeyBinding(k, mods);
        return true;
    }
}
