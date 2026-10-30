using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;

namespace Hrot.Core.Network
{
    /// <summary>
    /// Protocol-neutral abstraction over all DDS write/read operations performed by the
    /// Image Generator application layer.
    ///
    /// The IG application creates a DDS participant and passes it to the factory. The factory
    /// (e.g. NedNetworkFactory) constructs the concrete adapter that owns all DDS writer and
    /// reader instances. The IG itself only calls this interface -- it never references any
    /// Hrot.NED type directly.
    /// </summary>
    public interface IIgNetworkAdapter : IDisposable
    {
        // ── Egress (IG -> network) ────────────────────────────────────────────






        // ── Ingress (network -> IG) -- polled once per frame ─────────────────
        // ⛔ R-3067 / AQ109 X3 — none left: ExCon's map-command, map-config and context-menu topics, and the entity-ack poll that fed the retired MapCommandController, are gone.




        // ── Command gateway ───────────────────────────────────────────────────

        /// <summary>
        /// Neutral command gateway for create-entity / update-descriptor / mission-control
        /// requests initiated by the IG operator (e.g. MiniExConPanel, drag-drop).
        /// </summary>
        ICommandGateway CommandGateway { get; }

        /// <summary>
        /// DDS writer used by <see cref="Hrot.Network.NED.Gizmos.GizmoInteractionEgressSystem"/>
        /// to forward gizmo interaction events to SimHost.
        /// Null in headless/offline mode.
        /// </summary>
        IDdsWriter<GizmoMap.Network.GizmoInteractionBatch>? GizmoInteractionWriter { get; }

        /// <summary>
        /// DDS reader for incoming <see cref="DebugPrimitivesBatch"/> frames from SimHost.
        /// Consumed by <see cref="Hrot.Network.NED.Gizmos.DebugPrimitivesIngressTranslator"/>.
        /// Null in headless/offline mode.
        /// </summary>
        IDdsReader<GizmoMap.Network.DebugPrimitivesBatch>? DebugPrimitivesReader { get; }
    }

    /// <summary>No-op implementation used in offline / headless / editor mode.</summary>
    public sealed class NullIgNetworkAdapter : IIgNetworkAdapter
    {
        /// <summary>Shared singleton instance.</summary>
        public static readonly NullIgNetworkAdapter Instance = new();

        private NullIgNetworkAdapter() { }

        public ICommandGateway CommandGateway => NullIgCommandGateway.Instance;
        public IDdsWriter<GizmoMap.Network.GizmoInteractionBatch>? GizmoInteractionWriter => null;
        public IDdsReader<GizmoMap.Network.DebugPrimitivesBatch>? DebugPrimitivesReader => null;
        public void Dispose() { }
    }

    /// <summary>No-op command gateway used by <see cref="NullIgNetworkAdapter"/>.</summary>
    internal sealed class NullIgCommandGateway : ICommandGateway
    {
        public static readonly NullIgCommandGateway Instance = new();

        private NullIgCommandGateway() { }

        public Task<int> CreateEntityAsync(CreateEntityCommand cmd, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task SendUpdateDescriptorAsync(UpdateEntityDescriptorCommand cmd, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<MissionCommitResult> SendMissionControlRequestAsync(MissionControlCommand cmd, CancellationToken ct = default)
            => Task.FromResult(new MissionCommitResult { Success = false });

        public Task SendUpdateAttributeAsync(Fdp.Toolkit.Replication.Events.UpdateEntityAttributeCommand cmd, CancellationToken ct = default)
            => Task.CompletedTask;

        public void Dispose() { }
    }
}
