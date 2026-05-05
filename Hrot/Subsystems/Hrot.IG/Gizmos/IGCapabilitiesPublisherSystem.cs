using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.IG.Abstractions;

namespace Hrot.IG.Gizmos
{
    // Publishes an IGCapabilitiesAnnounce record exactly once when the IG node starts up,
    // advertising supported pipeline targets, layer masks, and shape types to remote clients.
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class IGCapabilitiesPublisherSystem : IEcsModuleSystem
    {
        private readonly IDdsWriter<IGCapabilitiesAnnounce>? _writer; // null = local-only, no-op
        private readonly uint _nodeId;
        private bool _published;

        public IGCapabilitiesPublisherSystem(uint nodeId, IDdsWriter<IGCapabilitiesAnnounce>? writer = null)
        {
            _nodeId = nodeId;
            _writer = writer;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (_writer == null || _published) return;
            _published = true;

            _writer.Write(new IGCapabilitiesAnnounce
            {
                NodeId             = _nodeId,
                SupportedTargets   = PipelineTarget.Map2D,
                SupportedLayerMask = 0xFFFF,
                SupportedShapes    = 0xFF,
                LayerNamesJson     = "[]",
            });
        }
    }
}
