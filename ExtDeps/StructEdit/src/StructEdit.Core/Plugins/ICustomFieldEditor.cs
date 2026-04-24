namespace StructEdit.Core.Plugins;

/// <summary>
/// Plugin that overrides how individual field/property nodes are built in the document tree.
/// Register via <c>ComponentEditServiceBuilder.RegisterFieldEditor</c>.
/// </summary>
public interface ICustomFieldEditor
{
    /// <summary>The CLR type this editor handles.</summary>
    Type TargetType { get; }

    /// <summary>
    /// Creates and returns a replacement <see cref="EditNode"/> for the field described
    /// by the supplied parameters.
    /// Return <see langword="null"/> to fall back to the default reflection-based builder.
    /// </summary>
    EditNode? CreateNode(
        EditNodeId id,
        string name,
        string jsonPath,
        IValueBinding binding,
        EditNodeMetadata metadata);
}
