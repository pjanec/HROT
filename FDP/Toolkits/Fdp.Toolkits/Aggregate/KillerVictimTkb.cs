using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb;

namespace Fdp.Toolkit.Aggregate
{
    /// <summary>
    /// ⭐⭐ <c>CE-8008</c> (C-1, RV-1 / RV-15) — the killer–victim GENERATOR's TKB ADAPTER: the numbers <see cref="KillerVictimGenerator"/> needs,
    /// read from the SAME TKB the physical weapons fire with — a firer's first <see cref="WeaponMountDto"/> (its dispersion, its round through
    /// <c>CombatTkb.RoundOf</c>, its range, its pacing) and a target's <see cref="CombatPlatformDefDto"/> (its armour per facing, health, the
    /// area it presents). ⭐ The effective rate is DERIVED from the mount's pacing — a magazine aimed round by round
    /// (<c>AimSeconds</c>, fallback 0.8 s) plus its reload — ⛔ never <c>RateOfFire</c> (RV-1); ⚠ RV-1's aim is a rate MEASURED on physical
    /// rails, which replaces this when it lands. ⚠ A missile / HE / no-dispersion row (RV-15) still needs the physical models RUN — the
    /// closed form is exact only for small arms.
    /// </summary>
    public static class KillerVictimTkb
    {
        /// <summary>The aim per aimed round when the mount declares none (s) — <c>EngineFallbacks.AimSeconds</c>'s value.</summary>
        public const float FallbackAimSeconds = 0.8f;
        /// <summary>The radius a hull presents when its type declares no area (m).</summary>
        public const float FallbackHullRadius = 2f;
        /// <summary>⭐ CE-8019 — a soldier's hit points when no personal-weapon soldier declares a platform (<c>CombatPlatformDefDto.MaxHealth</c>).</summary>
        public const float FallbackSoldierHealth = 100f;

        /// <summary>⭐ CE-8019 — the hit points of the soldiers a slot stands for (the personal-weapon soldiers' <c>MaxHealth</c>, the largest),
        /// so a hit's damage, measured on a body, lands on a slot's <see cref="UnitSplits.SlotFullHealth"/> scale.</summary>
        public static float SoldierHealth(ITkbDatabase db, IReadOnlyDictionary<SlotRole, TkbTypeId>? personal)
        {
            float hp = 0f;
            if (personal != null)
                foreach (var kv in personal)
                    if (db.TryGetByType(kv.Value, out var t) && t?.GetDescriptor<CombatPlatformDefDto>() is { MaxHealth: > 0f } p) hp = MathF.Max(hp, p.MaxHealth);
            return hp > 0f ? hp : FallbackSoldierHealth;
        }

        /// <summary>⭐ CE-8009 — each zone's share and its damage ÷ the mean, for the personal soldier's own rifle against his own zones
        /// (<see cref="KillerVictimTable.ManZoneFactor"/>). Nothing when the zones or the rifle are unknown (the mean on every hit).</summary>
        private static void ZoneFactors(ITkbDatabase db, IReadOnlyDictionary<SlotRole, TkbTypeId>? personal, ManZone[]? zones, KillerVictimTable table)
        {
            if (zones == null || zones.Length == 0 || personal == null) return;
            TkbTypeId soldier = personal.TryGetValue(SlotRole.Rifleman, out var r) ? r : default;
            if ((long)soldier == 0) foreach (var kv in personal) { soldier = kv.Value; break; }
            if (!TryWeapon(db, soldier, out var w)) return;
            var share = new float[zones.Length]; var dmg = new float[zones.Length];
            float total = 0f, mean = 0f;
            for (int i = 0; i < zones.Length; i++) total += zones[i].Share;
            if (total <= 0f) return;
            for (int i = 0; i < zones.Length; i++)
            {
                share[i] = zones[i].Share / total;
                dmg[i] = ArmorModel.HitDamage(w.Penetration, w.DamagePerHit, zones[i].Armour) * zones[i].Multiplier;
                mean += share[i] * dmg[i];
            }
            if (mean <= 0f) return;
            for (int i = 0; i < dmg.Length; i++) dmg[i] /= mean;
            table.ManZoneShare = share;
            table.ManZoneFactor = dmg;
        }

        /// <summary>⭐ CE-8009 — the SLOT health below which a man is out of the fight: the personal soldiers' <c>Gen.Injury</c>
        /// <c>IncapacitatedBelow</c> (the largest) × <see cref="UnitSplits.SlotFullHealth"/> — the line the physical injury model takes a man out
        /// at. 0 when none declares one (he fights until dead, as physically).</summary>
        public static float SoldierOutBelow(ITkbDatabase db, IReadOnlyDictionary<SlotRole, TkbTypeId>? personal)
        {
            float frac = 0f;
            if (personal != null)
                foreach (var kv in personal)
                    if (db.TryGetByType(kv.Value, out var t) && t?.GetDescriptor<InjuryDefDto>() is { IncapacitatedBelow: > 0f } inj) frac = MathF.Max(frac, inj.IncapacitatedBelow);
            return frac * UnitSplits.SlotFullHealth;
        }

        /// <summary>⭐ CE-8009 — the body zones of the soldier a slot stands for (the Rifleman's, else the first personal soldier's), as the physical
        /// hit model resolves them (<c>BodyZones.Of</c>: his own zones, his platform armour as the torso's, the DIS profile, the engine person) —
        /// so body armour stops the same share of a round in both resolutions. Null when unknown (⇒ the unarmoured mean).</summary>
        public static ManZone[]? SoldierZones(ITkbDatabase db, IReadOnlyDictionary<SlotRole, TkbTypeId>? personal)
        {
            if (personal == null || personal.Count == 0) return null;
            TkbTypeId type = personal.TryGetValue(SlotRole.Rifleman, out var r) ? r : default;
            if ((long)type == 0) foreach (var kv in personal) { type = kv.Value; break; }
            if (!db.TryGetByType(type, out var t) || t == null) return null;
            var body = Fdp.Toolkit.Physics.BodyParts.BodyZones.Of(t);
            if (body == null) return null;
            float platform = t.GetDescriptor<CombatPlatformDefDto>()?.ArmorFront ?? 0f;
            var zones = new List<ManZone>(body.Zones.Count);
            foreach (var z in body.Zones)
            {
                float share = z.Name.ToLowerInvariant() switch
                {
                    "head" => KillerVictimGenerator.HeadShare, "torso" => KillerVictimGenerator.TorsoShare,
                    "arm" => KillerVictimGenerator.ArmShare, "leg" => KillerVictimGenerator.LegShare, _ => 0f,
                };
                if (share <= 0f) continue;
                float armour = z.ArmourOn(Facing.Front);
                zones.Add(new ManZone(share, z.Multiplier, float.IsNaN(armour) ? platform : armour));
            }
            return zones.Count > 0 ? zones.ToArray() : null;
        }

        /// <summary>⭐ CE-8009 — how far the soldiers a slot stands for SEE with the naked eye (the personal soldiers' <c>VisionRange</c>, the
        /// largest; 0 = unknown, no cap). The physical soldier sees nothing past it, so a stochastic eye looks no further (one source).</summary>
        public static float SoldierEyeReach(ITkbDatabase db, IReadOnlyDictionary<SlotRole, TkbTypeId>? personal)
        {
            float reach = 0f;
            if (personal != null)
                foreach (var kv in personal)
                    if (db.TryGetByType(kv.Value, out var t) && t?.GetDescriptor<SensorCapabilitiesDto>() is { VisionRange: > 0f } c) reach = MathF.Max(reach, c.VisionRange);
            return reach;
        }

        /// <summary>The effective aimed rate (rounds / s) of <paramref name="mount"/>: a magazine at one aimed round per aim, then the reload.</summary>
        public static float EffectiveRate(WeaponMountDto mount)
        {
            float aim = mount.AimSeconds > 0f ? mount.AimSeconds : FallbackAimSeconds;
            int mag = Math.Max(1, mount.MagazineSize);
            float cycle = mag * aim + MathF.Max(0f, mount.ReloadSeconds);
            return cycle <= 0f ? 0f : mag / cycle;
        }

        /// <summary>The firing numbers of TKB type <paramref name="type"/> (its first mount), or false when it has none.</summary>
        public static bool TryWeapon(ITkbDatabase db, TkbTypeId type, out WeaponNumbers numbers)
        {
            numbers = default;
            if (!db.TryGetByType(type, out var t) || t == null) return false;
            var suite = t.GetDescriptor<WeaponSuiteDto>();
            if (suite == null || suite.Mounts.Count == 0) return false;
            var m = suite.Mounts[0];
            var (pen, dmg) = CombatTkb.RoundOf(db, m, 0);
            if (pen <= 0f) pen = m.Penetration;
            if (dmg <= 0f) dmg = m.DamagePerHit;
            numbers = new WeaponNumbers(type, m.DispersionMils, pen, dmg, EffectiveRate(m), m.Range > 0f ? m.Range : 600f,
                                        CombatTkb.PenetratorOf(db, unchecked((long)m.AmmoGuid)));
            return true;
        }

        /// <summary>The target numbers of hull type <paramref name="type"/>, or false when it has no platform record.</summary>
        public static bool TryHull(ITkbDatabase db, TkbTypeId type, out TargetNumbers numbers)
        {
            numbers = default;
            if (!db.TryGetByType(type, out var t) || t == null) return false;
            var p = t.GetDescriptor<CombatPlatformDefDto>();
            if (p == null) return false;
            float radius = p.PresentedAreaM2 > 0f ? MathF.Sqrt(p.PresentedAreaM2 / MathF.PI) : FallbackHullRadius;
            numbers = new TargetNumbers(type, radius, p.MaxHealth, p.ArmorFront, p.ArmorSide, p.ArmorRear, p.ArmorTop, ArmourBottom: p.ArmorBottom);   // ⭐ CE-8017 — MS-T1's belly (CE-4624)
            return true;
        }

        /// <summary>
        /// ⭐ The table for the types a scenario's units hold: every <paramref name="firers"/> type (the held systems, the soldiers that carry the
        /// personal weapons) against the men and every <paramref name="hulls"/> type; <paramref name="personal"/> maps a slot role to the soldier
        /// type whose weapon it fires. Types with no weapon / no platform record are skipped (said in <paramref name="skipped"/>).
        /// </summary>
        public static KillerVictimTable Build(ITkbDatabase db, IEnumerable<TkbTypeId> firers, IEnumerable<TkbTypeId> hulls,
                                              IReadOnlyDictionary<SlotRole, TkbTypeId>? personal, List<string>? skipped = null)
        {
            var weapons = new List<WeaponNumbers>();
            foreach (var f in firers)
                if (TryWeapon(db, f, out var w)) weapons.Add(w);
                else skipped?.Add($"{(long)f}: no weapon suite");
            var zones = SoldierZones(db, personal);
            var targets = new List<TargetNumbers> { new(KillerVictimTable.Personnel, 0.3f, UnitSplits.SlotFullHealth, BodyHealth: SoldierHealth(db, personal), Zones: zones) };   // ⭐ CE-8019
            foreach (var h in hulls)
                if (TryHull(db, h, out var t)) targets.Add(t);
                else skipped?.Add($"{(long)h}: no platform record");
            var table = KillerVictimGenerator.Build(weapons, targets);
            if (personal != null) foreach (var kv in personal) table.SetPersonalWeapon(kv.Key, kv.Value);
            table.ManOutBelow = SoldierOutBelow(db, personal);   // ⭐ CE-8009 — incapacitated is out of the fight
            ZoneFactors(db, personal, zones, table);             // ⭐ CE-8009 — a hit lands on ONE zone
            return table;
        }

        /// <summary>⭐ The slot roles' soldier types (host-supplied: the TKB composite's slot → soldier type, CE-8013). Null = the slots fire no
        /// personal weapon — only the held systems (said: the silent-default rule).</summary>
        public static IReadOnlyDictionary<SlotRole, TkbTypeId>? PersonalSoldiers { get; set; }

        /// <summary>
        /// ⭐ <c>CE-8008</c> — the table for <paramref name="world"/>, built from ITS TKB over the types its stochastic units hold (serial; called
        /// once, by the fire phase, when no table is in force). Null when the world has no TKB or no stochastic unit holds anything.
        /// </summary>
        public static KillerVictimTable? LoadFor(EntityRepository world)
        {
            if (!world.HasSingletonManaged<ITkbDatabase>() || !world.IsComponentTypeRegistered<UnitHoldings>()) return null;
            var db = world.GetSingletonManaged<ITkbDatabase>();
            if (db == null) return null;
            var firers = new HashSet<TkbTypeId>();
            var hulls = new HashSet<TkbTypeId>();
            foreach (var e in world.Query().With<UnitHoldings>().Build())
            {
                ref readonly var h = ref world.GetComponentRO<UnitHoldings>(e);
                for (int i = 0; i < h.Count; i++)
                {
                    if (h.Items[i].Kind == HoldingKind.Sensor) continue;
                    firers.Add(h.Items[i].Type);
                    if (h.Items[i].Kind == HoldingKind.Vehicle) hulls.Add(h.Items[i].Type);
                }
            }
            if (PersonalSoldiers != null) foreach (var kv in PersonalSoldiers) firers.Add(kv.Value);
            if (firers.Count == 0) return null;
            return Build(db, firers, hulls, PersonalSoldiers);
        }
    }
}
