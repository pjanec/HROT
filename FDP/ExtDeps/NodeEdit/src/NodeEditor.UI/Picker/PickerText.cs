using ImGuiNET;

namespace NodeEditor.UI.Picker;

/// <summary>
/// ⭐ CE-4141 — text for a picker pane that the CURRENT font can actually draw. A scenario's description may carry emoji (<c>📄</c>) or other characters the editor font has no
/// glyph for, and ImGui draws those as <c>?</c>. <see cref="Drawable(string, Func{char, bool})"/> drops every character the font cannot draw — surrogate pairs (all emoji) first of all —
/// and keeps whitespace and line breaks. The predicate form is pure (the rail's); <see cref="Drawable(string)"/> asks the current ImGui font.
/// </summary>
public static class PickerText
{
    /// <summary><paramref name="text"/> without the characters <paramref name="hasGlyph"/> rejects and without any surrogate (an emoji or other astral character).
    /// Space, tab and line breaks always stay. A run of dropped characters that leaves two spaces behind is squeezed to one.</summary>
    public static string Drawable(string text, Func<char, bool> hasGlyph)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (char.IsSurrogate(c)) continue;
            if (c is ' ' or '\t' or '\n' or '\r' || hasGlyph(c))
            {
                if (c == ' ' && sb.Length > 0 && sb[^1] == ' ') continue;
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary><see cref="Drawable(string, Func{char, bool})"/> against the font in use now (call inside an ImGui frame).</summary>
    public static unsafe string Drawable(string text)
    {
        var font = ImGui.GetFont();
        return Drawable(text, c => font.FindGlyphNoFallback(c).NativePtr != null);
    }
}
