using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Interfaces;

namespace NodeEditor.UI.MiniEditors;

/// <summary>
/// Inline editor for <c>Vector2</c>, <c>Vector3</c>, and <c>Vector4</c> pins.
/// Renders N drag-float fields packed horizontally; each field uses
/// <see cref="DragFloatWithExpression"/> for expression-aware text editing.
/// </summary>
public sealed class VectorPinEditor : IPinDefaultValueEditor
{
    private readonly int _dimension; // 2, 3, or 4

    /// <param name="dimension">Number of components: 2, 3, or 4.</param>
    public VectorPinEditor(int dimension)
    {
        if (dimension is < 2 or > 4)
            throw new ArgumentOutOfRangeException(nameof(dimension), "Dimension must be 2, 3, or 4.");
        _dimension = dimension;
    }

    /// <inheritdoc/>
    public bool Draw(ref object? value, DefaultEditorContext ctx, out bool committed)
    {
        committed = false;
        var v = Decode(value);
        bool changed = false;
        float fieldWidth = ctx.MaxWidth / _dimension - 2f;

        ImGui.PushItemWidth(fieldWidth);
        if (DragFloatWithExpression.Render("##x", ref v.X)) changed = true;
        committed |= ImGui.IsItemDeactivated();
        ImGui.SameLine(0f, 2f);

        if (DragFloatWithExpression.Render("##y", ref v.Y)) changed = true;
        committed |= ImGui.IsItemDeactivated();

        if (_dimension >= 3)
        {
            ImGui.SameLine(0f, 2f);
            if (DragFloatWithExpression.Render("##z", ref v.Z)) changed = true;
            committed |= ImGui.IsItemDeactivated();
        }

        if (_dimension >= 4)
        {
            ImGui.SameLine(0f, 2f);
            if (DragFloatWithExpression.Render("##w", ref v.W)) changed = true;
            committed |= ImGui.IsItemDeactivated();
        }

        ImGui.PopItemWidth();

        if (changed) value = Encode(v);
        return changed;
    }

    private Vector4 Decode(object? v) => _dimension switch
    {
        2 => v is Vector2 v2 ? new Vector4(v2.X, v2.Y, 0f, 0f) : default,
        3 => v is Vector3 v3 ? new Vector4(v3.X, v3.Y, v3.Z, 0f) : default,
        4 => v is Vector4 v4 ? v4 : default,
        _ => default,
    };

    private object Encode(Vector4 v) => _dimension switch
    {
        2 => new Vector2(v.X, v.Y),
        3 => new Vector3(v.X, v.Y, v.Z),
        4 => v,
        _ => v,
    };
}
