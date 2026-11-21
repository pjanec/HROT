using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Replication
{
    /// <summary>
    /// ⭐ <c>CE-2173</c> (Air Ops §9c, the live <c>ua-air-strike</c> miss) — where an entity is and how it moves AS THIS NODE KNOWS IT.
    /// <para>📐 Measured: on a brain node (CGF) a unit whose MOTION another node owns (SimHost) is neither owned nor a ghost, so
    /// <c>DeadReckoningSyncSystem</c> skips it (<c>driveFromNetwork = !roleHasMuscle &amp;&amp; !roleHasBrain</c> ⇒ ghosts only) and its
    /// <see cref="SimVelocity"/> is never refreshed — the live velocity arrives only in <see cref="NetworkVelocity"/>
    /// (<c>GeoSpatialIngressTranslator</c>). A brain that read <see cref="SimVelocity"/> saw a plane standing still.</para>
    /// <para>The rule: motion this node owns ⇒ <see cref="SimTransform"/> / <see cref="SimVelocity"/>; otherwise, when a network sample
    /// exists, the sample projected to now along its velocity — <see cref="Project"/>, the SAME formula dead reckoning blends toward.</para>
    /// </summary>
    public static class ReplicatedMotion
    {
        /// <summary>Dead reckoning's target: <c>LastPosition + Vel · (simNow − SimStamp)</c>, the age clamped at zero
        /// (docs/DESIGN_Dead_Reckoning.md R2–R4).</summary>
        public static Vector3 Project(in NetworkTransform sample, in NetworkVelocity velocity, double simNow)
        {
            float age = (float)(simNow - sample.SimStamp);
            if (age < 0f) age = 0f;
            return sample.LastPosition + velocity.Value * age;
        }

        /// <summary>
        /// <paramref name="position"/> and <paramref name="velocity"/> of <paramref name="entity"/> as this node knows them (see the type).
        /// False when it has no <see cref="SimTransform"/>.
        /// </summary>
        public static bool Of(EntityRepository world, Entity entity, out Vector3 position, out Vector3 velocity)
        {
            position = default; velocity = default;
            if (!world.IsAlive(entity) || !world.HasComponent<SimTransform>(entity)) return false;
            if (!world.HasAuthority<SimTransform>(entity)
                && world.IsComponentTypeRegistered<NetworkVelocity>() && world.HasComponent<NetworkVelocity>(entity)
                && world.IsComponentTypeRegistered<NetworkTransform>() && world.HasComponent<NetworkTransform>(entity))
            {
                ref readonly var sample = ref world.GetComponentRO<NetworkTransform>(entity);
                ref readonly var vel = ref world.GetComponentRO<NetworkVelocity>(entity);
                velocity = vel.Value;
                position = Project(in sample, in vel, Fdp.Toolkit.Time.SimClock.Of(world).TotalTime);
                return true;
            }
            position = world.GetComponentRO<SimTransform>(entity).Position;
            velocity = world.IsComponentTypeRegistered<SimVelocity>() && world.HasComponent<SimVelocity>(entity)
                ? world.GetComponentRO<SimVelocity>(entity).Linear : Vector3.Zero;
            return true;
        }
    }
}
