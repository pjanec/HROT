using System.Text;
using ImGuiNET;

namespace NodeEditor.Core.Widgets;

/// <summary>
/// ⭐ R-2084 — a text box that allocates NOTHING per frame: ImGui.NET's <c>InputText(ref string)</c> builds a new string every call, so a filter / type-ahead box owns a pinned UTF-8 buffer and
/// materialises <see cref="Text"/> only on the frame the user edited it. One instance per box (kept by the widget's state).
/// </summary>
public sealed class TextBuffer
{
    private readonly byte[] _buf;
    private int _len;

    public TextBuffer(int capacity = 128) => _buf = GC.AllocateArray<byte>(capacity, pinned: true);

    /// <summary>The current text (a string built when it last changed — never per frame).</summary>
    public string Text { get; private set; } = "";

    /// <summary>Replaces the text (programmatic: clear a filter, preset a value).</summary>
    public void Set(string text)
    {
        Text = text;
        _len = Encoding.UTF8.GetBytes(text, 0, Math.Min(text.Length, _buf.Length - 1), _buf, 0);
        _buf[_len] = 0;
    }

    public void Clear() { if (Text.Length > 0 || _buf[0] != 0) Set(""); }

    /// <summary>Reads the buffer into <see cref="Text"/> (a box that was just deactivated by Enter or a click away: the last keystrokes may not have been reported as an edit yet).</summary>
    public void Sync()
    {
        _len = Array.IndexOf(_buf, (byte)0);
        if (_len < 0) _len = _buf.Length;
        Text = Encoding.UTF8.GetString(_buf, 0, _len);
    }

    private string? _idKey, _hintKey;
    private byte[] _idBytes = Array.Empty<byte>(), _hintBytes = Array.Empty<byte>();

    private static byte[] Pin(string s)
    {
        var b = GC.AllocateArray<byte>(Encoding.UTF8.GetByteCount(s) + 1, pinned: true);
        Encoding.UTF8.GetBytes(s, 0, s.Length, b, 0);
        return b;
    }

    /// <summary>Draws the box (with a grey <paramref name="hint"/> while empty) at the cursor; true on the frame the text changed. <paramref name="id"/> should carry <c>##</c>.</summary>
    public unsafe bool Draw(string id, string hint, float width)
    {
        // ImGui.NET's managed overloads take `ref string` (a new string per call) — so the native call is made with ids encoded once per box.
        if (!ReferenceEquals(_idKey, id)) { _idKey = id; _idBytes = Pin(id); }
        if (!ReferenceEquals(_hintKey, hint)) { _hintKey = hint; _hintBytes = Pin(hint); }
        if (width > 0f) ImGui.SetNextItemWidth(width);
        bool edited;
        fixed (byte* label = _idBytes) fixed (byte* h = _hintBytes) fixed (byte* buf = _buf)
            edited = ImGuiNative.igInputTextWithHint(label, h, buf, (uint)_buf.Length, ImGuiInputTextFlags.None, null, null) != 0;
        if (edited)
        {
            _len = Array.IndexOf(_buf, (byte)0);
            if (_len < 0) _len = _buf.Length;
            Text = Encoding.UTF8.GetString(_buf, 0, _len);
        }
        return edited;
    }
}
