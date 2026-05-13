using GizmoMap.Network;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Modules
{
    // Adapts an IDdsWriter<GizmoUiState> to the IGizmoUiStatePublisher contract
    // so the DDS transport integrates cleanly with the GizmoUiStateHub.
    internal sealed class DdsGizmoUiStatePublisher : IGizmoUiStatePublisher
    {
        private readonly Network.IDdsWriter<GizmoUiState> _writer;

        public DdsGizmoUiStatePublisher(Network.IDdsWriter<GizmoUiState> writer)
        {
            _writer = writer;
        }

        public void Publish(GizmoUiState state)
        {
            _writer.Write(state);
        }
    }
}
