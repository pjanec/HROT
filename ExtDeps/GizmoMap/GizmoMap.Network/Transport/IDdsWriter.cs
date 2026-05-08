namespace GizmoMap.Network
{
    // Thin abstraction over a DDS writer used by gizmo publisher adapters.
    // Decouples production code from the concrete CycloneDDS writer so that
    // unit tests can inject a capturing stub without a live DDS participant.
    public interface IDdsWriter<T>
    {
        void Write(T sample);
    }
}
