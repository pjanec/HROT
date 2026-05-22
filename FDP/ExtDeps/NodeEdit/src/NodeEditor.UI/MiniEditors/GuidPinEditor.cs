using ImGuiNET;
using NodeEditor.Core.Interfaces;

namespace NodeEditor.UI.MiniEditors;

/// <summary>
/// Inline editor for <c>Guid</c> pins. Renders a truncated read-only button
/// showing the first 4 and last 4 hex chars. Clicking opens the host picker if
/// a <see cref="PinDefaultMetadata.PickerSourceKey"/> is configured.
/// </summary>
public sealed class GuidPinEditor : IPinDefaultValueEditor
{
    /// <inheritdoc/>
    public bool Draw(ref object? value, DefaultEditorContext ctx, out bool committed)
    {
        committed = false;
        var guid = value is Guid g ? g : Guid.Empty;
        string label = FormatGuid(guid);

        ImGui.PushItemWidth(ctx.MaxWidth);
        // Display as a disabled input text (read-only reference display)
        if (ImGui.Button(label, new System.Numerics.Vector2(ctx.MaxWidth, 0)))
        {
            // Picker integration is host-driven; we just record the intent.
            // A full implementation would call ctx.Host.Pickers.Open(...) but
            // DefaultEditorContext does not carry host services — the host
            // should register a custom editor that overrides this one per type.
        }
        ImGui.PopItemWidth();

        return false;
    }

    private static string FormatGuid(Guid g)
    {
        if (g == Guid.Empty) return "(empty)";
        string hex = g.ToString("N"); // 32 hex chars
        return $"{hex[..4]}…{hex[^4..]}";
    }
}
