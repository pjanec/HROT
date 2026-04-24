using System.ComponentModel;

namespace StructEdit.Core;

/// <summary>
/// Internal contract for the temporary storage of a cloned component during an edit session.
/// Must be public because <see cref="EditValidationContext"/> exposes it as a public property.
/// </summary>
/// <remarks>Infrastructure interface. Do not implement directly.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IEditBuffer : IDisposable
{
    Type ComponentType { get; }
    bool IsNative { get; }
    bool IsDirty { get; }

    void MarkDirty();
    bool TryGetRootSpan(out Span<byte> bytes);
    IValueBinding CreateRootBinding();
    object Box();
}
