namespace StructEdit.Core;

/// <summary>
/// Container for the session's instruction tree.
/// </summary>
public sealed class EditDocument
{
    public EditNode Root { get; }
    public Type RootComponentType { get; }
    public EditScope Scope { get; }

    public EditDocument(EditNode root, Type rootComponentType, EditScope scope)
    {
        Root = root;
        RootComponentType = rootComponentType;
        Scope = scope;
    }
}
