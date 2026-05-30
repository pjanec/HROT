namespace Hrot.Utility.Editor.FieldEdit;

using Fdp.Toolkit.Utility;
using StructEdit.Core;
using StructEdit.Core.Plugins;

/// <summary>
/// ICustomFieldEditor that collapses UtilityCurve into a single EditNodeKind.Custom node.
/// Follows the GuidFieldEditor pattern. The Presentation-layer drawer
/// (UtilityCurveFieldDrawer) owns all rendering.
/// </summary>
public sealed class UtilityCurveFieldEditor : ICustomFieldEditor
{
    public Type TargetType => typeof(UtilityCurve);

    public EditNode? CreateNode(
        EditNodeId id, string name, string jsonPath,
        IValueBinding binding, EditNodeMetadata metadata)
        => new EditNode(id, name, jsonPath, EditNodeKind.Custom,
                        typeof(UtilityCurve), binding, null, metadata);
}
