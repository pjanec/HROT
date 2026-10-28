using System.Numerics;
using Fdp.Toolkit.Replication.Components;
using Hrot.NED.Descriptors;

namespace Hrot.Map.Common.Replication.Utils
{
    /// <summary>
    /// ⭐ AQ107 D — the ONE mapping between the ECS pose (<c>SimTransform</c> + <c>SimVelocity</c>) and the <see cref="WorldPos"/> wire
    /// sample, both ways. The egress translator, the ingress translator, the spawn-request mapper and the descriptor-update system all
    /// call it, so no path can decode a pose differently from how another path encodes it.
    /// <para>Position, velocity and angular velocity are COPIED (local ENU, <see cref="EnuWire"/>); the orientation goes through
    /// <see cref="QuaternionWireCodec"/> — the same helper BDC uses. ⛔ No Euler angles, no geodetic maths.</para>
    /// </summary>
    public static class WorldPosCodec
    {
        /// <summary>The sample for an entity's pose. <paramref name="simTimeSeconds"/> is the cluster-synced simulation time it was
        /// sampled at (dead reckoning ages from it).</summary>
        public static WorldPos Encode(
            long entityId, double simTimeSeconds,
            Vector3 position, Quaternion rotation, Vector3 linearVelocity, Vector3 angularVelocity)
        {
            QuaternionWireCodec.Encode(rotation, out float qx, out float qy, out float qz);
            return new WorldPos
            {
                EntityId        = entityId,
                SimTime         = simTimeSeconds,
                Position        = EnuWire.ToWire(position),
                OrientationXyz  = new Hrot.NED.Messages.OrientationQuat { X = qx, Y = qy, Z = qz },
                Velocity        = EnuWire.ToWireF(linearVelocity),
                AngularVelocity = EnuWire.ToWireF(angularVelocity),
            };
        }

        /// <summary>The sample's position, local ENU metres.</summary>
        public static Vector3 Position(in WorldPos pose) => EnuWire.FromWire(pose.Position);

        /// <summary>The sample's orientation. An all-zero <see cref="WorldPos.OrientationXyz"/> is the identity.</summary>
        public static Quaternion Rotation(in WorldPos pose)
            => QuaternionWireCodec.Decode(pose.OrientationXyz.X, pose.OrientationXyz.Y, pose.OrientationXyz.Z);

        /// <summary>The sample's linear velocity, m/s, local ENU.</summary>
        public static Vector3 LinearVelocity(in WorldPos pose) => EnuWire.FromWireF(pose.Velocity);

        /// <summary>The sample's angular velocity, rad/s about the local ENU axes.</summary>
        public static Vector3 AngularVelocity(in WorldPos pose) => EnuWire.FromWireF(pose.AngularVelocity);

        /// <summary>True when the sample carries any motion (linear or angular) — what a spawn request needs to decide whether to add
        /// a <c>SimVelocity</c>.</summary>
        public static bool HasVelocity(in WorldPos pose)
            => pose.Velocity.X != 0f || pose.Velocity.Y != 0f || pose.Velocity.Z != 0f
            || pose.AngularVelocity.X != 0f || pose.AngularVelocity.Y != 0f || pose.AngularVelocity.Z != 0f;
    }
}
