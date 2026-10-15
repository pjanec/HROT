using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Combat.Components;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐⭐ WL-6 (docs/DESIGN_Weapon_Layers.md W-13b, R-272) — the ONE write path into <see cref="WeaponPosture"/>. The weapon brain's
    /// executors <see cref="Report"/> at the points they already decide "aiming / firing / holding" (beside their status row); the
    /// dispatcher calls <see cref="Idle"/> for a unit with no running weapon action, which lowers Aimed to Ready at once and Ready to
    /// AtEase after <see cref="LowerAfterSeconds"/>. A no-op where the unit or the world has none.
    /// </summary>
    public static class WeaponPostureOf
    {
        /// <summary>How long a unit keeps its weapon Ready after its last weapon action before carrying it at ease.</summary>
        public const float LowerAfterSeconds = 5f;

        /// <summary>The posture a weapon-brain status means; null = it says nothing about the weapon (None, Moving).</summary>
        public static WeaponPostureKind? For(ActionReason reason) => reason switch
        {
            ActionReason.Aiming or ActionReason.Firing or ActionReason.Cooldown or ActionReason.BlindFire
                or ActionReason.HoldFriendlyOnLine or ActionReason.HoldRoe or ActionReason.Slewing or ActionReason.Guiding
                or ActionReason.Turning   // ⭐ CE-3195 — held while the body turns, still aimed (the gait turns him toward it)
                or ActionReason.AwaitingRelease   // ⭐ Air Ops slice 4 — a bomb on the run-in, waiting for its cue
                or ActionReason.AwaitingFireTime  // ⭐ IF-1 — laid, waiting for the time on target
                => WeaponPostureKind.Aimed,
            ActionReason.HoldNotSeen or ActionReason.Reloading or ActionReason.Throwing or ActionReason.OutOfArc
                or ActionReason.Done or ActionReason.Failed or ActionReason.OutOfAmmo
                or ActionReason.TooClose or ActionReason.HoldMoving   // ⭐ M4 / D-L — held, weapon ready
                or ActionReason.NotControlled or ActionReason.NoGunner or ActionReason.NotDeployed   // ⭐ WS-3 — the weapon system holds
                or ActionReason.OutOfRange or ActionReason.Masked   // ⭐ IF-1 — no fire solution
                => WeaponPostureKind.Ready,
            _ => null,
        };

        /// <summary>The weapon brain reports <paramref name="reason"/> about <paramref name="target"/> (or <paramref name="point"/>).</summary>
        public static void Report(EntityRepository world, Entity unit, ActionReason reason, Entity target, Vector3 point = default)
        {
            if (For(reason) is not { } kind || !world.IsComponentTypeRegistered<WeaponPosture>() || !world.HasComponent<WeaponPosture>(unit))
                return;
            ref var p = ref world.GetComponentRW<WeaponPosture>(unit);
            p.Kind = kind;
            p.Target = target;
            p.Point = target.IsNull ? point : default;
            p.IdleSeconds = 0f;
        }

        /// <summary>
        /// ⭐ <c>CE-3195</c> (W-14b) — where an AIMED <paramref name="unit"/> points (world metres): its target's chest (1 m above its
        /// position) or its aim point. False when it is not Aimed or aims at nothing it can place. The ONE reading of the posture's aim,
        /// shared by the drawn twist (<c>EntityBodyLayer3D</c>), the person gait (<c>CarKinematicsSystem</c>) and the Stride motor.
        /// </summary>
        public static bool TryAimPoint(EntityRepository world, Entity unit, out Vector3 at)
        {
            at = default;
            if (!world.IsComponentTypeRegistered<WeaponPosture>() || !world.HasComponent<WeaponPosture>(unit)) return false;
            ref readonly var posture = ref world.GetComponentRO<WeaponPosture>(unit);
            if (posture.Kind != WeaponPostureKind.Aimed) return false;
            if (!posture.Target.IsNull && world.IsAlive(posture.Target) && world.HasComponent<SimTransform>(posture.Target))
            {
                at = world.GetComponentRO<SimTransform>(posture.Target).Position + new Vector3(0f, 0f, 1.0f);
                return true;
            }
            if (posture.Target.IsNull && posture.Point != Vector3.Zero) { at = posture.Point; return true; }
            return false;
        }

        /// <summary><paramref name="unit"/> runs no weapon action this frame (<paramref name="dt"/> seconds).</summary>
        public static void Idle(EntityRepository world, Entity unit, float dt)
        {
            if (!world.IsComponentTypeRegistered<WeaponPosture>() || !world.HasComponent<WeaponPosture>(unit)) return;
            ref var p = ref world.GetComponentRW<WeaponPosture>(unit);
            if (p.Kind == WeaponPostureKind.AtEase) return;
            p.IdleSeconds += dt;
            if (p.Kind == WeaponPostureKind.Aimed) p.Kind = WeaponPostureKind.Ready;
            if (p.IdleSeconds >= LowerAfterSeconds) { p.Kind = WeaponPostureKind.AtEase; p.Target = Entity.Null; p.Point = default; }
        }
    }
}
