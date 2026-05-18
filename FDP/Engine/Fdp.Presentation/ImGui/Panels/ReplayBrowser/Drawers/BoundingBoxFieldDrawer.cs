using System;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.ReplayBrowser.Search;
using ImGuiNET;
using StructEdit.Core;

namespace Fdp.Presentation.Panels.ReplayBrowser.Drawers;

using ImGuiApi = ImGuiNET.ImGui;

/// <summary>
/// Custom <see cref="IImGuiFieldDrawer"/> for <see cref="BoundingBox2D"/> fields.
/// Renders two DragFloat2 controls for Min and Max. The "Pick Area" button is
/// handled by <see cref="ComponentEditDrawer"/> via <see cref="ISpatialPickerContext"/>
/// when the field carries <see cref="MapPickableBoundingBoxAttribute"/>.
/// </summary>
internal sealed class BoundingBoxFieldDrawer : IImGuiFieldDrawer
{
    public Type TargetType => typeof(BoundingBox2D);

    public bool DrawInput(ref object value, EditNode node)
    {
        var box  = value is BoundingBox2D b ? b : default;
        var min  = box.Min;
        var max  = box.Max;
        bool changed = false;

        float inputWidth = ImGuiApi.GetContentRegionAvail().X - 140f;
        if (inputWidth < 60f) inputWidth = 60f;

        ImGuiApi.SetNextItemWidth(inputWidth);
        if (ImGuiApi.DragFloat2("Min##bbox", ref min, 0.5f))
        {
            box     = new BoundingBox2D { Min = min, Max = box.Max };
            value   = box;
            changed = true;
        }

        ImGuiApi.SetNextItemWidth(inputWidth);
        if (ImGuiApi.DragFloat2("Max##bbox", ref max, 0.5f))
        {
            box     = new BoundingBox2D { Min = box.Min, Max = max };
            value   = box;
            changed = true;
        }

        return changed;
    }
}
