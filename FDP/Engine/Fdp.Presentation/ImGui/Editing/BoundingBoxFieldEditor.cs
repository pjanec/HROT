using System;
using Fdp.Toolkit.ReplayBrowser.Search;
using StructEdit.Core;
using StructEdit.Core.Plugins;

namespace Fdp.Presentation.Editing;

public sealed class BoundingBoxFieldEditor : ICustomFieldEditor
{
    public Type TargetType => typeof(BoundingBox2D);

    public EditNode? CreateNode(
        EditNodeId id,
        string name,
        string jsonPath,
        IValueBinding binding,
        EditNodeMetadata metadata)
    {
        return new EditNode(
            id,
            name,
            jsonPath,
            EditNodeKind.Custom,
            typeof(BoundingBox2D),
            binding,
            null,
            metadata);
    }
}
