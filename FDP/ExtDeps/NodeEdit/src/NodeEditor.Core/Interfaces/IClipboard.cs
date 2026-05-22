namespace NodeEditor.Core.Interfaces;

/// <summary>Editor's clipboard abstraction. Wraps either OS clipboard or in-process buffer.</summary>
public interface IClipboard
{
    /// <summary>Read clipboard text.</summary>
    string? GetText();

    /// <summary>Write clipboard text.</summary>
    void SetText(string text);
}
