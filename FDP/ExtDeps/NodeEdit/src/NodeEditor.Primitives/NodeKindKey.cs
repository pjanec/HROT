namespace NodeEditor.Primitives;

/// <summary>
/// String-keyed node-kind identifier. The host owns the catalog of node
/// kinds; the editor only references kinds by key. Standard convention:
/// "DomainArea.NodeName", e.g. "Math.Multiply", "Control.Branch".
/// </summary>
public readonly record struct NodeKindKey(string Id)
{
    public static NodeKindKey Empty => new(string.Empty);
    public bool IsEmpty => string.IsNullOrEmpty(Id);
    public override string ToString() => Id;
}
