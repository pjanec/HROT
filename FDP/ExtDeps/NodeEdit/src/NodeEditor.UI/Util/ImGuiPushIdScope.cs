using ImGuiNET;

namespace NodeEditor.UI.Util;

/// <summary>
/// RAII wrapper that calls <c>ImGui.PushID</c> on construction and
/// <c>ImGui.PopID</c> on disposal. Use with <c>using var _ = new ImGuiPushIdScope(…)</c>
/// to get stable ImGui IDs inside per-node render loops.
/// </summary>
public readonly struct ImGuiPushIdScope : IDisposable
{
    /// <summary>Push a string-based ID scope.</summary>
    public ImGuiPushIdScope(string id) => ImGui.PushID(id);

    /// <summary>Push an integer ID scope.</summary>
    public ImGuiPushIdScope(int id) => ImGui.PushID(id);

    /// <inheritdoc/>
    public void Dispose() => ImGui.PopID();
}
