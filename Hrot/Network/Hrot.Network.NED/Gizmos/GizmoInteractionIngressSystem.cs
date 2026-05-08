using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using GizmoInteractionBatch = GizmoMap.Network.GizmoInteractionBatch;
using GizmoInteractionEventKind = GizmoMap.Network.GizmoInteractionEventKind;

namespace Hrot.Network.NED.Gizmos
{
    /// <summary>
    /// SimHost-side ECS system that reads pending <see cref="GizmoInteractionBatch"/> DDS
    /// records and publishes the appropriate typed interaction events to the local ECS bus.
    /// Runs in BeforeSync so gizmo systems see the events in the same frame.
    /// </summary>
    [UpdateInPhase(SystemPhase.BeforeSync)]
    public sealed class GizmoInteractionIngressSystem : IEcsModuleSystem
    {
        private readonly IDdsReader<GizmoInteractionBatch>? _reader;

        public GizmoInteractionIngressSystem(
            IDdsReader<GizmoInteractionBatch>? reader = null)
        {
            _reader = reader;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (_reader == null) return;
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(GizmoInteractionIngressSystem)} requires direct EntityRepository access.");

            while (_reader.TryRead(out var batch))
                Translate(repo, batch);
        }

        private static void Translate(EntityRepository repo, in GizmoInteractionBatch batch)
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
                    repo.Bus.Publish(new GizmoInteractionStartedEvent { Token = token, WorldPos = worldPos });
                    break;

                case GizmoInteractionEventKind.DragUpdate:
                    if (!alive)
                        // Entity gone during drag — substitute cancel for safety.
                        repo.Bus.Publish(new GizmoInteractionCancelEvent { Token = token });
                    else
                        repo.Bus.Publish(new GizmoDragUpdateEvent { Token = token, WorldPos = worldPos, Space = batch.Space });
                    break;

                case GizmoInteractionEventKind.Commit:
                    if (!alive)
                        repo.Bus.Publish(new GizmoInteractionCancelEvent { Token = token });
                    else
                        repo.Bus.Publish(new GizmoInteractionCommitEvent { Token = token, WorldPos = worldPos, Space = batch.Space });
                    break;

                case GizmoInteractionEventKind.Cancel:
                    // Always forward cancel regardless of entity liveness.
                    repo.Bus.Publish(new GizmoInteractionCancelEvent { Token = token });
                    break;
            }
        }
    }
}
