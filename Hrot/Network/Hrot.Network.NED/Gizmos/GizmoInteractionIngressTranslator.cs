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
    /// Reads pending <see cref="GizmoInteractionBatch"/> DDS records and publishes
    /// the appropriate typed interaction events directly to the isolated
    /// <see cref="FdpEventBus"/> provided at construction time.
    /// Bypasses the global world bus entirely so that interaction noise is
    /// quarantined inside the <c>GizmoInteractionModule</c> pipeline.
    /// </summary>
    public sealed class GizmoInteractionIngressTranslator : INetworkTranslator
    {
        private readonly IDdsReader<GizmoInteractionBatch>? _reader;
        private readonly FdpEventBus _interactionBus;

        public string TopicName => "GizmoInteractionBatch";
        public TranslatorDirection Direction => TranslatorDirection.Ingress;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }

        public GizmoInteractionIngressTranslator(
            IDdsReader<GizmoInteractionBatch>? reader,
            FdpEventBus interactionBus)
        {
            _reader         = reader;
            _interactionBus = interactionBus ?? throw new ArgumentNullException(nameof(interactionBus));
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader == null) return;

            while (_reader.TryRead(out var batch))
            {
                ReceivedSampleCount++;
                Translate(view, batch);
            }
        }

        public void ScanAndPublish(ISimulationView view) { }

        private void Translate(ISimulationView view, in GizmoInteractionBatch batch)
        {
            var entity   = new Entity((int)batch.PickAnchorId, (ushort)batch.PickStreamId);
            var worldPos = new Vector3(batch.WorldX, batch.WorldY, batch.WorldZ);
            var token    = new PickToken
            {
                Target       = entity,
                SubElementId = batch.PickSubElementId,
            };

            bool alive = view.IsAlive(entity);

            switch (batch.Kind)
            {
                case GizmoInteractionEventKind.Started:
                    _interactionBus.Publish(new GizmoInteractionStartedEvent { Token = token, WorldPos = worldPos });
                    break;

                case GizmoInteractionEventKind.DragUpdate:
                    if (!alive)
                        // Entity gone during drag -- substitute cancel for safety.
                        _interactionBus.Publish(new GizmoInteractionCancelEvent { Token = token });
                    else
                        _interactionBus.Publish(new GizmoDragUpdateEvent { Token = token, WorldPos = worldPos, Space = (CoordinateSpace)batch.Space });
                    break;

                case GizmoInteractionEventKind.Commit:
                    if (!alive)
                        _interactionBus.Publish(new GizmoInteractionCancelEvent { Token = token });
                    else
                        _interactionBus.Publish(new GizmoInteractionCommitEvent { Token = token, WorldPos = worldPos, Space = (CoordinateSpace)batch.Space });
                    break;

                case GizmoInteractionEventKind.Cancel:
                    // Always forward cancel regardless of entity liveness.
                    _interactionBus.Publish(new GizmoInteractionCancelEvent { Token = token });
                    break;

                case GizmoInteractionEventKind.MenuAction:
                    // Route the selected context-menu item back as a ContextActionTriggered event
                    // so that the local GizmoInteractionModule pipeline can execute the domain action.
                    // ActionName is the integer action ID serialised as a string to match the
                    // convention used by IgApplication.HandleContextMenuAction.
                    _interactionBus.PublishManaged(new ContextActionTriggered
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
                    bool isMouse   = (batch.Space & 0x80) != 0;
                    bool isPressed = (batch.Space & 0x01) != 0;
                    if (isMouse)
                        _interactionBus.Publish(new GizmoMouseEvent
                        {
                            Token     = token,
                            Button    = (MapMouseButton)batch.ActionId,
                            IsPressed = isPressed,
                            WorldPos  = worldPos,
                        });
                    else
                        _interactionBus.Publish(new GizmoKeyEvent
                        {
                            Token     = token,
                            Key       = (MapKeyboardKey)batch.ActionId,
                            IsPressed = isPressed,
                        });
                    break;

                case GizmoInteractionEventKind.StructUpdate:
                    _interactionBus.PublishManaged(new GizmoStructUpdateEvent
                    {
                        AnchorId    = batch.PickAnchorId,
                        PayloadJson = batch.PayloadJson ?? string.Empty,
                    });
                    break;
            }
        }
    }
}
