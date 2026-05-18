namespace StructEdit.Core;

/// <summary>
/// Top-level factory that opens edit sessions for components.
/// Configured via <c>ComponentEditServiceBuilder</c> in <c>StructEdit.Reflection</c>.
/// </summary>
public interface IComponentEditService
{
    /// <summary>
    /// Opens a new <see cref="IEditSession"/> for the given <paramref name="component"/>.
    /// The session owns a private copy of the component's data; the original is not modified
    /// until <see cref="IEditSession.Commit"/> is called and the caller applies the result.
    /// </summary>
    /// <param name="component">The boxed component instance to edit.</param>
    /// <param name="componentType">The exact CLR type of the component.</param>
    /// <param name="scope">
    /// Which fields to expose in the <see cref="IEditSession.Document"/>;
    /// defaults to <see cref="EditScope.WholeComponent"/>.
    /// </param>
    /// <param name="context">Optional external context passed to <c>IBufferViewProvider</c>.</param>
    IEditSession Open(
        object component,
        Type componentType,
        EditScope? scope = null,
        EditContext? context = null);
}
