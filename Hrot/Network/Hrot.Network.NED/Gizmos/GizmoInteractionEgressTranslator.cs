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
                WriteMenuAction(evt.AnchorId, evt.ActionId);
        }

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
                PickAnchorId         = token.Target.Index,
                PickStreamId         = (uint)token.Target.Generation,
                PickSubElementId     = token.SubElementId,
                WorldX               = worldPos.X,
                WorldY               = worldPos.Y,
                WorldZ               = worldPos.Z,
                Space                = (byte)space,
            });
            SentSampleCount++;
        }

        private void WriteMenuAction(long anchorId, int actionId)
        {
            _writer!.Write(new GizmoInteractionBatch
            {
                SourceNodeId   = _nodeId,
                SequenceNumber = _sequenceNumber++,
                Kind           = GizmoInteractionEventKind.MenuAction,
                PickAnchorId   = anchorId,
                ActionId       = actionId,
            });
            SentSampleCount++;
        }
    }
}

