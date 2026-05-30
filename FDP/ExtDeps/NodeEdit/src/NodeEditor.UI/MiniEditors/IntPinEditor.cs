using ImGuiNET;
using NodeEditor.Core.Interfaces;

namespace NodeEditor.UI.MiniEditors;

/// <summary>
/// Inline editor for <c>int</c> / <c>System.Int32</c> pins. Uses a drag-int
/// widget with expression support on double-click. Commits on mouse release.
/// </summary>
public sealed class IntPinEditor : IPinDefaultValueEditor
{
    /// <inheritdoc/>
    public bool Draw(ref object? value, DefaultEditorContext ctx, out bool committed)
    {
        int current = value is int i ? i : 0;

        ImGui.PushItemWidth(ctx.MaxWidth);
        bool changed = DragFloatWithExpression.Render("##int", ref current, out committed,
            speed: (float)(ctx.Metadata.Step ?? 1.0));
        ImGui.PopItemWidth();

        if (changed)
        {
            if (ctx.Metadata.ClampToRange && (ctx.Metadata.RangeMin.HasValue || ctx.Metadata.RangeMax.HasValue))
            {
                int lo = ctx.Metadata.RangeMin.HasValue ? (int)ctx.Metadata.RangeMin.Value : int.MinValue;
                int hi = ctx.Metadata.RangeMax.HasValue ? (int)ctx.Metadata.RangeMax.Value : int.MaxValue;
                current = Math.Clamp(current, lo, hi);
            }

            value = current;
        }

        return changed;
    }
}
