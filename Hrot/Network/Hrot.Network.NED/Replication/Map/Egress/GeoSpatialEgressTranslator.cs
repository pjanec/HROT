using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.NED.Descriptors;
using Hrot.NED.Common;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Hrot.Map.Common.Replication.Utils;
using Fdp.Core.Logging;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;
using Fdp.Network.Cyclone.Translators;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// Reads <see cref="SimTransform"/> + <see cref="SimVelocity"/> ECS components and publishes the merged
    /// <see cref="WorldPos"/> DDS topic — a COPY into the local ENU frame (AQ107 B / D, <see cref="WorldPosCodec"/>), no geodetic maths.
    /// </summary>
    public class GeoSpatialEgressTranslator : CycloneTranslator<WorldPos, WorldPos>
    {
        private readonly HashSet<long> _tracedNetIds = new();
        private readonly long _localNodeId;

        private const long WorldPosOrdinal = (long)Hrot.NED.Descriptors.EDescriptorType.dtWorldPos;

        /// <summary>
        /// ECS component IDs that authority-gate this descriptor.
        /// SimTransform (0), NetworkTransform (52), NetworkVelocity (53).
        /// </summary>
        private static readonly IReadOnlyList<int> _targetIds = new int[]
        {
            GlobalComponentIds.SimTransform,
            GlobalComponentIds.NetworkTransform,
            GlobalComponentIds.NetworkVelocity,
        };

        public IReadOnlyList<int> TargetComponentIds => _targetIds;

        public override TranslatorDirection Direction => TranslatorDirection.Egress;

        public GeoSpatialEgressTranslator(
            DdsParticipant participant,
            NetworkEntityMap entityMap,
            long localNodeId)
            : base(participant, ordinal: WorldPosOrdinal, entityMap)
        {
            _localNodeId = localNodeId;
        }

        /// <summary>
        /// Inbound decode is not used for authority nodes.
        /// </summary>
        protected override void Decode(in WorldPos data, IEntityCommandBuffer cmd, ISimulationView view)
        {
        }

        /// <summary>
        /// Tombstones the <see cref="WorldPos"/> topic instance to prevent descriptor leaks.
        /// </summary>
        public override void Dispose(long networkEntityId)
        {
            base.Dispose(networkEntityId);
        }

        /// <summary>
        /// Scans all locally-owned entities with <see cref="SimTransform"/> and publishes
        /// <see cref="WorldPos"/> (position, quaternion orientation, velocity, angular velocity — local ENU) to DDS.
        ///
        /// <para>
        /// Change-detection is performed by comparing the live <see cref="SimTransform"/> against
        /// the <see cref="NetworkTransform"/> shadow component, which stores the position and
        /// rotation that were last sent to the network.  A packet is sent when:
        /// <list type="bullet">
        ///   <item>The entity has moved more than 1 cmÂ˛ (Position threshold).</item>
        ///   <item>The entity has rotated by more than ~0.5Â° (Quaternion dot threshold).</item>
        ///   <item>A salted 600-tick heartbeat fires (UDP loss recovery).</item>
        /// </list>
        /// This bypass of <c>SmartEgressUtil</c> keeps the hot path entirely in unmanaged
        /// memory and avoids the Dictionary/HashSet lookups of <see cref="EgressPublicationState"/>.
        /// </para>
        /// </summary>
        public override void ScanAndPublish(ISimulationView view)
        {
            // GetComponentRW requires EntityRepository (concrete write access).
            // ScanAndPublish is only called from the egress system which always
            // supplies the live world; bail out safely if this ever changes.
            if (view is not EntityRepository repo) return;

            // Sampled ONCE per scan, so every entity published in this frame carries the same stamp —
            // which is what makes a receiver's `simNow - stamp` age comparable across entities.
            double simNowSeconds = Fdp.Toolkit.Time.SimClock.Of(view).TotalTime;

            // ⭐⭐⭐ CE-147 — THE SHADOW IS ATTACHED HERE, BY ITS OWN CONSUMER.
            //
            // ⚠ This comment used to claim "entities spawned through NedTkbBuilder always receive this
            //   component". 🔴 That was FALSE: the production TKB catalog never declared it, so this query
            //   matched ZERO entities and SimHost published no WorldPos at all (AX-011). The shadow was
            //   then attached by SimHostNodeBootstrapper's per-host onEntitySpawned hook — which fixed the
            //   symptom on ONE host and left every future owning host to remember the same wiring.
            //
            // ⭐⭐ So NetworkTransform is no longer a REQUIREMENT of this query; it is an OUTPUT of this
            //   loop. Below the authority check we upsert it when absent, which makes the invariant
            //   "whenever this node egresses an entity, the shadow exists" true BY CONSTRUCTION rather
            //   than by every spawn path remembering to provide it.
            //
            // ⭐ This is not a new pattern — it COMPLETES an existing one. GeoSpatialIngressTranslator
            //   already upserts the same component via SetComponent on first receipt (:75, :116), so the
            //   replica side has always been self-healing. Egress was the asymmetric half.
            //
            // 📄 docs/DESIGN_Cgf_AxisB_Rotation_Slice.md §13.7 (supersedes §13.3's placement ruling).
            var query = view.Query()
                .With<SimTransform>()
                .DefaultRoutePublishable()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            const float PositionThresholdSq = 0.0001f; // 1 cmÂ˛  â€” avoids spurious sends from float noise
            const float RotationDotThreshold = 0.9999f; // ~0.5Â° arc â€” Quaternion.Dot == 1 when identical
            const uint  HeartbeatInterval   = 600;      // 10 s at 60 Hz for UDP loss recovery
            const float VelocityThresholdSq = 0.05f * 0.05f;   // ⭐ CE-2348 ② — 5 cm/s: dead reckoning drifts ≤ 0.5 m in a heartbeat

            long packedKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DescriptorOrdinal, 0);

            foreach (var entity in query)
            {
                // Authority check: only publish if this node owns geospatial data for this entity.
                if (!view.HasAuthority(entity, packedKey))
                    continue;

                // ⭐⭐⭐ CE-147 — attach the shadow on first sight, AFTER the authority gate.
                //
                // ⚠ Deliberately below the gate: a REPLICA must not get one from here. Its shadow is
                //   written by GeoSpatialIngressTranslator on first receipt, and attaching one to every
                //   ghost would spend 28 bytes duplicating what the ingress path already provisions.
                //
                // ⚠⚠ SEEDED TO `default` — ZEROS — and that is a BEHAVIOURAL REQUIREMENT, not a detail.
                //   The change detection below publishes only when the live pose differs from this
                //   shadow, or when the salted heartbeat fires at % 600 ticks. ⛔ Seeding from the
                //   entity's CURRENT SimTransform would make the very first comparison say "has not
                //   moved", leaving a stationary spawned entity invisible to every other node for up to
                //   600 ticks — 10 s at 60 Hz. Zeros force a first publish.
                //   📄 §13.4 — this requirement is now satisfied by construction, not by a convention
                //   the attaching host had to know about.
                //
                // ⚠ The registration guard mirrors SpatialCoreTkbTranslator: a bare AddComponent throws
                //   "Component NetworkTransform is not registered" on a world that never registered it.
                //   A world that cannot hold the component cannot egress either, so skipping is correct.
                if (!repo.HasComponent<NetworkTransform>(entity))
                {
                    if (!repo.IsComponentTypeRegistered<NetworkTransform>())
                        continue;

                    repo.AddComponent(entity, default(NetworkTransform));

                    // ⭐ AddComponent does not touch the AuthorityMask (EntityRepository.cs:762), and the
                    //   spawn path snapshots that mask at one instant which has already passed. Grant it
                    //   explicitly so the owner owns the shadow it writes every tick.
                    repo.SetAuthority<NetworkTransform>(entity, true);
                }

                ref readonly var simTf = ref view.GetComponentRO<SimTransform>(entity);
                ref var          netTf = ref repo.GetComponentRW<NetworkTransform>(entity);

                // Shadow comparison â€” entirely in unmanaged memory, no heap allocations.
                bool hasMoved   = Vector3.DistanceSquared(simTf.Position, netTf.LastPosition) > PositionThresholdSq;
                bool hasRotated = Math.Abs(Quaternion.Dot(simTf.Rotation, netTf.LastRotation)) < RotationDotThreshold;

                // ⭐ CE-2348 ② — a changed VELOCITY is sent too: a hull that stops (or is stopped by a mobility kill) does not move another
                //   centimetre, so its last sample — still carrying its creep — was all the other nodes had, and their dead reckoning moved it.
                var linear = view.HasComponent<SimVelocity>(entity) ? view.GetComponentRO<SimVelocity>(entity).Linear : Vector3.Zero;
                bool velocityChanged = Vector3.DistanceSquared(linear, netTf.LastVelocity) > VelocityThresholdSq;

                // ⭐ CE-2348 ② — the heartbeat counts the ticks SINCE THE LAST SEND. ⛔ It was `(Tick + salt) % 600 == 0`: one exact tick in
                //   600, which a translator that is not called on every tick can step over again and again (measured: a stale velocity held
                //   ~200 s, not 10 s). Entities fall out of step by themselves, as each sends when it moves.
                bool heartbeat = view.Tick - netTf.LastSentTick >= HeartbeatInterval;

                if (!hasMoved && !hasRotated && !velocityChanged && !heartbeat)
                    continue;

                // Update shadow before publishing so the next tick comparison is against the
                // just-sent values, not stale pre-move values.
                netTf.LastPosition = simTf.Position;
                netTf.LastRotation = simTf.Rotation;
                netTf.LastVelocity = linear;
                netTf.LastSentTick = view.Tick;

                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);

                // ⭐ AQ107 B / D — the ECS pose is COPIED onto the wire (local ENU metres, quaternion, ENU velocities).
                //   ⛔ No geodetic conversion, no Euler angles (CE-4068: a nose straight up lost its heading in Euler).
                Vector3 angular = Vector3.Zero;
                if (view.HasComponent<SimVelocity>(entity))
                    angular = view.GetComponentRO<SimVelocity>(entity).Angular;   // rad/s about the local ENU axes — as held

                // Cluster-synced SIMULATION time, not wall time (CE-211). The receiver ages this sample as `simNow - SimTime` to
                // extrapolate; that subtraction only means anything if both terms come from the same synced clock. A UtcNow stamp
                // re-introduced the node-to-node skew the time sync exists to remove, and kept advancing while the cluster was paused.
                // 📄 docs/DESIGN_Dead_Reckoning.md rule R4 + §"Encoding".
                Publish(WorldPosCodec.Encode(netId.Value, simNowSeconds, simTf.Position, simTf.Rotation, linear, angular));

                if (_tracedNetIds.Add(netId.Value))
                {
                    FdpLog<GeoSpatialEgressTranslator>.Trace(
                        "[Node-{0}] Egress: Writing WorldPos for NetID={1} pos={2}", _localNodeId, netId.Value, simTf.Position);
                }
            }
        }

        /// <inheritdoc/>
        public override void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
        }
    }
}
