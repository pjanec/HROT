using NodeEditor.Core.Interfaces;

namespace NodeEditor.Demo.FakeBlueprint;

/// <summary>In-memory clipboard (not wired to OS clipboard for simplicity).</summary>
public sealed class FakeClipboard : IClipboard
{
    private string? _buffer;

    public string? GetText()      => _buffer;
    public void    SetText(string text) => _buffer = text;
}
