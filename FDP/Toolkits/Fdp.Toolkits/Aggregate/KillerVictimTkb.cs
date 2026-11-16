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
            numbers = new TargetNumbers(type, radius, p.MaxHealth, p.ArmorFront, p.ArmorSide, p.ArmorRear, p.ArmorTop);
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
            var targets = new List<TargetNumbers> { new(KillerVictimTable.Personnel, 0.3f, UnitSplits.SlotFullHealth) };
            foreach (var h in hulls)
                if (TryHull(db, h, out var t)) targets.Add(t);
                else skipped?.Add($"{(long)h}: no platform record");
            var table = KillerVictimGenerator.Build(weapons, targets);
            if (personal != null) foreach (var kv in personal) table.SetPersonalWeapon(kv.Key, kv.Value);
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
