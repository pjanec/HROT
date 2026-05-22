namespace NodeEditor.Primitives;

/// <summary>
/// String-keyed type identifier. The editor does not know specific types;
/// the host owns the type namespace and provides type info via
/// <c>ITypeSystem</c>. Standard convention: full CLR-style name, e.g.
/// "System.Single", "MyHost.Combat.DamageInfo".
/// </summary>
public readonly record struct TypeKey(string Id)
{
    public static TypeKey Empty => new(string.Empty);
    public bool IsEmpty => string.IsNullOrEmpty(Id);
    public override string ToString() => Id;
}
