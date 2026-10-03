using System;
using Fdp.Toolkit.Replication;
using StructEdit.Core;
using StructEdit.Core.Plugins;

namespace Fdp.Presentation.Editing;

/// <summary>
/// ⭐ <c>DESIGN_Entity_Reference.md</c> D5 — collapses an <see cref="EntityRef"/> into ONE leaf (its network id) instead of a
/// struct with a read-only field, so <see cref="ComponentEditDrawer"/> can draw it with an entity picker. The TYPE makes
/// the field pickable; <c>[MapPickableEntity]</c> only narrows the pick.
/// </summary>
public sealed class EntityRefFieldEditor : ICustomFieldEditor
{
    public Type TargetType => typeof(EntityRef);

    public EditNode? CreateNode(EditNodeId id, string name, string jsonPath, IValueBinding binding, EditNodeMetadata metadata)
        => new(id, name, jsonPath, EditNodeKind.Scalar, typeof(EntityRef), binding, null, metadata);
}
