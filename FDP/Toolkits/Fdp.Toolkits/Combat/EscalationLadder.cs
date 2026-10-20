using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Components;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ <c>CE-2239</c> part 3 (B2; docs/DESIGN_Civilians_And_Crowd_Control.md §5k EL-3/EL-4) — the men a security-force unit has WARNED, and
    /// when (sim seconds; full ⇒ the oldest goes), plus how long its warning cue still shows. Brain-local on the WARNER, never saved, one
    /// writer: <see cref="EscalationLadder"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.WarningLog)]
    [DataPolicy(DataPolicy.NoScenario)]
    public unsafe struct WarningLog
    {
        public fixed long TargetIds[EscalationLadder.MaxWarned];
        public fixed double At[EscalationLadder.MaxWarned];
        public byte Count;
        /// <summary>Until when (sim seconds) the warner shows <see cref="GestureKind.Warning"/>.</summary>
        public double ShowUntil;
    }

    /// <summary>
    /// ⭐ <c>CE-2239</c> part 3 (AW-10, AW-16, AW-17; B2, §5k) — the security forces' ESCALATION LADDER against a civilian who is not a
    /// combatant on status: the rung this unit may use on THAT man — identified-armed at ease ⇒ <see cref="RoeForceLevel.Warn"/> (no round) ·
    /// ready (or firing into the air) ⇒ <see cref="RoeForceLevel.NonLethal"/>, and <see cref="RoeForceLevel.Lethal"/> once this unit warned him
    /// <see cref="IgnoredWarningSeconds"/> ago and he is still ready · not identified-armed ⇒ NonLethal. Aimed at the unit, or a lethal act,
    /// is self-defence (<see cref="ForceLevels.SelfDefence"/>, before the ladder). The unit's <c>Roe.ForceLevel</c> caps the rung
    /// (<see cref="ForceLevels.LevelFor"/>). ⛔ Never for a soldier (AW-24).
    /// </summary>
    public static unsafe class EscalationLadder
    {
        /// <summary>How many warned men one unit remembers.</summary>
        public const int MaxWarned = 8;

        /// <summary>EL-2 — a warning IGNORED this long (he is still ready) opens the lethal rung (s). ⭐ A lean, for the user's review.</summary>
        public const float IgnoredWarningSeconds = 5f;

        /// <summary>EL-3 — how long the warner shows the cue (s).</summary>
        public const float WarningShowSeconds = 2f;

        /// <summary>EL-1 — does the ladder decide <paramref name="unit"/>'s force on <paramref name="target"/>: a security-force unit, a live
        /// target who is not a combatant on status.</summary>
        public static bool Applies(EntityRepository world, Entity unit, Entity target)
            => !target.IsNull && world.IsAlive(target) && Armament.IsSecurityForce(world, unit) && !Armament.IsCombatant(world, target);

        /// <summary>EL-2 — the rung on <paramref name="target"/> (before the unit's cap and self-defence — <see cref="ForceLevels.LevelFor"/>).</summary>
        public static RoeForceLevel Level(EntityRepository world, Entity unit, Entity target)
        {
            if (Armament.Judge(world, unit, target) != ArmedJudgement.IdentifiedLethal) return RoeForceLevel.NonLethal;
            if (PostureOf(world, target) == WeaponPostureKind.AtEase) return RoeForceLevel.Warn;
            double at = WarnedAt(world, unit, target);
            return at > 0 && Now(world) - at >= IgnoredWarningSeconds ? RoeForceLevel.Lethal : RoeForceLevel.NonLethal;
        }

        /// <summary>EL-3 — warn <paramref name="man"/> when it is due: he is identified-armed to <paramref name="unit"/>, or the unit may only
        /// warn (its <c>Roe.ForceLevel</c> is Warn), and this unit has not warned him yet. The FIRST warning's time stands (a repeated shout
        /// does not reset the clock). True when he has been warned (now or before).</summary>
        public static bool WarnIfDue(EntityRepository world, Entity unit, Entity man)
        {
            if (WarnedAt(world, unit, man) > 0) return true;
            if (Armament.Judge(world, unit, man) != ArmedJudgement.IdentifiedLethal && RoeOf.ForceLevel(world, unit) != RoeForceLevel.Warn) return false;
            Warn(world, unit, man);
            return true;
        }

        /// <summary>EL-3 — <paramref name="unit"/> warns <paramref name="man"/>: the cue (<see cref="GestureKind.Warning"/> for
        /// <see cref="WarningShowSeconds"/>) and the stamp in its own <see cref="WarningLog"/>.</summary>
        public static void Warn(EntityRepository world, Entity unit, Entity man)
        {
            if (man.IsNull || !world.IsAlive(unit)) return;
            if (!world.IsComponentTypeRegistered<WarningLog>()) world.RegisterComponent<WarningLog>();
            if (!world.HasComponent<WarningLog>(unit)) world.AddComponent(unit, new WarningLog());
            ref var log = ref world.GetComponentRW<WarningLog>(unit);
            double now = Now(world);
            long id = (long)man.PackedValue;
            bool known = false;
            for (int i = 0; i < log.Count && !known; i++) known = log.TargetIds[i] == id;
            if (!known)
            {
                if (log.Count == MaxWarned)
                {
                    for (int i = 1; i < MaxWarned; i++) { log.TargetIds[i - 1] = log.TargetIds[i]; log.At[i - 1] = log.At[i]; }
                    log.Count--;
                }
                log.TargetIds[log.Count] = id;
                log.At[log.Count] = now;
                log.Count++;
            }
            log.ShowUntil = now + WarningShowSeconds;
            if (world.IsComponentTypeRegistered<Gesture>()) Gestures.Set(world, unit, GestureKind.Warning);
        }

        /// <summary>When (sim seconds) <paramref name="unit"/> first warned <paramref name="man"/>; 0 = never (EL-4: per warner).</summary>
        public static double WarnedAt(EntityRepository world, Entity unit, Entity man)
        {
            if (man.IsNull || !world.IsComponentTypeRegistered<WarningLog>() || !world.HasComponent<WarningLog>(unit)) return 0;
            ref readonly var log = ref world.GetComponentRO<WarningLog>(unit);
            long id = (long)man.PackedValue;
            for (int i = 0; i < log.Count; i++)
                if (log.TargetIds[i] == id) return log.At[i];
            return 0;
        }

        /// <summary>EL-3 — the police brain's watch, every tick: lower the cue once it has shown, and warn every man <paramref name="unit"/>
        /// REMEMBERS within <paramref name="reach"/> m who is identified-armed and not a combatant — a Neutral too (AW-16: drawn ⇒ warn).</summary>
        public static void Watch(EntityRepository world, Entity unit, float reach)
        {
            if (world.IsComponentTypeRegistered<WarningLog>() && world.HasComponent<WarningLog>(unit)
                && world.GetComponentRO<WarningLog>(unit).ShowUntil < Now(world) && Gestures.Of(world, unit) == GestureKind.Warning)
                Gestures.Set(world, unit, GestureKind.None);
            if (!world.HasComponent<TargetMemory>(unit) || !world.HasComponent<SimTransform>(unit)) return;
            var me = world.GetComponentRO<SimTransform>(unit).Position;
            float reach2 = reach * reach;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(unit);
            for (int i = 0; i < mem.Count; i++)
            {
                if (TargetMemory.IsAnonymous(in mem, i)) continue;
                float dx = mem.PositionsX[i] - me.X, dy = mem.PositionsY[i] - me.Y;
                if (dx * dx + dy * dy > reach2) continue;
                var man = new Entity((ulong)mem.EntityIds[i]);
                if (!Applies(world, unit, man) || !Injury.IsInTheFight(world, man) || WarnedAt(world, unit, man) > 0) continue;
                if (Armament.Judge(world, unit, man) == ArmedJudgement.IdentifiedLethal) Warn(world, unit, man);
            }
        }

        /// <summary>His posture as the ladder reads it: none ⇒ at ease. ⭐ EL-5 (aimed at a POINT — into the air — is the ready rung, never
        /// self-defence) needs nothing here: the rung only tells at-ease apart, and <see cref="ForceLevels.SelfDefence"/> asks for aimed AT
        /// the unit.</summary>
        private static WeaponPostureKind PostureOf(EntityRepository world, Entity man)
        {
            if (!world.IsComponentTypeRegistered<WeaponPosture>() || !world.HasComponent<WeaponPosture>(man)) return WeaponPostureKind.AtEase;
            return world.GetComponentRO<WeaponPosture>(man).Kind;
        }

        private static double Now(EntityRepository world)
        {
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : world.SimulationTime;
            return now > 0 ? now : 1e-6;
        }
    }
}
