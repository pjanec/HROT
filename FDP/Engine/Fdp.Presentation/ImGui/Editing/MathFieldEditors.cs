using System;
using System.Numerics;
using StructEdit.Core;
using StructEdit.Core.Plugins;

namespace Fdp.Presentation.Editing;

/// <summary>⭐ S0 item 6 — one editor for a body rotation, whichever type stores it: a <see cref="Quaternion"/> or a
/// <see cref="Fdp.Core.WorldRotation"/> (the same four floats; <see cref="Fdp.Core.SemanticValueTypes"/> is the one mapping).</summary>
public sealed class QuaternionEulerFieldEditor : ICustomFieldEditor
{
    public QuaternionEulerFieldEditor() : this(typeof(Quaternion)) { }
    public QuaternionEulerFieldEditor(Type target) { TargetType = target; }

    public Type TargetType { get; }

    public EditNode? CreateNode(
        EditNodeId id,
        string name,
        string jsonPath,
        IValueBinding binding,
        EditNodeMetadata metadata)
    {
        bool isSpatialRotation = name == "Rotation" || name == "LastRotation";
        if (!isSpatialRotation )  // for other fields we want default struct editor which shows the raw x,y,z,w
			return null;

        return new EditNode(id, name, jsonPath, EditNodeKind.Custom, TargetType, binding, null, metadata);
    }
}
