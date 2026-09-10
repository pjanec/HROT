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

        // ⭐⭐⭐ S1 (DESIGN_Gizmo_Anchor_Identity.md §6) — RESOLVE, DO NOT REBUILD.
        //   ⛔ This translator used to do `new Entity((int)batch.PickAnchorId, (ushort)batch.PickStreamId)`,
        //     i.e. it rebuilt a LOCAL entity handle out of the SENDER's process-local ECS index and
        //     generation. Indices are allocated per process in spawn order, so the sender's (7,3) is a
        //     DIFFERENT or DEAD entity here => remote gizmo interaction silently mis-targeted, and the
        //     IsAlive check below MASKED it by dropping or substituting a cancel.
        //   🔒 GizmoInteractionBatch.cs:21 already documents these fields as a "blittable breakdown of
        //     stable network ID", so this is a CONFORMANCE fix, not a contract change.
        //   ⭐ O(1) both ways: Fdp.Toolkit.Replication.Services.NetworkEntityMap is the map
        //     NetworkSpawningSystem:198 registers into.
        private readonly Fdp.Toolkit.Replication.Services.NetworkEntityMap? _entityMap;

        public string TopicName => "GizmoInteractionBatch";
        public TranslatorDirection Direction => TranslatorDirection.Ingress;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }

        public GizmoInteractionIngressTranslator(
            IDdsReader<GizmoInteractionBatch>? reader,
            FdpEventBus interactionBus,
            Fdp.Toolkit.Replication.Services.NetworkEntityMap? entityMap = null)
        {
            _reader         = reader;
            _interactionBus = interactionBus ?? throw new ArgumentNullException(nameof(interactionBus));
            _entityMap      = entityMap;
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
            // ⭐ S1 — PickAnchorId is a NETWORK id (GizmoInteractionBatch.cs:21). Resolve it locally.
            //   ⛔ A network id this node does not know yields NO event: dropping is correct, whereas the
            //     old handle-rebuild fabricated a wrong-or-dead entity and let it through.
            Entity entity = default;
            if (_entityMap == null || !_entityMap.TryGetEntity(batch.PickAnchorId, out entity))
                return;
            var worldPos = new Vector3(batch.WorldX, batch.WorldY, batch.WorldZ);
            var token    = new PickToken
            {
                Target       = entity,
                SubElementId = batch.PickSubElementId,
                GizmoTypeId  = batch.PickGizmoTypeId,
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
                    // Also publish as a typed GizmoMenuActionEvent so DataDrivenGizmoSystem can
                    // route it to the matching gizmo via the composite key.
                    _interactionBus.PublishManaged(new GizmoMenuActionEvent
                    {
                        AnchorId    = batch.PickAnchorId,
                        ActionId    = batch.ActionId,
                        GizmoTypeId = batch.PickGizmoTypeId,
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
                        GizmoTypeId = batch.PickGizmoTypeId,
                        PayloadJson = batch.PayloadJson ?? string.Empty,
                    });
                    break;
            }
        }
    }
}
