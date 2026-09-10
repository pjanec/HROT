using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using GizmoInteractionBatch = GizmoMap.Network.GizmoInteractionBatch;
using GizmoInteractionEventKind = GizmoMap.Network.GizmoInteractionEventKind;

namespace Hrot.Network.NED.Gizmos
{
    /// <summary>
    /// Drains all gizmo interaction events from the isolated interaction
    /// <see cref="FdpEventBus"/> and forwards each as a <see cref="GizmoInteractionBatch"/>
    /// DDS record. Reads exclusively from the private bus so that only locally-generated
    /// UI events (from <c>DebugGizmoLayer</c>) are forwarded; network-ingress events are
    /// never re-broadcast.
    /// </summary>
    public sealed class GizmoInteractionEgressTranslator : INetworkTranslator
    {
        private readonly byte _nodeId;
        private readonly IDdsWriter<GizmoInteractionBatch>? _writer;
        private readonly FdpEventBus _interactionBus;
        private uint _sequenceNumber;

        // ⭐⭐ S2 — the Entity -> network id direction. Fdp.Toolkit.Replication.Services.NetworkEntityMap is
        //   bidirectional and O(1) both ways (TryGetEntity / TryGetNetworkId), so no scan is involved.
        private readonly Fdp.Toolkit.Replication.Services.NetworkEntityMap? _entityMap;
        public string TopicName => "GizmoInteractionBatch";
        public TranslatorDirection Direction => TranslatorDirection.Egress;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }

        public GizmoInteractionEgressTranslator(
            byte nodeId,
            IDdsWriter<GizmoInteractionBatch>? writer,
            FdpEventBus interactionBus,
            Fdp.Toolkit.Replication.Services.NetworkEntityMap? entityMap = null)
        {
            _nodeId         = nodeId;
            _writer         = writer;
            _interactionBus = interactionBus ?? throw new ArgumentNullException(nameof(interactionBus));
            _entityMap      = entityMap;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (_writer == null) return;

            // Drain all interaction event types from the isolated bus.
            foreach (ref readonly var evt in _interactionBus.Read<GizmoInteractionStartedEvent>())
                WriteRecord(GizmoInteractionEventKind.Started, evt.Token, evt.WorldPos);

            foreach (ref readonly var evt in _interactionBus.Read<GizmoDragUpdateEvent>())
                WriteRecord(GizmoInteractionEventKind.DragUpdate, evt.Token, evt.WorldPos, evt.Space);

            foreach (ref readonly var evt in _interactionBus.Read<GizmoInteractionCommitEvent>())
                WriteRecord(GizmoInteractionEventKind.Commit, evt.Token, evt.WorldPos, evt.Space);

            foreach (ref readonly var evt in _interactionBus.Read<GizmoInteractionCancelEvent>())
                WriteRecord(GizmoInteractionEventKind.Cancel, evt.Token, Vector3.Zero);

            // Forward context-menu action selections back to SimHost.
            foreach (ref readonly var evt in _interactionBus.Read<GizmoMenuActionEvent>())
                WriteMenuAction(evt.AnchorId, evt.ActionId, evt.GizmoTypeId);

            // Forward StructInspector Apply mutations back to SimHost.
            foreach (var evt in _interactionBus.ReadManaged<GizmoStructUpdateEvent>())
                WriteStructUpdate(evt.AnchorId, evt.GizmoTypeId, evt.PayloadJson);
        }

        /// <summary>⭐ S2 — the entity's stable network id, or 0 when it has none (then nothing is sent).</summary>
        private long NetworkIdOf(Entity entity)
            => _entityMap != null && _entityMap.TryGetNetworkId(entity, out var netId) ? netId : 0L;

        private void WriteRecord(
            GizmoInteractionEventKind kind,
            PickToken token,
            Vector3 worldPos,
            CoordinateSpace space = default)
        {
            _writer!.Write(new GizmoInteractionBatch
            {
                SourceNodeId         = _nodeId,
                SequenceNumber       = _sequenceNumber++,
                Kind                 = kind,
                // ⭐⭐⭐ S2 — send the NETWORK id, which is what the record documents (:21).
                //   ⛔ This used to send token.Target.Index / .Generation — a PROCESS-LOCAL handle that is
                //     meaningless on the receiver. S1 and S2 are the two ends of one hop and land together.
                PickAnchorId         = NetworkIdOf(token.Target),
                PickStreamId         = 0u,   // reserved: "publisher stream discriminator" (GizmoPickToken.cs:10)
                PickSubElementId     = token.SubElementId,
                PickGizmoTypeId      = token.GizmoTypeId,
                WorldX               = worldPos.X,
                WorldY               = worldPos.Y,
                WorldZ               = worldPos.Z,
                Space                = (byte)space,
            });
            SentSampleCount++;
        }

        private void WriteMenuAction(long anchorId, int actionId, uint gizmoTypeId)
        {
            _writer!.Write(new GizmoInteractionBatch
            {
                SourceNodeId   = _nodeId,
                SequenceNumber = _sequenceNumber++,
                Kind           = GizmoInteractionEventKind.MenuAction,
                PickAnchorId   = anchorId,
                ActionId       = actionId,
                PickGizmoTypeId = gizmoTypeId,
            });
            SentSampleCount++;
        }

        private void WriteStructUpdate(long anchorId, uint gizmoTypeId, string payloadJson)
        {
            _writer!.Write(new GizmoInteractionBatch
            {
                SourceNodeId   = _nodeId,
                SequenceNumber = _sequenceNumber++,
                Kind           = GizmoInteractionEventKind.StructUpdate,
                PickAnchorId   = anchorId,
                PickGizmoTypeId = gizmoTypeId,
                PayloadJson    = payloadJson,
            });
            SentSampleCount++;
        }
    }
}

