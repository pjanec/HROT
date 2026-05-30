namespace Hrot.Utility.Editor.FieldEdit;

using Fdp.Presentation.Editing;
using Fdp.Toolkit.Utility;
using Hrot.Utility.Editor.Curve;
using StructEdit.Core;

/// <summary>
/// IImGuiFieldDrawer that renders a UtilityCurve using the host-agnostic CurveWidget.
/// Follows the QuaternionEulerFieldDrawer pattern.
/// Returns true when the user changes the curve this frame, so StructEdit marks
/// the session dirty and the tuning console commit path enqueues the change.
/// </summary>
public sealed class UtilityCurveFieldDrawer : IImGuiFieldDrawer
{
    public Type TargetType => typeof(UtilityCurve);

    public bool DrawInput(ref object value, EditNode node)
    {
        var curve = value is UtilityCurve c ? c : default;
        bool changed = CurveWidget.Draw(node.JsonPath, ref curve, CurveWidgetOptions.Default);
        if (changed) value = curve;
        return changed;
    }
}
