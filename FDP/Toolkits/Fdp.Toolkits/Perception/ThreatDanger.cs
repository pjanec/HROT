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
            // ⭐ CE-2233 (CIV-0; DESIGN_Civilians_And_Crowd_Control CV-8, R-3013) — a NEUTRAL is no danger: a civilian is not a threat
            //   until a visible hostile act flips its side (CV-4). Here, once, for every threat reader — like the dead above.
            if (IsNeutral(view, target)) return 0f;
            // ⭐ CV-13a (R-2024; DESIGN_Civilians_And_Crowd_Control §4, §5h.2) — a man showing HANDS UP is held like a neutral, whatever his
            //   side: no danger while his hands are up, the full danger the moment they come down (a feint is read live). Not disarmed (AW-5).
            if (ShowsHandsUp(view, target)) return 0f;
            // ⭐⭐ CE-3229 (B-7; DESIGN_Civilians_And_Crowd_Control §7.8) — THE armed rule: a combatant on STATUS (today's test — a weapon
            //   not only a dispenser, CE-2188, or a working controlled weapon, WS-4), a civilian by what THIS observer can identify (AW-15).
            //   ⭐ A-6: Unidentified reads unarmed — "never fire on this alone", and danger feeds the Sentry's fire threshold.
            if (Fdp.Toolkit.Combat.Armament.Judge(view, self, target) != Fdp.Toolkit.Combat.ArmedJudgement.IdentifiedLethal) return Unarmed;
            // ⭐ CE-2165 V1 — to a VEHICLE, danger is what the target's weapons can do to MY armour (DESIGN_Vehicle_Exposure_Cycle §2).
            if (view is EntityRepository repo && ToMe(repo, self, target, out float toMe)) return toMe;
            return 1f;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2233</c> (CIV-0, the safety gate; 📄 DESIGN_Civilians_And_Crowd_Control.md CV-8) — is <paramref name="target"/> a
        /// NEUTRAL the ground AI must not engage: its <see cref="EntityInfo"/> says <see cref="ForceId.Neutral"/>. ⚠ Neutral is the
        /// enum's ZERO, so an entity with NO <c>EntityInfo</c> is not neutral (unknown = engageable, as for <see cref="Of"/>'s "a target
        /// this node does not hold"). The side is read LIVE (memory stores no side), so a civilian whose side flips to Hostile is
        /// engageable on the next read. Readers: <see cref="Of"/> (danger 0), the threat pick (<c>EqsTacticsNodes.TopThreat</c>) and the
        /// weapon brain's hold (<c>AimAndFireExecutor.HoldOf</c> ⇒ <c>ActionReason.HoldNeutral</c>).
        /// </summary>
        /// <summary>
        /// ⭐ <c>CV-13a</c> (R-2024; 📄 DESIGN_Civilians_And_Crowd_Control.md §4, §5h.2) — is <paramref name="target"/> showing HANDS UP
        /// (<see cref="Fdp.Toolkit.Combat.Gestures.Of"/>, read live) — held like a neutral, even Hostile; ⛔ it never flips his side and never
        /// disarms him (AW-5). Readers: <see cref="Of"/> (danger 0) and the weapon brain's hold (<c>AimAndFireExecutor.HoldOf</c> ⇒
        /// <c>ActionReason.HoldSurrender</c>).
        /// </summary>
        public static bool ShowsHandsUp(ISimulationView view, Entity target)
            => !target.IsNull && Fdp.Toolkit.Combat.Gestures.Of(view, target) == Fdp.Toolkit.Combat.GestureKind.HandsUp;

        public static bool IsNeutral(ISimulationView view, Entity target)
            => !target.IsNull && view.IsAlive(target) && view.HasComponent<EntityInfo>(target)
               && view.GetComponentRO<EntityInfo>(target).ForceId == ForceId.Neutral;

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
            if (world == null || !world.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>()) return false;
            return ToMe(world, world.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(), self, target, out danger);
        }

        /// <summary>⭐ <c>CE-2165</c> V3 — <see cref="ToMe(EntityRepository, Entity, Entity, out float)"/> on ANY view with the TKB handed
        /// in (the EQS solver's snapshot carries none — it reads the live one through <c>CombatTkb.Live</c>). The one implementation.</summary>
        public static bool ToMe(ISimulationView view, Fdp.Interfaces.ITkbDatabase? db, Entity self, Entity target, out float danger)
        {
            danger = 1f;
            if (!MyArmour(view, db, self, out var mine)) return false;
            if (!Fdp.Toolkit.Combat.CombatTkb.TryGetTemplate(view, db, target, out var template)) return false;
            var suite = template.GetDescriptor<Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto>();
            // ⭐ WS-4 — a bare host's mounts are its station weapon's (the danger is its gun, judged from where the host stands)
            if ((suite == null || suite.Mounts.Count == 0) && view is EntityRepository host
                && Fdp.Toolkit.Combat.WeaponSystems.WeaponOf(host, target) is var gun && !gun.IsNull && gun != target
                && Fdp.Toolkit.Combat.CombatTkb.TryGetTemplate(view, db, gun, out var gunTemplate))
                suite = gunTemplate.GetDescriptor<Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto>();
            if (suite == null || suite.Mounts.Count == 0) return false;
            if (!view.HasComponent<SimTransform>(self) || !view.HasComponent<SimTransform>(target)) return false;

            var myTransform = view.GetComponentRO<SimTransform>(self);
            var hisPosition = view.GetComponentRO<SimTransform>(target).Position;
            float distance = System.Numerics.Vector3.Distance(myTransform.Position, hisPosition);
            // ⭐ M1 — the face his round meets, in 3-D: from his eye to my hull's middle (a man on a roof close in reads my TOP).
            var hisEye = hisPosition + new System.Numerics.Vector3(0f, 0f, Fdp.Toolkit.Spatial.Eqs.EqsTerrainSight.Mount(view, target).Standing);
            var myMiddle = myTransform.Position
                           + new System.Numerics.Vector3(0f, 0f, 0.5f * Fdp.Toolkit.Physics.Components.PhysicsColliderReaders.HullHeight(view, self));
            var face = Fdp.Toolkit.Combat.ArmorModel.FacingOfArrival(in myTransform, myMiddle - hisEye);

            float best = 0f;
            foreach (var mount in suite.Mounts)
            {
                float inRange = InRange(mount.Range, distance);
                if (inRange <= 0f) continue;
                best = System.Math.Max(best, CanHurt(db, mount, mine, face, distance) * inRange);
            }
            danger = System.Math.Clamp(best, ArmedHarmless, 1f);
            return true;
        }

        /// <summary>
        /// ⭐ <c>CE-2165</c> V3 — how FAR <paramref name="target"/> can hurt <paramref name="self"/>: the longest <c>Range</c> of his mounts
        /// whose round can hurt me at all — for an ARMOURED vehicle self, one that penetrates my thinnest armoured face (the same
        /// <see cref="CanHurt"/> as <see cref="ToMe(ISimulationView, Fdp.Interfaces.ITkbDatabase?, Entity, Entity, out float)"/>); for any
        /// other self, every mount. 0 when none can, or when no range is known (a mount of range 0 states none).
        /// </summary>
        public static float ReachAgainst(ISimulationView view, Fdp.Interfaces.ITkbDatabase? db, Entity self, Entity target)
        {
            if (!Fdp.Toolkit.Combat.CombatTkb.TryGetTemplate(view, db, target, out var template)) return 0f;
            var suite = template.GetDescriptor<Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto>();
            if (suite == null) return 0f;
            bool armoured = MyArmour(view, db, self, out var mine);
            float reach = 0f;
            foreach (var mount in suite.Mounts)
                if (mount.Range > reach && (!armoured || CanHurt(db, mount, mine, face: null, distance: float.MaxValue) > 0f)) reach = mount.Range;
            return reach;
        }

        /// <summary>
        /// The chance <paramref name="mount"/> defeats <paramref name="mine"/>: the best of the ROUNDS it carries (its loadout, M8 — else its
        /// one round), each with its own penetrator (M2 / M3) against <paramref name="face"/> — a top-attack missile beyond its top-attack
        /// range dives on my TOP (as <c>FireProcessingSystem</c> flies it); none inside a missile's minimum range. A null
        /// <paramref name="face"/> = my THINNEST face for that round (can he hurt me at all). ⚠ An UNKNOWN round (penetration ≤ 0) ignores
        /// armour, exactly as <c>ArmorModel.HitDamage</c> does — never harmless.
        /// </summary>
        private static float CanHurt(Fdp.Interfaces.ITkbDatabase? db, Fdp.Toolkit.Tkb.Domain.WeaponMountDto mount,
            Fdp.Toolkit.Tkb.Domain.CombatPlatformDefDto mine, Fdp.Toolkit.Combat.Facing? face, float distance)
        {
            if (mount.Loadout.Count == 0) return RoundCanHurt(db, mount, unchecked((long)mount.AmmoGuid), mine, face, distance);
            float best = 0f;
            foreach (var load in mount.Loadout)
                best = System.Math.Max(best, RoundCanHurt(db, mount, unchecked((long)load.AmmoGuid), mine, face, distance));
            return best;
        }

        private static float RoundCanHurt(Fdp.Interfaces.ITkbDatabase? db, Fdp.Toolkit.Tkb.Domain.WeaponMountDto mount, long ammo,
            Fdp.Toolkit.Tkb.Domain.CombatPlatformDefDto mine, Fdp.Toolkit.Combat.Facing? face, float distance)
        {
            var (penetration, _) = Fdp.Toolkit.Combat.CombatTkb.RoundOf(db, mount, ammo);
            if (penetration <= 0f) return 1f;
            var penetrator = Fdp.Toolkit.Combat.CombatTkb.PenetratorOf(db, ammo);
            var missile = ammo != 0 && db != null && db.TryGetByType(ammo, out var t) && t != null
                ? t.GetDescriptor<Fdp.Toolkit.Tkb.Domain.MissileDto>() : null;
            if (missile != null && distance < missile.MinRangeM) return 0f;
            float armour;
            if (face == null) armour = Thinnest(mine, penetrator);
            else
            {
                // ⭐ CE-2201 (DESIGN_Missiles_And_Ammo §3) — it dives on my top only where its climb-and-dive fits the range: the launch's own answer
                bool dives = missile != null
                             && Fdp.Toolkit.Combat.Systems.GuidedRoundSystem.TopAttackFits(missile, missile.SpeedMps, distance, 0f, out _);
                armour = Fdp.Toolkit.Combat.ArmorModel.ArmourFor(mine, dives ? Fdp.Toolkit.Combat.Facing.Top : face.Value, penetrator);
            }
            return Fdp.Toolkit.Combat.ArmorModel.PenetrationChance(penetration, armour);
        }

        /// <summary>True when <paramref name="self"/> is a VEHICLE whose TKB declares armour (a soldier's vest is not a vehicle's armour).</summary>
        private static bool MyArmour(ISimulationView view, Fdp.Interfaces.ITkbDatabase? db, Entity self, out Fdp.Toolkit.Tkb.Domain.CombatPlatformDefDto mine)
        {
            mine = null!;
            if (self.IsNull || !view.IsAlive(self) || !Fdp.Toolkit.Spatial.Eqs.VehicleCover.IsVehicle(view, self)) return false;
            if (!Fdp.Toolkit.Combat.CombatTkb.TryGetTemplate(view, db, self, out var template)) return false;
            var platform = template.GetDescriptor<Fdp.Toolkit.Tkb.Domain.CombatPlatformDefDto>();
            if (platform == null || (platform.ArmorFront <= 0f && platform.ArmorSide <= 0f && platform.ArmorRear <= 0f)) return false;
            mine = platform;
            return true;
        }

        /// <summary>The thinnest armoured face of <paramref name="platform"/> against <paramref name="penetrator"/> — front, side, rear and top
        /// (a face of 0 mm is not armoured and does not count).</summary>
        private static float Thinnest(Fdp.Toolkit.Tkb.Domain.CombatPlatformDefDto platform, Fdp.Toolkit.Combat.Penetrator penetrator)
        {
            float t = float.MaxValue;
            foreach (var f in Faces)
            {
                float a = Fdp.Toolkit.Combat.ArmorModel.ArmourFor(platform, f, penetrator);
                if (a > 0f) t = System.Math.Min(t, a);
            }
            return t == float.MaxValue ? 0f : t;
        }

        private static readonly Fdp.Toolkit.Combat.Facing[] Faces =
            { Fdp.Toolkit.Combat.Facing.Front, Fdp.Toolkit.Combat.Facing.Side, Fdp.Toolkit.Combat.Facing.Rear, Fdp.Toolkit.Combat.Facing.Top };

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
            => Fdp.Toolkit.Combat.Injury.StateOf(view, target) >= Fdp.Toolkit.Combat.InjuryState.Incapacitated
               // ⭐ CE-2241 ① (AW-21, R-2027; DESIGN_Civilians_And_Crowd_Control §5h.5) — or he SURRENDERED: every weapon laid down (B-10) AND
               //    his hands up; hands up alone (a feint, a weapon still up) is CV-13a's hold, not out of the fight
               || (Fdp.Toolkit.Combat.WeaponLaying.AllLaidDown(view, target) && ShowsHandsUp(view, target));

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
            // ⭐ CE-3229 (§7.8 A-7) — the TRUTH form: a lethal round on a built-in mount or a working controlled weapon (a bare host's
            //   station gun, WS-4); a dispenser (CE-2188) or non-lethal rounds only (AW-2) arm nobody
            bool armed = Fdp.Toolkit.Combat.Armament.LethalInHand(view, self);
            return health * (armed ? 1f : Unarmed);
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
