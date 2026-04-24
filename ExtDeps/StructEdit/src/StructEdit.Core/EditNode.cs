using System.Collections.Immutable;

namespace StructEdit.Core;

/// <summary>
/// Immutable descriptor of one editable unit in the component editor tree.
/// </summary>
public sealed class EditNode
{
    public EditNodeId Id { get; }
    public string Name { get; }
    public string JsonPath { get; }
    public EditNodeKind Kind { get; }
    public Type ClrType { get; }
    public IValueBinding? Binding { get; }
    public IReadOnlyList<EditNode> Children { get; }
    public EditNodeMetadata Metadata { get; }
    public bool IsReadOnly { get; }

    public EditNode(
        EditNodeId id,
        string name,
        string jsonPath,
        EditNodeKind kind,
        Type clrType,
        IValueBinding? binding = null,
        IReadOnlyList<EditNode>? children = null,
        EditNodeMetadata? metadata = null,
        bool isReadOnly = false)
    {
        Id = id;
        Name = name;
        JsonPath = jsonPath;
        Kind = kind;
        ClrType = clrType;
        Binding = binding;
        Children = children ?? ImmutableList<EditNode>.Empty;
        Metadata = metadata ?? EditNodeMetadata.Empty;
        IsReadOnly = isReadOnly;
    }
}
