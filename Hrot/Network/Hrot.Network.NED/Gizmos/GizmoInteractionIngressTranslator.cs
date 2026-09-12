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

        // ⭐⭐⭐ S1 (DESIGN_Gizmo_Anchor_Identity.md §6) — RESOLVE, DO NOT REBUILD; then §6.7 — DO NOT
        //   EVEN RESOLVE.
        //   ⛔ This translator used to do `new Entity((int)batch.PickAnchorId, (ushort)batch.PickStreamId)`,
        //     i.e. it rebuilt a LOCAL entity handle out of the SENDER's process-local ECS index and
        //     generation. Indices are allocated per process in spawn order, so the sender's (7,3) is a
        //     DIFFERENT or DEAD entity here => remote gizmo interaction silently mis-targeted, and the
        //     IsAlive check below MASKED it by dropping or substituting a cancel.
        //   🔒 GizmoInteractionBatch.cs:21 already documents these fields as a "blittable breakdown of
        //     stable network ID", so S1 was a CONFORMANCE fix, not a contract change.
        // 🔴 §6.7, 2026-09-11 — `NetworkEntityMap? _entityMap` DELETED. S1's map lookup was only needed
        //   to fill `PickToken.Target`, which no longer exists; the published token carries the received
        //   id. ⚠ And the lookup came with a `return` on the miss, i.e. a node without a map dropped
        //   EVERY incoming interaction in silence. That is gone with it.

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
            // ⭐⭐⭐ S1/§6.7 — PickAnchorId is a NETWORK id (GizmoInteractionBatch.cs:21), and the token
            //   now carries a network id, so it is FORWARDED, not translated.
            //
            //   ⛔⛔ HISTORY, and it is the reason §6.7 exists. This began as a handle-rebuild (defect
            //     D2, which fabricated a wrong-or-dead local entity); S1 replaced it with a
            //     NetworkEntityMap lookup and a `return` on the miss. ⚠ That early-return was a SILENT
            //     DROP OF EVERY INCOMING INTERACTION on any node without a map — the exact class of
            //     per-module exception the user rejected: *"replaybrowser is ecs module like any else."*
            //   ⭐ The DROP is kept — see the addressability check below — but its QUESTION changed from
            //     "is this id in my map" to "is this id in my WORLD", which every node can answer.
            var worldPos = new Vector3(batch.WorldX, batch.WorldY, batch.WorldZ);
            var token    = new PickToken
            {
                AnchorId     = batch.PickAnchorId,
                SubElementId = batch.PickSubElementId,
                GizmoTypeId  = batch.PickGizmoTypeId,
            };

            var repo = view as EntityRepository;

            // ⭐⭐ Behaviour ①, KEPT (R-137): the anchored entity dying mid-drag turns a
            //   DragUpdate/Commit into a Cancel, below.
            //   ⛔⛔ An UNKNOWN anchor is NOT a dead one, and conflating them would cancel the local
            //     focus holder's drag on every foreign interaction — on a cluster, most of them. ⇒ only
            //     a KNOWN-and-dead anchor counts. 📄 §6.7; NetworkIdResolver.IsKnownDeadAnchor.
            bool knownDead = Fdp.Toolkit.Replication.Services.NetworkIdResolver
                .IsKnownDeadAnchor(repo, batch.PickAnchorId);

            // ⭐⭐⭐ Behaviour ②, KEPT and GENERALISED: an anchor this node does not host yields NO event.
            //   ⛔ Dropping is not tidiness. DDS is broadcast, so every node sees every interaction; and
            //     DataDrivenGizmoSystem.Recipient routes to the FOCUS HOLDER FIRST (R-144) ⇒ forwarding a
            //     foreign drag would feed one operator's gesture into another operator's active tool.
            //   ⭐⭐ §6.7 CHANGED THE QUESTION, not the answer. It was *"is this id in my NetworkEntityMap"*
            //     — which made a node WITHOUT a map drop EVERY incoming interaction, in silence, and that
            //     is the per-module exception the user rejected. It is now *"is this id in my WORLD"*,
            //     which every ECS node can answer (NetworkIdResolver falls back to a filtered scan when
            //     there is no map). ⭐ Strictly stronger too: a map that has gone stale no longer decides
            //     whether a legitimate interaction is delivered.
            //   ⚠ A known-dead anchor is deliberately NOT dropped — it must reach the Cancel arm.
            if (!knownDead
                && Fdp.Toolkit.Replication.Services.NetworkIdResolver
                    .ResolveNetworkId(repo, batch.PickAnchorId).IsNull)
                return;

            bool alive = !knownDead;

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
