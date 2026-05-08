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

        /// <summary>Publishes a map-click event.</summary>
        void WriteMapClick(MapClickEventDto dto);

        /// <summary>Publishes a selection-changed event.</summary>
        void WriteSelectionChanged(SelectionChangedEventDto dto);

        /// <summary>Publishes a map-command ACK (response to ExCon tool-activation commands).</summary>
        void WriteMapCommandAck(MapCommandAckDto dto);

        /// <summary>
        /// Publishes a ContextMenuRequest so ExCon can push back an action list when
        /// the IG has no cached actions for an entity.
        /// </summary>
        void WriteContextMenuRequest(Guid requestId, int mapId, IReadOnlyList<int> forSelection);

        /// <summary>
        /// Publishes the IG capabilities announcement (layer tree, supported tools).
        /// Called once on startup.
        /// </summary>
        void PublishCapabilities(int mapId, string layerTreeJson, string configSchemasJson);

        // ── Ingress (network -> IG) -- polled once per frame ─────────────────

        /// <summary>
        /// Polls the MapInteractionConfig topic.
        /// Returns null when there is no new sample.
        /// </summary>
        MapConfigDto? PollMapConfig();

        /// <summary>
        /// Polls the MapCommandRequest topic.
        /// Returns null when there is no new command.
        /// </summary>
        MapCommandDto? PollMapCommand();

        /// <summary>
        /// Polls the CreateUpdateDeleteEntityAck topic.
        /// Returns null when there is no new ACK.
        /// </summary>
        EntityLifecycleAckDto? PollEntityLifecycleAck();

        // ── Route entity creation ─────────────────────────────────────────────

        /// <summary>
        /// Creates a route entity with the given waypoints and returns the assigned entity ID.
        /// Returns 0 on failure.
        /// </summary>
        Task<int> CreateRouteEntityAsync(
            long tkbRouteType,
            IReadOnlyList<(double Lat, double Lon, double Alt)> waypoints,
            double anchorLat, double anchorLon, double anchorAlt,
            int commanderEntityId,
            CancellationToken ct = default);

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
        IDdsWriter<GizmoInteractionBatch>? GizmoInteractionWriter { get; }

        /// <summary>
        /// DDS reader for incoming <see cref="DebugPrimitivesBatch"/> frames from SimHost.
        /// Consumed by <see cref="Hrot.Network.NED.Gizmos.DebugPrimitivesIngressTranslator"/>.
        /// Null in headless/offline mode.
        /// </summary>
        IDdsReader<DebugPrimitivesBatch>? DebugPrimitivesReader { get; }
    }

    /// <summary>No-op implementation used in offline / headless / editor mode.</summary>
    public sealed class NullIgNetworkAdapter : IIgNetworkAdapter
    {
        /// <summary>Shared singleton instance.</summary>
        public static readonly NullIgNetworkAdapter Instance = new();

        private NullIgNetworkAdapter() { }

        public void WriteMapClick(MapClickEventDto dto) { }
        public void WriteSelectionChanged(SelectionChangedEventDto dto) { }
        public void WriteMapCommandAck(MapCommandAckDto dto) { }
        public void WriteContextMenuRequest(Guid requestId, int mapId, IReadOnlyList<int> forSelection) { }
        public void PublishCapabilities(int mapId, string layerTreeJson, string configSchemasJson) { }
        public MapConfigDto? PollMapConfig() => null;
        public MapCommandDto? PollMapCommand() => null;
        public EntityLifecycleAckDto? PollEntityLifecycleAck() => null;
        public Task<int> CreateRouteEntityAsync(long tkbRouteType, IReadOnlyList<(double Lat, double Lon, double Alt)> waypoints,
            double anchorLat, double anchorLon, double anchorAlt, int commanderEntityId, CancellationToken ct = default)
            => Task.FromResult(0);
        public ICommandGateway CommandGateway => NullIgCommandGateway.Instance;
        public IDdsWriter<GizmoInteractionBatch>? GizmoInteractionWriter => null;
        public IDdsReader<DebugPrimitivesBatch>? DebugPrimitivesReader => null;
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
