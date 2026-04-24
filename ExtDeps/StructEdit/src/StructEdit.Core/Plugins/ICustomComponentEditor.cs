namespace StructEdit.Core.Plugins;

/// <summary>
/// Plugin that overrides the entire document-building process for a specific component type.
/// Register via <c>ComponentEditServiceBuilder.RegisterComponentEditor</c>.
/// </summary>
public interface ICustomComponentEditor
{
    /// <summary>The component type this editor handles.</summary>
    Type ComponentType { get; }

    /// <summary>
    /// Builds and returns a complete <see cref="EditDocument"/> for the component
    /// whose data is stored in <paramref name="buffer"/>.
    /// </summary>
    EditDocument BuildDocument(IEditBuffer buffer, EditScope scope, EditContext? context);
}
