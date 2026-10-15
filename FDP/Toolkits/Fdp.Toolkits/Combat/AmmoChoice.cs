using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐⭐ M8 (D-I, W-6; <c>docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md</c> §2) — the weapon brain's ROUND choice, and the
    /// one expected-damage rule both the choice and the AI's effectiveness input read (R-212 A1 — no second formula): ONE round of
    /// <c>ammo</c> from this mount, on the face the shot would meet (eye → the target's middle, <see cref="ArmorModel.FacingOfArrival"/>),
    /// against the armour column its penetrator reads; a warhead with an area effect adds its fragments (kinetic, vs that face) and —
    /// on an unarmoured target (no hull) — its blast, both at distance 0. Divided by the target's maximum health, capped at 1.
    /// </summary>
    public static class AmmoChoice
    {
        /// <summary>Values closer than this are a tie — and a tie keeps the loaded round (no needless switch).</summary>
        public const float Tie = 1e-4f;

        /// <summary>The armour (mm) the shot of <paramref name="ammo"/> from <paramref name="shooter"/> would meet on <paramref name="target"/>:
        /// the face from the shooter's EYE to the target's middle (the line <c>FireProcessingSystem</c> flies), the column its penetrator
        /// reads. 0 when either has no position or the target declares no armour.</summary>
        public static float ArmourFacing(EntityRepository world, Entity shooter, Entity target, long ammo)
            => ArmourFacing(world, shooter, target, CombatTkb.PenetratorOf(world, ammo));

        private static float ArmourFacing(EntityRepository world, Entity shooter, Entity target, Penetrator penetrator)
        {
            if (!world.HasComponent<SimTransform>(target) || !world.HasComponent<SimTransform>(shooter)) return 0f;
            var eye = world.GetComponent<SimTransform>(shooter).Position;
            eye.Z += Fdp.Toolkit.Perception.LineOfSight.TerrainWorldLosStrategy.EyeHeightFor(world, shooter, HitModel.LogicalStance(world, shooter));
            var middle = AimPoint.Middle(world, target, out _, out _);
            var facing = ArmorModel.FacingOfArrival(world.GetComponent<SimTransform>(target), middle - eye);
            return ArmorModel.ArmourFor(CombatTkb.PlatformOf(world, target), facing, penetrator);
        }

        /// <summary>The damage ONE round of <paramref name="ammo"/> from <paramref name="mount"/> is expected to do to
        /// <paramref name="target"/> (direct hit + the warhead's area at distance 0). <paramref name="shooter"/> is the unit (the owner of
        /// a mount child).</summary>
        public static float ExpectedDamage(EntityRepository world, Entity shooter, Entity target, WeaponMountDto? mount, long ammo)
        {
            var (direct, area) = Split(world, shooter, target, mount, ammo);
            return direct + area;
        }

        /// <summary>The direct-hit and the area part of <see cref="ExpectedDamage"/>.</summary>
        private static (float Direct, float Area) Split(EntityRepository world, Entity shooter, Entity target, WeaponMountDto? mount, long ammo)
        {
            var (pen, dmg) = CombatTkb.RoundOf(world, mount, ammo);
            var penetrator = CombatTkb.PenetratorOf(world, ammo);
            float armour = pen > 0f ? ArmourFacing(world, shooter, target, penetrator) : 0f;
            float direct = ArmorModel.HitDamage(pen, dmg, armour);
            float area = 0f;
            if (CombatTkb.WarheadOf(world, ammo) is { HasAreaEffect: true } w)
            {
                float kinetic = penetrator == Penetrator.Kinetic && pen > 0f ? armour : ArmourFacing(world, shooter, target, Penetrator.Kinetic);
                // ⭐ CE-3203 (Building Interiors §3k.1) — the burst's own law, a metre from a man / a weapon, at a hull's skin
                var platformOf = CombatTkb.PlatformOf(world, target);
                var (kind, size, at) = Systems.AreaEffectSystem.FragmentTargetOf(world, target, platformOf,
                    Fdp.Toolkit.Physics.Components.PhysicsColliderReaders.HullHeight(world, target), Fdp.Toolkit.Tkb.Domain.StanceId.Standing, 1f);
                area += AreaEffect.Fragments(w, kind, size, at, kinetic, 1f)?.Damage
                        ?? w.FragmentDamage * ArmorModel.PenetrationChance(w.FragmentPenetrationMm, kinetic);
                // ⭐ R-2019 D-M2 — the burst's own rule (R-212 A1): blast × the target's susceptibility (a person 1, a pickup 1, an MBT 0)
                area += w.BlastLethalDamage * ArmorModel.BlastSusceptibility(CombatTkb.PlatformOf(world, target),
                    Fdp.Toolkit.Physics.Components.PhysicsColliderReaders.HullHeight(world, target));
            }
            return (direct, area);
        }

        /// <summary>⭐ D-I — the value of one round: <see cref="ExpectedDamage"/> ÷ the target's maximum health (its own
        /// <see cref="Health.Max"/>, else its TKB <c>MaxHealth</c>), capped at 1. A target with no maximum scores the raw damage / 100.</summary>
        public static float ValueOf(EntityRepository world, long ammo, WeaponMountDto? mount, Entity shooter, Entity target)
            => Score(world, ammo, mount, shooter, target).Value;

        /// <summary>The value and, as the tie-break, the AREA damage: when every round kills (a man — the value caps at 1), the one
        /// that also sprays the room around him wins (HE against men and technicals, D-I).</summary>
        private static (float Value, float Area) Score(EntityRepository world, long ammo, WeaponMountDto? mount, Entity shooter, Entity target)
        {
            float maxHealth = world.HasComponent<Health>(target) ? world.GetComponentRO<Health>(target).Max : 0f;
            if (maxHealth <= 0f) maxHealth = CombatTkb.PlatformOf(world, target)?.MaxHealth ?? 0f;
            if (maxHealth <= 0f) maxHealth = 100f;
            var (direct, area) = Split(world, shooter, target, mount, ammo);
            return (MathF.Min(1f, (direct + area) / maxHealth), area);
        }

        /// <summary>
        /// The index of the best round in <paramref name="load"/> against <paramref name="target"/> — only types with rounds left; the
        /// higher value wins, then (all equal) the larger area effect; a tie on both keeps the loaded one. −1 when nothing is left.
        /// </summary>
        public static int Best(EntityRepository world, in AmmoLoadout load, WeaponMountDto? mount, Entity shooter, Entity target)
        {
            int best = -1; (float Value, float Area) bestScore = (float.NegativeInfinity, float.NegativeInfinity);
            if (load.Loaded < load.Count && load.RoundsAt(load.Loaded) > 0)
            {
                best = load.Loaded;
                bestScore = Score(world, load.AmmoAt(load.Loaded), mount, shooter, target);
            }
            for (int i = 0; i < load.Count; i++)
            {
                if (i == load.Loaded || load.RoundsAt(i) <= 0) continue;
                var v = Score(world, load.AmmoAt(i), mount, shooter, target);
                bool better = v.Value > bestScore.Value + Tie
                              || (MathF.Abs(v.Value - bestScore.Value) <= Tie && v.Area > bestScore.Area + Tie);
                if (better) { best = i; bestScore = v; }
            }
            return best;
        }

        /// <summary>
        /// ⭐ D-I — the round for this engagement, chosen ONCE per target (or again when the loaded type runs out): a different round
        /// starts a switch of one reload (<paramref name="reloadSeconds"/>). True while the switch runs (the weapon holds). A mount with
        /// no <see cref="AmmoLoadout"/> never switches.
        /// </summary>
        public static bool Choose(EntityRepository world, Entity mountEntity, WeaponMountDto? mount, Entity shooter, Entity target, float reloadSeconds)
        {
            if (!world.IsComponentTypeRegistered<AmmoLoadout>() || !world.HasComponent<AmmoLoadout>(mountEntity)) return false;
            ref var load = ref world.GetComponentRW<AmmoLoadout>(mountEntity);
            if (load.ChosenFor != target || load.RoundsAt(load.Loaded) <= 0)
            {
                int best = Best(world, load, mount, shooter, target);
                load.ChosenFor = target;
                if (best >= 0 && best != load.Loaded)
                {
                    load.Loaded = (byte)best;
                    load.SwitchRemaining = MathF.Max(Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.ReloadSecondsOrFallback(reloadSeconds), float.Epsilon);
                }
            }
            return load.SwitchRemaining > 0f;
        }

        /// <summary>The munition type loaded in <paramref name="mountEntity"/> (0 = none: the mount's own <c>AmmoGuid</c> applies).</summary>
        public static long LoadedAmmo(EntityRepository world, Entity mountEntity)
            => world.IsComponentTypeRegistered<AmmoLoadout>() && world.HasComponent<AmmoLoadout>(mountEntity)
                ? world.GetComponentRO<AmmoLoadout>(mountEntity).LoadedAmmo : 0L;

        /// <summary>
        /// ⭐ CE-1079 (DESIGN_Entity_Control §8e D-2/D-3) — the OPERATOR's round choice: move the breech <paramref name="steps"/> types
        /// on (skipping types with no rounds left, wrapping). A different round costs one reload, as the AI's switch does (D-I);
        /// <c>ChosenFor</c> is cleared so an AI that takes the unit back chooses afresh. False when the mount has no loadout, or no
        /// other type has rounds.
        /// </summary>
        public static bool Cycle(EntityRepository world, Entity mountEntity, int steps, float reloadSeconds)
        {
            if (steps <= 0 || !world.IsComponentTypeRegistered<AmmoLoadout>() || !world.HasComponent<AmmoLoadout>(mountEntity)) return false;
            ref var load = ref world.GetComponentRW<AmmoLoadout>(mountEntity);
            if (load.Count < 2) return false;
            int at = load.Loaded;
            for (int step = 0; step < steps; step++)
            {
                int next = at;
                for (int k = 1; k <= load.Count; k++)
                {
                    int i = (at + k) % load.Count;
                    if (load.RoundsAt(i) > 0) { next = i; break; }
                }
                at = next;
            }
            if (at == load.Loaded) return false;
            load.Loaded = (byte)at;
            load.ChosenFor = Entity.Null;
            load.SwitchRemaining = MathF.Max(Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.ReloadSecondsOrFallback(reloadSeconds), float.Epsilon);
            return true;
        }

        /// <summary>⭐ CE-1079 — a round switch is running on <paramref name="mountEntity"/> (the weapon holds, like a reload).</summary>
        public static bool Switching(EntityRepository world, Entity mountEntity)
            => world.IsComponentTypeRegistered<AmmoLoadout>() && world.HasComponent<AmmoLoadout>(mountEntity)
               && world.GetComponentRO<AmmoLoadout>(mountEntity).SwitchRemaining > 0f;

        /// <summary>One round of the loaded type leaves (call beside <see cref="Magazine.Spend"/>).</summary>
        public static void SpendLoaded(EntityRepository world, Entity mountEntity)
        {
            if (!world.IsComponentTypeRegistered<AmmoLoadout>() || !world.HasComponent<AmmoLoadout>(mountEntity)) return;
            ref var load = ref world.GetComponentRW<AmmoLoadout>(mountEntity);
            load.SetRounds(load.Loaded, Math.Max(0, load.RoundsAt(load.Loaded) - 1));
        }
    }
}
