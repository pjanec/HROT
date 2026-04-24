namespace StructEdit.Core.Memory;

/// <summary>
/// Type-specific operations for copying unmanaged structs to/from native memory.
/// </summary>
public interface IRuntimeTypeOps
{
    int SizeOf { get; }
    unsafe void CopyObjectToNative(object boxed, void* destination);
    unsafe object BoxFromNative(void* source);
}
