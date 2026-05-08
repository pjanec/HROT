using System.Numerics;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using GizmoInteractionBatch = GizmoMap.Network.GizmoInteractionBatch;
using GizmoInteractionEventKind = GizmoMap.Network.GizmoInteractionEventKind;

namespace Hrot.Network.NED.Gizmos
{
    /// <summary>
    /// IG-side ECS system that drains all gizmo interaction events from the local bus
    /// and forwards each as a <see cref="GizmoInteractionBatch"/> DDS record.
    /// Runs in BeforeSync so events generated in the UI thread are forwarded
    /// before the next ECS tick begins.
    /// </summary>
    [UpdateInPhase(SystemPhase.BeforeSync)]
    public sealed class GizmoInteractionEgressSystem : IEcsModuleSystem
    {
        private readonly byte _nodeId;
        private readonly IDdsWriter<GizmoInteractionBatch>? _writer;
        private uint _sequenceNumber;

        public GizmoInteractionEgressSystem(
            byte nodeId,
            IDdsWriter<GizmoInteractionBatch>? writer = null)
        {
            _nodeId = nodeId;
            _writer = writer;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (_writer == null) return;

            // Drain all four interaction event types.
            foreach (ref readonly var evt in view.ReadEvents<GizmoInteractionStartedEvent>())
                WriteRecord(GizmoInteractionEventKind.Started, evt.Token, evt.WorldPos);

            foreach (ref readonly var evt in view.ReadEvents<GizmoDragUpdateEvent>())
                WriteRecord(GizmoInteractionEventKind.DragUpdate, evt.Token, evt.WorldPos, evt.Space);

            foreach (ref readonly var evt in view.ReadEvents<GizmoInteractionCommitEvent>())
                WriteRecord(GizmoInteractionEventKind.Commit, evt.Token, evt.WorldPos, evt.Space);

            foreach (ref readonly var evt in view.ReadEvents<GizmoInteractionCancelEvent>())
                WriteRecord(GizmoInteractionEventKind.Cancel, evt.Token, Vector3.Zero);
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
                Space                = space,
            });
        }
    }
}
