using System;
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
        private readonly PipelineTarget _supportedTargets;
        private bool _published;

        public IGCapabilitiesPublisherSystem(
            uint nodeId,
            IDdsWriter<IGCapabilitiesAnnounce>? writer = null,
            PipelineTarget supportedTargets = PipelineTarget.Map2D)
        {
            _nodeId          = nodeId;
            _writer          = writer;
            _supportedTargets = supportedTargets;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (_writer == null || _published) return;
            _published = true;

            // Build shape mask by reflecting over all DebugPrimitiveShape values.
            // One-time cold path; no per-frame allocation.
            uint shapeMask = 0u;
            foreach (DebugPrimitiveShape shape in Enum.GetValues<DebugPrimitiveShape>())
                shapeMask |= (1u << (int)shape);

            _writer.Write(new IGCapabilitiesAnnounce
            {
                NodeId               = _nodeId,
                SupportedTargets     = _supportedTargets,
                SupportedLayerMask   = 0xFFFF,
                SupportedShapeMask   = shapeMask,
                LayerNamesJson       = "[]",
                // IG is a dumb terminal (GZ038): no local gizmo plugins.
                RegisteredGizmosJson = "[]",
            });
        }
    }
}
