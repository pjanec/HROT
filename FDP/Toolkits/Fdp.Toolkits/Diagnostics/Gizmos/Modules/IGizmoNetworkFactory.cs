using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Modules
{
    // Minimal factory abstraction used by GizmoNetworkTransportModule.
    // Lives in Fdp.Toolkits so the module stays free of Hrot assembly references.
    // INetworkFactory (Hrot.Core) implements this interface because it exposes
    // the same three members with matching signatures.
    public interface IGizmoNetworkFactory
    {
        // The DDS participant owned by this factory instance.
        // Null when the factory was created without a participant (headless / unit-test mode).
        DdsParticipant? Participant { get; }

        // Creates the ECS system that publishes the gizmo primitive buffer to the network.
        // Returns null when the protocol does not support gizmo streaming.
        IEcsModuleSystem? CreateGizmoPublisherSystem(DebugPrimitiveBuffer buffer, long localNodeId);

        // Creates the gizmo interaction network translators (ingress and/or egress).
        // Returns an empty list when the protocol does not support gizmo streaming.
        IReadOnlyList<INetworkTranslator> CreateGizmoTranslators(FdpEventBus interactionBus, long localNodeId, bool headless);
    }
}
