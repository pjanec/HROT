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
                or ActionReason.HoldNeutralOnLine   // ⭐ AW-20 — still aimed, waiting for a civilian to clear the line
                or ActionReason.AwaitingRelease   // ⭐ Air Ops slice 4 — a bomb on the run-in, waiting for its cue
                or ActionReason.AwaitingFireTime  // ⭐ IF-1 — laid, waiting for the time on target
                => WeaponPostureKind.Aimed,
            ActionReason.HoldNotSeen or ActionReason.Reloading or ActionReason.Throwing or ActionReason.OutOfArc
                or ActionReason.Done or ActionReason.Failed or ActionReason.OutOfAmmo
                or ActionReason.TooClose or ActionReason.HoldMoving   // ⭐ M4 / D-L — held, weapon ready
                or ActionReason.NotControlled or ActionReason.NoGunner or ActionReason.NotDeployed   // ⭐ WS-3 — the weapon system holds
                or ActionReason.OutOfRange or ActionReason.Masked   // ⭐ IF-1 — no fire solution
                or ActionReason.WrongDomain   // ⭐ CE-2187 — a SAM held at a truck: the weapon stays ready
                or ActionReason.HoldNeutral   // ⭐ CE-2233 — a civilian: the weapon stays ready, never fired
                or ActionReason.HoldSurrender // ⭐ CV-13a — hands up: covered, ready, never fired
                or ActionReason.HoldForceLevel // ⭐ CE-2239 — the ROE caps the force: covered, ready, never fired
                or ActionReason.HoldConduct   // ⭐ CE-2238 part 4 — an unarmed civilian: covered, ready, never fired
                or ActionReason.HoldWarn      // ⭐ CE-2239 part 3 — warned, covered, ready, never fired
                or ActionReason.HoldStunned   // ⭐ CE-3255 (BR-2) — stunned: the weapon stays ready, never fired
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
            if (kind == WeaponPostureKind.Aimed) AimedAtTheForces(world, unit, target);
        }

        /// <summary>
        /// ⭐ <c>CE-2238</c> part 4 (AW-16, CO-1; docs/DESIGN_Civilians_And_Crowd_Control.md §5h.7) — AIMING at the forces is the hostile act
        /// (drawing is not): a unit that is not a combatant (a civilian, a neutral — <see cref="Armament.IsCombatant"/>) whose weapon goes
        /// Aimed at a forces person (Friend, a combatant) commits a LETHAL act, once — his side flips (B-16). Here, in the one writer of an
        /// aimed posture, so every behaviour that aims commits it the same way.
        /// </summary>
        private static void AimedAtTheForces(EntityRepository world, Entity unit, Entity target)
        {
            if (target.IsNull || !world.IsAlive(target) || !world.IsComponentTypeRegistered<EntityInfo>() || !world.HasComponent<EntityInfo>(target)
                || world.GetComponentRO<EntityInfo>(target).ForceId != ForceId.Friend || !Armament.IsCombatant(world, target)) return;
            if (Armament.IsCombatant(world, unit)
                || Fdp.Toolkit.Civilians.HostileActs.Of(world, unit) == Fdp.Toolkit.Civilians.HostileActKind.Lethal) return;
            Fdp.Toolkit.Civilians.HostileActs.Commit(world, unit, Fdp.Toolkit.Civilians.HostileActKind.Lethal);
        }

        /// <summary>⭐ ui handoff P4 (W-13e) — the weapon brain names the mount it fires with this frame (written only when it changes).</summary>
        public static void SetActiveMount(EntityRepository world, Entity unit, int mountIndex)
        {
            if (mountIndex < 0 || mountIndex > byte.MaxValue || !world.IsComponentTypeRegistered<WeaponPosture>() || !world.HasComponent<WeaponPosture>(unit))
                return;
            if (world.GetComponentRO<WeaponPosture>(unit).ActiveMount == (byte)mountIndex) return;
            world.GetComponentRW<WeaponPosture>(unit).ActiveMount = (byte)mountIndex;
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
