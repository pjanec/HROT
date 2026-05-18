namespace GizmoMap.Network
{
    // Minimal read-only DDS subscriber abstraction for gizmo network components.
    // Production code uses CycloneDDS.Runtime.DdsReader<T> wrapped in an adapter;
    // unit tests supply a fake.
    public interface IDdsReader<T>
    {
        // Attempts to read one pending sample. Returns true if a sample was available,
        // false when the reader is empty or no-op.
        bool TryRead(out T sample);
    }
}
