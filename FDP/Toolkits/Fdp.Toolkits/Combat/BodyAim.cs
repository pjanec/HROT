using System;
using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐⭐ <c>CE-3195</c> (R-3003, docs/DESIGN_Weapon_Layers.md W-14a) — the FIRE ARC of a person: an aimed round leaves only while what it
    /// is aimed at lies within <see cref="CarKinem.Controllers.HumanGait.MaxAimTwist"/> of his heading (a snap shot up to the twist);
    /// beyond it the weapon brain holds the round (<c>ActionReason.Turning</c>, which keeps the posture Aimed) and the Muscle's gait turns
    /// him (W-14b). 📌 Before it a soldier walking away from his target kept shooting at it — behind his own back.
    /// <para>Not held: a non-person (a vehicle's fixed gun; a turret has its own arc, W-8), an embarked passenger (he sits at the
    /// vehicle's heading — the gait does not turn him), and a unit with no transform. The operator's fire never asks (M-2: he turns the
    /// body himself).</para>
    /// </summary>
    public static class BodyAim
    {
        /// <summary>How far (rad, 0..π, horizontal) <paramref name="at"/> lies off <paramref name="unit"/>'s heading; 0 when it has no transform
        /// or the point is under him.</summary>
        public static float OffBody(EntityRepository world, Entity unit, Vector3 at)
        {
            if (!world.HasComponent<SimTransform>(unit)) return 0f;
            ref readonly var tf = ref world.GetComponentRO<SimTransform>(unit);
            var to = new Vector2(at.X - tf.Position.X, at.Y - tf.Position.Y);
            if (to.LengthSquared() < 0.01f) return 0f;
            var fwd3 = Vector3.Transform(Vector3.UnitX, tf.Rotation);
            var fwd = new Vector2(fwd3.X, fwd3.Y);
            if (fwd.LengthSquared() < 1e-6f) return 0f;
            return MathF.Abs(global::CarKinem.Controllers.HumanGait.SignedAngle(Vector2.Normalize(fwd), to));
        }

        /// <summary>True when <paramref name="shooter"/> is a person who must turn before an aimed round at <paramref name="at"/> may
        /// leave (W-14a).</summary>
        public static bool HoldsFire(EntityRepository world, Entity shooter, Vector3 at)
            => Fdp.Toolkit.Physics.Components.PhysicsColliderReaders.IsPerson(world, shooter)
               && !(world.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>()
                    && world.HasComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(shooter))
               && OffBody(world, shooter, at) > global::CarKinem.Controllers.HumanGait.MaxAimTwist;

        /// <summary>The point an aimed round at <paramref name="target"/> flies to, for <see cref="HoldsFire"/>: its position; false when
        /// it has none (then nothing is held).</summary>
        public static bool TargetPoint(EntityRepository world, Entity target, out Vector3 at)
        {
            at = default;
            if (target.IsNull || !world.IsAlive(target) || !world.HasComponent<SimTransform>(target)) return false;
            at = world.GetComponentRO<SimTransform>(target).Position;
            return true;
        }
    }
}
