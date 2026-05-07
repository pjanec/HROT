namespace Fdp.Toolkit.Diagnostics.Gizmos.Network
{
    /// <summary>
    /// Minimal read-only DDS subscriber abstraction for gizmo network components.
    /// Production code uses <c>CycloneDDS.Runtime.DdsReader&lt;T&gt;</c> wrapped in an adapter;
    /// unit tests supply a fake.
    /// </summary>
    public interface IDdsReader<T>
    {
        /// <summary>
        /// Attempts to read one pending sample. Returns <c>true</c> if a sample was available,
        /// <c>false</c> when the reader is empty or no-op.
        /// </summary>
        bool TryRead(out T sample);
    }
}
