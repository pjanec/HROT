using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Components;

namespace Fdp.Toolkit.Perception
{
    /// <summary>
    /// ⭐⭐ <c>CE-3054</c> B — how DANGEROUS a contact is, judged at READ time (R-194 / R-201: memory stores identity +
    /// freshness, never danger). The ONE function every threat reader calls. 📄 docs/DESIGN_Sensors_And_Doctrine.md §7.8a.
    /// <para>First cut: armed (the target carries <see cref="WeaponState"/>, which <c>CombatTkbTranslator</c> stamps exactly
    /// when its TKB has a weapon mount — replicas included) = 1, unarmed = <see cref="Unarmed"/>, ⭐ killed (Health ≤ 0,
    /// <c>CE-3080</c>) = 0. A target this node does not hold is UNKNOWN and counts as armed: hidden does not mean harmless.</para>
    /// </summary>
    public static class ThreatDanger
    {
        /// <summary>The danger of an unarmed contact.</summary>
        public const float Unarmed = 0.3f;

        /// <summary>The danger of <paramref name="target"/> to <paramref name="self"/>, in [0, 1].</summary>
        public static float Of(ISimulationView view, Entity self, Entity target)
        {
            if (target.IsNull || !view.IsAlive(target)) return 1f;
            // ⭐ CE-3080 (R-214) — a KILLED (⭐ CE-2155: or incapacitated) unit is no danger: its entity stays (Health 0) and still carries WeaponState, so
            //   it used to read as armed for every threat reader. Here, once, for all of them. 📄 DESIGN_Sensors_And_Doctrine §7.8a.
            if (IsOutOfTheFight(view, target)) return 0f;
            if (!view.HasComponent<WeaponState>(target)) return Unarmed;
            // ⭐ CE-2165 V1 — to a VEHICLE, danger is what the target's weapons can do to MY armour (DESIGN_Vehicle_Exposure_Cycle §2).
            if (view is EntityRepository repo && ToMe(repo, self, target, out float toMe)) return toMe;
            return 1f;
        }

        /// <summary>The floor of an ARMED target's danger to a vehicle: harmless to my armour, but still armed and still watched.</summary>
        public const float ArmedHarmless = 0.1f;

        /// <summary>A mount's danger fades to 0 between its range and this multiple of it.</summary>
        public const float RangeFadeFactor = 1.2f;

        /// <summary>
        /// ⭐ <c>CE-2165</c> V1 (DESIGN_Vehicle_Exposure_Cycle §2, Sensors §11.2 G1) — the danger of an armed <paramref name="target"/> to a
        /// VEHICLE <paramref name="self"/>: over the target's mounts, the most of <c>ArmorModel.PenetrationChance</c>(the mount's resolved
        /// round, on the face of mine it faces) × in range, clamped to [<see cref="ArmedHarmless"/>, 1] — the person scale's "armed = he
        /// can hurt me = 1", read through the SAME armour model the shot uses (R-212 A1). ⚠ Not damage ÷ my health: a side-on RPG would
        /// read 0.16 and fall under every absolute threshold (the Sentry's <c>MinDanger</c> 0.5). False — the caller keeps "armed = 1" —
        /// when self is not a vehicle (a soldier's vest is not a vehicle's armour), has no armour of its own, or either side has no TKB numbers.
        /// </summary>
        public static bool ToMe(EntityRepository world, Entity self, Entity target, out float danger)
        {
            danger = 1f;
            if (self.IsNull || !world.IsAlive(self) || !Fdp.Toolkit.Spatial.Eqs.VehicleCover.IsVehicle(world, self)) return false;
            var mine = Fdp.Toolkit.Combat.CombatTkb.PlatformOf(world, self);
            if (mine == null || (mine.ArmorFront <= 0f && mine.ArmorSide <= 0f && mine.ArmorRear <= 0f)) return false;
            if (!Fdp.Toolkit.Combat.CombatTkb.TryGetTemplate(world, target, out var template)) return false;
            var suite = template.GetDescriptor<Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto>();
            if (suite == null || suite.Mounts.Count == 0) return false;
            if (!world.HasComponent<SimTransform>(self) || !world.HasComponent<SimTransform>(target)) return false;

            var myTransform = world.GetComponentRO<SimTransform>(self);
            var hisPosition = world.GetComponentRO<SimTransform>(target).Position;
            float distance = System.Numerics.Vector3.Distance(myTransform.Position, hisPosition);
            float armour = Fdp.Toolkit.Combat.ArmorModel.ArmourFor(mine, Fdp.Toolkit.Combat.ArmorModel.FacingOf(in myTransform, hisPosition));

            float best = 0f;
            foreach (var mount in suite.Mounts)
            {
                float inRange = InRange(mount.Range, distance);
                if (inRange <= 0f) continue;
                // ⚠ an UNKNOWN round (penetration ≤ 0) ignores armour, exactly as ArmorModel.HitDamage does — never harmless.
                float penetration = Fdp.Toolkit.Combat.CombatTkb.PenetrationOf(world, mount);
                float canHurtMe = penetration <= 0f ? 1f : Fdp.Toolkit.Combat.ArmorModel.PenetrationChance(penetration, armour);
                best = System.Math.Max(best, canHurtMe * inRange);
            }
            danger = System.Math.Clamp(best, ArmedHarmless, 1f);
            return true;
        }

        /// <summary>1 inside <paramref name="range"/> (0 = unknown ⇒ always in range), fading linearly to 0 at <see cref="RangeFadeFactor"/> × it.</summary>
        public static float InRange(float range, float distance)
        {
            if (range <= 0f || distance <= range) return 1f;
            float fadeEnd = range * RangeFadeFactor;
            return distance >= fadeEnd ? 0f : (fadeEnd - distance) / (fadeEnd - range);
        }

        /// <summary>⭐ <c>CE-3080</c> — true when <paramref name="target"/> is killed (Health at or below 0; the entity is kept) or,
        /// ⭐ <c>CE-2155</c> (R-2011 I4), INCAPACITATED: down and bleeding, no longer a threat or a target. A unit with no Health, or one
        /// this node does not hold, is still in the fight (renamed from <c>IsDead</c>).</summary>
        public static bool IsOutOfTheFight(ISimulationView view, Entity target)
            => Fdp.Toolkit.Combat.Injury.StateOf(view, target) >= Fdp.Toolkit.Combat.InjuryState.Incapacitated;

        /// <summary>
        /// ⭐ <c>CE-3063</c> (§6.1a K3, R-207) — the danger of memory slot <paramref name="slot"/>: an identified contact through
        /// <see cref="Of"/>; a HEARD, anonymous one from the class it sounded like (there is no entity to read). The ONE accessor
        /// a reader iterating the memory should call — ⚠ <see cref="Of"/> on an anonymous slot's synthetic id resolves no
        /// entity and reads as "unknown = armed" (1).
        /// </summary>
        public static unsafe float OfSlot(ISimulationView view, Entity self, in TargetMemory mem, int slot)
        {
            if (!TargetMemory.IsAnonymous(in mem, slot)) return Of(view, self, new Entity((ulong)mem.EntityIds[slot]));
            fixed (byte* c = mem.SourceClass) return OfClass(c[slot]);
        }

        /// <summary>
        /// The danger of a heard source by its class (<c>Tkb.Domain.SoundSourceClass</c>). ⚠ First-cut values, tunable: an
        /// unknown class counts as armed (hidden is not harmless); footsteps and a wheeled engine are "maybe armed".
        /// </summary>
        public static float OfClass(byte sourceClass) => sourceClass switch
        {
            1 => 0.6f,   // Footsteps
            2 => 0.6f,   // WheeledEngine
            3 => 1f,     // TrackedEngine
            4 => 0.8f,   // SmallArms
            5 => 1f,     // HeavyWeapon
            6 => 1f,     // Explosion
            _ => 1f,     // Unknown
        };

        /// <summary>The unit's own fighting strength in [0, 1]: its health fraction, scaled down when it is unarmed.</summary>
        public static float OwnStrength(ISimulationView view, Entity self)
        {
            float health = 1f;
            if (view.HasComponent<Health>(self))
            {
                ref readonly var h = ref view.GetComponentRO<Health>(self);
                health = h.Max > 0f ? System.Math.Clamp(h.Current / h.Max, 0f, 1f) : 0f;
            }
            return health * (view.HasComponent<WeaponState>(self) ? 1f : Unarmed);
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3054</c> C — how FRESH a remembered contact is. <see cref="TargetMemory.Freshness"/> IS the freshness
    /// score (lean A; the backend renames it later): it climbs while tracked and fades while not.
    /// </summary>
    public static class ThreatFreshness
    {
        /// <summary>A memory score as a freshness in [0, 1] (<see cref="PerceptionConstants.FreshnessSaturation"/> = 1).</summary>
        public static float Of(float score)
            => System.Math.Clamp(score / PerceptionConstants.FreshnessSaturation, 0f, 1f);

        /// <summary>
        /// True when slot <paramref name="i"/> of <paramref name="self"/>'s memory is LIVE: a sensor tracks it now, or its
        /// freshness is at least <see cref="PerceptionConstants.LiveFreshness"/>.
        /// </summary>
        public static unsafe bool IsLive(ISimulationView view, Entity self, in TargetMemory mem, int i)
        {
            if (Of(mem.Freshness[i]) >= PerceptionConstants.LiveFreshness) return true;
            if (!view.HasComponent<ActiveSensorTracks>(self)) return false;
            ref readonly var tracks = ref view.GetComponentRO<ActiveSensorTracks>(self);
            long id = mem.EntityIds[i];
            for (int t = 0; t < tracks.Count; t++)
                if (tracks.EntityIds[t] == id) return true;
            return false;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-3136</c> P-3 (peek-and-fire D4) — does <paramref name="self"/> SEE <paramref name="target"/> RIGHT NOW: a sensor
    /// holds it this frame with the Visual modality (<see cref="ActiveSensorTracks"/>, the fact <c>ThreatInSight</c> reads). ⛔ Not
    /// <see cref="TargetMemory"/> — its modality is OR-accumulated (ever seen, not seen now).
    /// ⚠ A unit with NO sight model — the world never registered the tracks, or the unit carries none (its TKB neither sees nor
    /// hears) — answers <c>true</c>: nothing can say it is hidden, so it behaves as before.
    /// </summary>
    public static class SightNow
    {
        public static unsafe bool Sees(ISimulationView view, Entity self, Entity target)
        {
            if (view is EntityRepository repo && !repo.IsComponentTypeRegistered<ActiveSensorTracks>()) return true;
            if (!view.HasComponent<ActiveSensorTracks>(self)) return true;
            ref readonly var tracks = ref view.GetComponentRO<ActiveSensorTracks>(self);
            long id = (long)target.PackedValue;
            for (int t = 0; t < tracks.Count; t++)
                if (tracks.EntityIds[t] == id && (tracks.Modalities[t] & (byte)SensorModality.Visual) != 0) return true;
            return false;
        }
    }
}
