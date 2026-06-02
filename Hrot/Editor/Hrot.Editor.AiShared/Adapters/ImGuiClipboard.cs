using System;
using ImGuiNET;
using NodeEditor.Core.Interfaces;

namespace Hrot.Editor.AiShared.Adapters;

/// <summary>
/// <see cref="IClipboard"/> that delegates to the OS clipboard via
/// <c>ImGui.GetClipboardText</c> / <c>ImGui.SetClipboardText</c>.
/// Both methods are guarded against a missing ImGui context so the class
/// is safe to instantiate headless (for tests or headless environments).
/// </summary>
public sealed class ImGuiClipboard : IClipboard
{
    /// <inheritdoc/>
    /// <remarks>Returns <see langword="null"/> when no ImGui context is active.</remarks>
    public string? GetText()
    {
        // Guard before any ImGui deref — AccessViolationException is a
        // corrupted-state exception that managed try/catch cannot handle.
        if (ImGui.GetCurrentContext() == IntPtr.Zero)
            return null;

        try
        {
            return ImGui.GetClipboardText();
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc/>
    /// <remarks>No-op when no ImGui context is active.</remarks>
    public void SetText(string text)
    {
        // Guard before any ImGui deref.
        if (ImGui.GetCurrentContext() == IntPtr.Zero)
            return;

        try
        {
            ImGui.SetClipboardText(text ?? string.Empty);
        }
        catch
        {
            // Swallowed: headless environments have no clipboard backend.
        }
    }
}
