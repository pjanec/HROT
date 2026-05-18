using System;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Perception.Systems
{
    /// <summary>
    /// Main-thread system that consumes <see cref="AudioStimulusEvent"/>s and updates
    /// <see cref="TargetMemory"/> for all entities within hearing range of each event.
    /// <para>
    /// <b>Position convention:</b> All position reads use <see cref="SimTransform"/>,
    /// projecting to the XY ground plane. <c>VehicleState.Position</c> is never used here.
    /// </para>
    /// <para>
    /// <b>Spatial query:</b> Candidate listeners are found via <see cref="SpatialHashGrid.QueryNeighbors"/>
    /// using the event's <see cref="AudioStimulusEvent.Intensity"/> as the search radius.
    /// Only those whose personal <see cref="PerceptionReceptor.HearingRange"/> also covers the
    /// distance to the event origin are updated.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public class AudioPerceptionSystem : IEcsModuleSystem
    {
        // Pre-allocated scratch buffer for spatial query results (stack-allocated per query).
        private const int MaxQueryResults = 256;

        public void Execute(ISimulationView view, float deltaTime)
        {
            var events = view.ReadEvents<AudioStimulusEvent>();
            if (events.IsEmpty) return;

            var repo = (EntityRepository)view;
            bool hasGrid = repo.HasSingleton<SpatialGridData>();

            // Re-use a stack-allocated buffer for neighbor results.
            Span<(Entity entity, Vector2 pos)> neighbors =
                stackalloc (Entity, Vector2)[MaxQueryResults];

            foreach (ref readonly var evt in events)
            {
                var eventPos2D = new Vector2(evt.Origin.X, evt.Origin.Y);

                int candidateCount;
                if (hasGrid)
                {
                    // Fast path: spatial grid broadphase.
                    candidateCount = repo.GetSingleton<SpatialGridData>().Grid
                        .QueryNeighbors(eventPos2D, evt.Intensity, neighbors);
                }
                else
                {
                    // Fallback (test / no SpatialHashSystem registered): query world directly.
                    // QueryFallback is internal helper; pass repo
                    candidateCount = QueryFallback(repo, eventPos2D, evt.Intensity, neighbors);
                }

                for (int i = 0; i < candidateCount; i++)
                {
                    // QueryNeighbors returns full Entity handles — no reconstruction needed.
                    Entity listener = neighbors[i].entity;
                    if (!view.HasComponent<PerceptionReceptor>(listener)) continue;

                    // Check the entity's own hearing range (second filter after spatial broadphase).
                    var receptor = view.GetComponentRO<PerceptionReceptor>(listener);
                    var tf       = view.GetComponentRO<SimTransform>(listener);
                    var listenerPos = new Vector2(tf.Position.X, tf.Position.Y);

                    float dist = Vector2.Distance(listenerPos, eventPos2D);
                    if (dist > receptor.HearingRange) continue;

                    repo.Bus.Publish(new TargetHeardEvent
                    {
                        Listener          = listener,
                        SourceEntityIndex = evt.SourceEntityIndex,
                        Origin            = evt.Origin,
                    });
                }
            }
        }

        /// <summary>
        /// Fallback spatial query used when no <see cref="SpatialGridData"/> singleton is present.
        /// Performs a brute-force linear scan over all entities that have both
        /// <see cref="SimTransform"/> and <see cref="PerceptionReceptor"/>.
        /// </summary>
        private static int QueryFallback(
            EntityRepository repo,
            Vector2 eventPos2D,
            float radius,
            Span<(Entity entity, Vector2 pos)> output)
        {
            int count   = 0;
            float radSq = radius * radius;

            var query = repo.Query().With<SimTransform>().With<PerceptionReceptor>().Build();
            foreach (var entity in query)
            {
                var tf  = repo.GetComponent<SimTransform>(entity);
                var pos = new Vector2(tf.Position.X, tf.Position.Y);
                if (Vector2.DistanceSquared(pos, eventPos2D) <= radSq)
                {
                    if (count < output.Length)
                        output[count++] = (entity, pos); // store full Entity, not raw index
                }
            }
            return count;
        }
    }
}
