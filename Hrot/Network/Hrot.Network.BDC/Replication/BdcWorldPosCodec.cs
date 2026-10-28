using System.Numerics;
using Fdp.Toolkit.Replication.Components;
using Hrot.BDC.Common;
using Hrot.BDC.Messages;

namespace Hrot.BDC.Replication
{
    /// <summary>
    /// ⭐ AQ107 F — the ONE mapping between the ECS pose and the BDC <see cref="BdcWorldPos"/> sample, both ways: the same rules as
    /// NED's <c>WorldPos</c> (local ENU metres, quaternion vector part with w implied, ENU velocities), in BDC's own wire types. The
    /// orientation arithmetic is NOT here — it is <see cref="QuaternionWireCodec"/>, the helper NED calls too.
    /// </summary>
    internal static class BdcWorldPosCodec
    {
        public static BdcWorldPos Encode(
            long entityId, double simTimeSeconds,
            Vector3 position, Quaternion rotation, Vector3 linearVelocity, Vector3 angularVelocity)
        {
            QuaternionWireCodec.Encode(rotation, out float qx, out float qy, out float qz);
            return new BdcWorldPos
            {
                EntityId        = entityId,
                SimTime         = simTimeSeconds,
                Position        = new BdcVec3d { X = position.X, Y = position.Y, Z = position.Z },
                OrientationXyz  = new BdcOrientationQuat { X = qx, Y = qy, Z = qz },
                Velocity        = new BdcVec3f { X = linearVelocity.X, Y = linearVelocity.Y, Z = linearVelocity.Z },
                AngularVelocity = new BdcVec3f { X = angularVelocity.X, Y = angularVelocity.Y, Z = angularVelocity.Z },
            };
        }

        public static void Decode(
            in BdcWorldPos pose,
            out Vector3 position, out Quaternion rotation, out Vector3 linearVelocity, out Vector3 angularVelocity)
        {
            position        = new Vector3((float)pose.Position.X, (float)pose.Position.Y, (float)pose.Position.Z);
            rotation        = QuaternionWireCodec.Decode(pose.OrientationXyz.X, pose.OrientationXyz.Y, pose.OrientationXyz.Z);
            linearVelocity  = new Vector3(pose.Velocity.X, pose.Velocity.Y, pose.Velocity.Z);
            angularVelocity = new Vector3(pose.AngularVelocity.X, pose.AngularVelocity.Y, pose.AngularVelocity.Z);
        }
    }
}
