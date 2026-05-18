using System;
using System.Numerics;
using StructEdit.Core;
using StructEdit.Core.Plugins;

namespace Fdp.Presentation.Editing;

public sealed class QuaternionEulerFieldEditor : ICustomFieldEditor
{
    public Type TargetType => typeof(Quaternion);

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

        return new EditNode(id, name, jsonPath, EditNodeKind.Custom, typeof(Quaternion), binding, null, metadata);
    }
}
