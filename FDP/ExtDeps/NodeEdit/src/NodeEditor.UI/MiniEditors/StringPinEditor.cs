using ImGuiNET;
using NodeEditor.Core.Interfaces;

namespace NodeEditor.UI.MiniEditors;

/// <summary>
/// Inline editor for <c>string</c> / <c>System.String</c> pins.
/// Single-line by default; commits on Enter or focus-out; Escape reverts.
/// </summary>
public sealed class StringPinEditor : IPinDefaultValueEditor
{
    /// <inheritdoc/>
    public bool Draw(ref object? value, DefaultEditorContext ctx, out bool committed)
    {
        committed = false;
        string current = value is string s ? s : "";

        ImGui.PushItemWidth(ctx.MaxWidth);

        string? placeholder = ctx.Metadata.PlaceholderText;
        ImGuiInputTextFlags flags = ImGuiInputTextFlags.EnterReturnsTrue;
        if (ctx.IsReadOnly) flags |= ImGuiInputTextFlags.ReadOnly;

        // Show placeholder hint (ImGui doesn't have native placeholder; we overlay it)
        bool inputActive = false;
        if (placeholder != null && current.Length == 0 && !ImGui.IsItemActive())
        {
            // Draw placeholder text in muted color, then render the actual input
            var gray = ImGui.GetColorU32(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1f));
            var pos = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddText(pos + new System.Numerics.Vector2(3, 3), gray, placeholder);
        }

        bool entered = ImGui.InputText("##str", ref current, 1024, flags);
        inputActive = ImGui.IsItemActive();
        bool deactivated = ImGui.IsItemDeactivated();
        bool escaped = ImGui.IsKeyPressed(ImGuiKey.Escape) && inputActive;

        ImGui.PopItemWidth();

        if (escaped)
        {
            // Revert — caller retains old value, we don't update
            return false;
        }

        if (entered || deactivated)
        {
            value = current;
            committed = true;
            return true;
        }

        return false;
    }
}
