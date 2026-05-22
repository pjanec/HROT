using ImGuiNET;
using NodeEditor.Core.Interfaces;

namespace NodeEditor.UI.MiniEditors;

/// <summary>
/// Inline editor for <c>float</c> / <c>System.Single</c> pins. Uses a drag-float
/// widget with expression support on double-click. Commits on mouse release.
/// Respects <see cref="PinDefaultMetadata.Units"/> as a suffix label when set.
/// </summary>
public sealed class FloatPinEditor : IPinDefaultValueEditor
{
    /// <inheritdoc/>
    public bool Draw(ref object? value, DefaultEditorContext ctx, out bool committed)
    {
        committed = false;
        float current = value is float f ? f : 0f;

        float speed = (float)(ctx.Metadata.Step ?? 0.01);
        string format = ctx.Metadata.Units is { } u ? $"%.3f {u}" : "%.3f";

        ImGui.PushItemWidth(ctx.MaxWidth);
        bool changed = DragFloatWithExpression.Render("##float", ref current, speed, format);
        ImGui.PopItemWidth();

        if (changed)
        {
            if (ctx.Metadata.ClampToRange && (ctx.Metadata.RangeMin.HasValue || ctx.Metadata.RangeMax.HasValue))
            {
                float lo = ctx.Metadata.RangeMin.HasValue ? (float)ctx.Metadata.RangeMin.Value : float.MinValue;
                float hi = ctx.Metadata.RangeMax.HasValue ? (float)ctx.Metadata.RangeMax.Value : float.MaxValue;
                current = Math.Clamp(current, lo, hi);
            }

            value = current;
            committed = ImGui.IsItemDeactivated();
            return true;
        }

        return false;
    }
}
