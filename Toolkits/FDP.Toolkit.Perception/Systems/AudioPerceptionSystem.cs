using System;
using System.Numerics;
using CarKinem.Spatial;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Perception.Events;
using Fdp.Kernel;

namespace FDP.Toolkit.Perception.Systems
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
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public class AudioPerceptionSystem : ComponentSystem
    {
        // Pre-allocated scratch buffer for spatial query results (stack-allocated per query).
        private const int MaxQueryResults = 256;

        protected override void OnUpdate()
        {
            var events = World.Bus.Consume<AudioStimulusEvent>();
            if (events.IsEmpty) return;

            bool hasGrid = World.HasSingleton<SpatialGridData>();

            uint tick = World.GlobalVersion;

            // Re-use a stack-allocated buffer for neighbor results.
            Span<(int entityId, Vector2 pos)> neighbors =
                stackalloc (int, Vector2)[MaxQueryResults];

            foreach (ref readonly var evt in events)
            {
                var eventPos2D = new Vector2(evt.Origin.X, evt.Origin.Y);

                int candidateCount;
                if (hasGrid)
                {
                    // Fast path: spatial grid broadphase.
                    candidateCount = World.GetSingleton<SpatialGridData>().Grid
                        .QueryNeighbors(eventPos2D, evt.Intensity, neighbors);
                }
                else
                {
                    // Fallback (test / no SpatialHashSystem registered): query world directly.
                    candidateCount = QueryFallback(eventPos2D, evt.Intensity, neighbors);
                }

                for (int i = 0; i < candidateCount; i++)
                {
                    int candidateIdx = neighbors[i].entityId;

                    // Reconstruct an Entity handle from the index.
                    // QueryNeighbors returns entity indices; we look up the full entity by index.
                    Entity listener = World.GetEntity(candidateIdx);
                    if (!World.IsAlive(listener)) continue;
                    if (!World.HasComponent<PerceptionReceptor>(listener)) continue;
                    if (!World.HasComponent<TargetMemory>(listener)) continue;

                    // Check the entity's own hearing range (second filter after spatial broadphase).
                    var receptor = World.GetComponent<PerceptionReceptor>(listener);
                    var tf       = World.GetComponent<SimTransform>(listener);
                    var listenerPos = new Vector2(tf.Position.X, tf.Position.Y);

                    float dist = Vector2.Distance(listenerPos, eventPos2D);
                    if (dist > receptor.HearingRange) continue;

                    // Determine source position (same as event origin for audio).
                    ref var mem = ref World.GetComponentRW<TargetMemory>(listener);
                    TargetMemory.AddOrUpdateTarget(
                        ref mem,
                        entityId:   evt.SourceEntityIndex,
                        posX:       evt.Origin.X,
                        posY:       evt.Origin.Y,
                        scoreBoost: 20f,
                        tick:       tick);
                }
            }
        }

        /// <summary>
        /// Fallback spatial query used when no <see cref="SpatialGridData"/> singleton is present.
        /// Performs a brute-force linear scan over all entities that have both
        /// <see cref="SimTransform"/> and <see cref="PerceptionReceptor"/>.
        /// </summary>
        private int QueryFallback(
            Vector2 eventPos2D,
            float radius,
            Span<(int entityId, Vector2 pos)> output)
        {
            int count   = 0;
            float radSq = radius * radius;

            var query = World.Query().With<SimTransform>().With<PerceptionReceptor>().Build();
            foreach (var entity in query)
            {
                var tf  = World.GetComponent<SimTransform>(entity);
                var pos = new Vector2(tf.Position.X, tf.Position.Y);
                if (Vector2.DistanceSquared(pos, eventPos2D) <= radSq)
                {
                    if (count < output.Length)
                        output[count++] = (entity.Index, pos);
                }
            }
            return count;
        }
    }
}
