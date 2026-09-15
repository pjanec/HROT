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

        // 🔴 §6.7 — `NetworkEntityMap? _entityMap` DELETED. S2 used it for the Entity -> network id
        //   direction; the token now carries the network id, so there is nothing to look up.
        public string TopicName => "GizmoInteractionBatch";
        public TranslatorDirection Direction => TranslatorDirection.Egress;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }

        public GizmoInteractionEgressTranslator(
            byte nodeId,
            IDdsWriter<GizmoInteractionBatch>? writer,
            FdpEventBus interactionBus)
        {
            _nodeId         = nodeId;
            _writer         = writer;
            _interactionBus = interactionBus ?? throw new ArgumentNullException(nameof(interactionBus));
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

        // 🔴 §6.7 — `private long NetworkIdOf(Entity)` DELETED with the map it read.

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
                // ⭐⭐⭐ S2/§6.7 — send the NETWORK id, which is what the record documents (:21) and what
                //   the token now already holds.
                //   ⛔ HISTORY: this sent `token.Target.Index`/`.Generation` (a PROCESS-LOCAL handle,
                //     meaningless on the receiver — defect D2); S2 then made it `NetworkIdOf(token.Target)`,
                //     a NetworkEntityMap lookup back UP from the handle. §6.7 deleted the handle from the
                //     token, so the id travels from the picked primitive to the wire untouched — ⭐ and a
                //     node with no map can now SEND, where before `NetworkIdOf` returned 0 and the
                //     interaction went out anchored to nothing.
                PickAnchorId         = token.AnchorId,
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

