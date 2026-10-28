using System;
using System.Numerics;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core.Logging;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Modules.Geographic;
using Hrot.Map.Common.Replication.Utils;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;
using Fdp.Network.Cyclone.Translators;

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// Ingress translator for the Hrot <c>WorldPos</c> DDS topic.
    /// Copies the sample's local-ENU pose (AQ107 B / D, <see cref="WorldPosCodec"/>) into <see cref="NetworkTransform"/> and
    /// <see cref="NetworkVelocity"/> (and <see cref="SimTransform"/> on a replica) in a single unified pass — no geodetic maths.
    /// </summary>
    public class GeoSpatialIngressTranslator : CycloneTranslator<WorldPos, WorldPos>
    {
        private const long OrdinalValue = (long)Hrot.NED.Descriptors.EDescriptorType.dtWorldPos;

        private readonly GhostCreationSystem _ghostCreationSystem;
        private readonly long _localNodeId;

        public override TranslatorDirection Direction => TranslatorDirection.Ingress;

        public GeoSpatialIngressTranslator(
            DdsParticipant? participant,
            NetworkEntityMap entityMap,
            GhostCreationSystem ghostCreationSystem,
            long localNodeId)
            : base(participant, OrdinalValue, entityMap)
        {
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
            _localNodeId = localNodeId;
        }

        protected override void Decode(in WorldPos data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            long netId = data.EntityId;
            var repo = view as EntityRepository;
            if (repo == null)
            {
                FdpLog<GeoSpatialIngressTranslator>.Warn(
                    "[Node-{0}] Cannot create ghost for NetID {1}: view is read-only.", _localNodeId, netId);
                return;
            }

            if (!EntityMap.TryGetEntity(netId, out var entity))
            {
                entity = _ghostCreationSystem.CreateGhost(repo, netId);
            }

            FdpLog<GeoSpatialIngressTranslator>.Trace(
                "[Node-{0}] Ingress: WorldPos Entity={1} pos=({2},{3})", _localNodeId, entity.Index,
                data.Position.X, data.Position.Y);

            // 1. Position & rotation — copied (local ENU); the orientation is decoded from the quaternion's vector part.
            var position = WorldPosCodec.Position(in data);
            var rotation = WorldPosCodec.Rotation(in data);

            // ⭐ CE-2348 ② — on a node that publishes this entity, the sample comes back by loopback; the EGRESS's own fields of the shadow
            //   (the last velocity sent, the last send's tick) are carried over, never reset by the echo. 📐 Measured: they were reset every
            //   frame, so a stop was never seen as a change and the heartbeat fired every frame.
            var egressShadow = repo.HasComponent<NetworkTransform>(entity) ? repo.GetComponentRO<NetworkTransform>(entity) : default;
            cmd.SetComponent(entity, new NetworkTransform
            {
                LastPosition = position,
                LastRotation = rotation,
                SimStamp     = CheckSimStamp(data.SimTime, view, netId),
                LastVelocity = egressShadow.LastVelocity,
                LastSentTick = egressShadow.LastSentTick,
            });

            // Guard: do NOT override SimTransform for locally-owned entities.
            // In AllInOne / combined Brain+Muscle roles, DDS loopback causes this translator
            // to receive WorldPos samples published by the SAME node. Without this guard
            // the last-published position would be written back every frame via loopback,
            // undoing any position changes made by physics or drag operations.

            // 1. Primary Owner check (handles Brain-node loopback for entities whose Brain IS
            //    also the NetworkAuthority primary owner).
            bool isLocallyOwned = repo.HasAuthority<SimTransform>(entity);

            if (!isLocallyOwned)
            {
                cmd.SetComponent(entity, new SimTransform { Position = position, Rotation = rotation });

                // 2. Velocity (only meaningful for remote/ghost entities): ENU, copied.
                var linear = WorldPosCodec.LinearVelocity(in data);
                cmd.SetComponent(entity, new NetworkVelocity { Value = linear });

                // ⭐ AQ107 D — the angular velocity (rad/s about the local ENU axes) lands in SimVelocity where the entity carries one.
                if (repo.IsComponentTypeRegistered<SimVelocity>() && repo.HasComponent<SimVelocity>(entity))
                    cmd.SetComponent(entity, new SimVelocity { Linear = linear, Angular = WorldPosCodec.AngularVelocity(in data) });
            }
        }

        /// <summary>
        /// The sample's simulation-time stamp (<see cref="WorldPos.SimTime"/>, seconds), complaining loudly if it looks unstamped.
        ///
        /// <para>
        /// ⭐ AQ107 D — the stamp is a plain <c>double</c> now, so the old "ticks = 0 means unspecified" sentinel is gone: sim time 0.0
        /// is a legitimate stamp. What remains of dead reckoning rule R6 is the loud diagnostic for the case that matters — a sample
        /// stamped 0 when the cluster clock is well past 0 can only come from a publisher that does not stamp, and dead reckoning
        /// would read it as an age of "the whole run so far" and fling the entity along its last velocity. The sample is still applied
        /// (a stale-stamped position beats no position), so this degrades to "no extrapolation" rather than to nothing.
        /// </para>
        ///
        /// <para>See <c>docs/DESIGN_Dead_Reckoning.md</c> rule R6.</para>
        /// </summary>
        private double CheckSimStamp(double simTime, ISimulationView view, long entityId)
        {
            if (simTime > 0.0)
                return simTime;

            double simNow = Fdp.Toolkit.Time.SimClock.Of(view).TotalTime;
            if (simNow > UnstampedGraceSeconds)
            {
                FdpLog<GeoSpatialIngressTranslator>.Warn(
                    "[Node-{0}] WorldPos for entity {1} arrived with SimTime={2} while the cluster clock reads {3:F1} s. " +
                    "Dead reckoning cannot extrapolate it and will hold the raw sample. " +
                    "The publisher is not stamping — fix it there (docs/DESIGN_Dead_Reckoning.md R6).",
                    _localNodeId, entityId, simTime, simNow);
            }

            return simTime;
        }

        /// <summary>A zero stamp is legitimate while the run is this young (a sample taken at the first frame).</summary>
        private const double UnstampedGraceSeconds = 1.0;

        public override void ScanAndPublish(ISimulationView view) { }

        public override void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not WorldPos geo) return;

            // 1. Position & rotation (copied; the orientation decoded from the quaternion's vector part)
            var position = WorldPosCodec.Position(in geo);
            var rotation = WorldPosCodec.Rotation(in geo);

            repo.SetComponent(entity, new NetworkTransform
            {
                LastPosition = position,
                LastRotation = rotation,
                SimStamp     = CheckSimStamp(geo.SimTime, repo, geo.EntityId),
            });
            repo.SetComponent(entity, new SimTransform    { Position = position, Rotation = rotation });

            // 2. Velocity
            repo.SetComponent(entity, new NetworkVelocity { Value = WorldPosCodec.LinearVelocity(in geo) });
        }
    }
}
