using Fdp.Toolkit.Diagnostics.Gizmos.Network;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    // Abstraction over the DDS writer for GizmoUiState, enabling test injection.
    public interface IGizmoUiStatePublisher
    {
        void Publish(GizmoUiState state);
    }
}
