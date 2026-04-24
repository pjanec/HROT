namespace StructEdit.Core.UnionSupport;

/// <summary>
/// Plugin interface that projects a raw fixed-buffer field as a typed view
/// (union / chameleon pattern). Registered on <see cref="ComponentEditServiceBuilder"/>.
/// </summary>
public interface IBufferViewProvider
{
    /// <summary>
    /// Returns true if this provider can project the buffer described by <paramref name="request"/>.
    /// Called once per <see cref="FixedBuffer"/> node during document build.
    /// </summary>
    bool CanCreateView(BufferViewRequest request);

    /// <summary>
    /// Creates and returns a <see cref="BufferViewResult"/> whose <see cref="BufferViewResult.Node"/>
    /// replaces the raw <see cref="EditNodeKind.FixedBuffer"/> node in the document tree.
    /// </summary>
    BufferViewResult CreateView(BufferViewRequest request);
}
