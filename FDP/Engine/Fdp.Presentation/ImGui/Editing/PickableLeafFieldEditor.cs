using System;
using StructEdit.Core;
using StructEdit.Core.Plugins;

namespace Fdp.Presentation.Editing;

/// <summary>
/// ⭐ Collapses a PICKABLE value type — <c>Fdp.Toolkit.Replication.EntityRef</c> or
/// <c>Fdp.Toolkit.Behavior.Params.PickableGeoPoint</c> — into ONE <c>Scalar</c> leaf instead of a struct with
/// member rows, so <see cref="ComponentEditDrawer"/> draws it with its map picker. The TYPE makes the field pickable.
/// 📄 <c>DESIGN_Entity_Reference.md</c> D5 · <c>DESIGN_Map_Picking_Unification.md</c> P5.
/// </summary>
public sealed class PickableLeafFieldEditor : ICustomFieldEditor
{
    public PickableLeafFieldEditor(Type targetType) => TargetType = targetType ?? throw new ArgumentNullException(nameof(targetType));

    public Type TargetType { get; }

    public EditNode? CreateNode(EditNodeId id, string name, string jsonPath, IValueBinding binding, EditNodeMetadata metadata)
        => new(id, name, jsonPath, EditNodeKind.Scalar, TargetType, binding, null, metadata);
}
