using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using Hrot.Common.Events;
using GizmoInteractionBatch = GizmoMap.Network.GizmoInteractionBatch;
using GizmoInteractionEventKind = GizmoMap.Network.GizmoInteractionEventKind;

namespace Hrot.Network.NED.Gizmos
{
    /// <summary>
    /// SimHost-side ECS system that reads pending <see cref="GizmoInteractionBatch"/> DDS
    /// records and publishes the appropriate typed interaction events to the local ECS bus.
    /// Runs in BeforeSync so gizmo systems see the events in the same frame.
    /// </summary>
    public sealed class GizmoInteractionIngressTranslator : INetworkTranslator
    {
        private readonly IDdsReader<GizmoInteractionBatch>? _reader;

        public string TopicName => "GizmoInteractionBatch";
        public TranslatorDirection Direction => TranslatorDirection.Ingress;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }

        public GizmoInteractionIngressTranslator(
            IDdsReader<GizmoInteractionBatch>? reader = null)
        {
            _reader = reader;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader == null) return;
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(GizmoInteractionIngressTranslator)} requires direct EntityRepository access.");

            while (_reader.TryRead(out var batch))
            {
                ReceivedSampleCount++;
                Translate(cmd, repo, batch);
            }
        }

        public void ScanAndPublish(ISimulationView view) { }

        private static void Translate(IEntityCommandBuffer cmd, EntityRepository repo, in GizmoInteractionBatch batch)
        {
            var entity   = new Entity((int)batch.PickAnchorId, (ushort)batch.PickStreamId);
            var worldPos = new Vector3(batch.WorldX, batch.WorldY, batch.WorldZ);
            var token    = new PickToken
            {
                Target       = entity,
                SubElementId = batch.PickSubElementId,
            };

            bool alive = repo.IsAlive(entity);

            switch (batch.Kind)
            {
                case GizmoInteractionEventKind.Started:
                    cmd.PublishEvent(new GizmoInteractionStartedEvent { Token = token, WorldPos = worldPos });
                    break;

                case GizmoInteractionEventKind.DragUpdate:
                    if (!alive)
                        // Entity gone during drag -- substitute cancel for safety.
                        cmd.PublishEvent(new GizmoInteractionCancelEvent { Token = token });
                    else
                        cmd.PublishEvent(new GizmoDragUpdateEvent { Token = token, WorldPos = worldPos, Space = (CoordinateSpace)batch.Space });
                    break;

                case GizmoInteractionEventKind.Commit:
                    if (!alive)
                        cmd.PublishEvent(new GizmoInteractionCancelEvent { Token = token });
                    else
                        cmd.PublishEvent(new GizmoInteractionCommitEvent { Token = token, WorldPos = worldPos, Space = (CoordinateSpace)batch.Space });
                    break;

                case GizmoInteractionEventKind.Cancel:
                    // Always forward cancel regardless of entity liveness.
                    cmd.PublishEvent(new GizmoInteractionCancelEvent { Token = token });
                    break;

                case GizmoInteractionEventKind.MenuAction:
                    // Route the selected context-menu item back as a ContextActionTriggered event
                    // so that SimHost-side handlers can execute the corresponding domain action.
                    // ActionName is the integer action ID serialised as a string to match the
                    // convention used by IgApplication.HandleContextMenuAction.
                    repo.Bus.PublishManaged(new ContextActionTriggered
                    {
                        EntityNetworkId = (int)batch.PickAnchorId,
                        ActionName      = batch.ActionId.ToString(),
                    });
                    break;

                case GizmoInteractionEventKind.RawInput:
                    // Space field encodes input type and state:
                    //   bit7 (0x80) = 1 -> mouse event, 0 -> keyboard event
                    //   bit0 (0x01) = 1 -> pressed, 0 -> released
                    // ActionId holds (int)MapMouseButton or (int)MapKeyboardKey.
                    bool isMouse  = (batch.Space & 0x80) != 0;
                    bool isPressed = (batch.Space & 0x01) != 0;
                    if (isMouse)
                        cmd.PublishEvent(new GizmoMouseEvent
                        {
                            Token     = token,
                            Button    = (MapMouseButton)batch.ActionId,
                            IsPressed = isPressed,
                            WorldPos  = worldPos,
                        });
                    else
                        cmd.PublishEvent(new GizmoKeyEvent
                        {
                            Token     = token,
                            Key       = (MapKeyboardKey)batch.ActionId,
                            IsPressed = isPressed,
                        });
                    break;
            }
        }
    }
}
