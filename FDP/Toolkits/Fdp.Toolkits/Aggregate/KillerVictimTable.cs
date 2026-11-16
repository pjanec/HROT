using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.Toolkit.Combat;

namespace Fdp.Toolkit.Aggregate
{
    /// <summary>
    /// ⭐⭐ <c>CE-8007</c> (ST-3, C-1, RV-1 / RV-2 / RV-15; docs/designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md §18) — one
    /// row of THE killer–victim table: what one firing system of a weapon type does to one target class, per second of EFFECTIVE fire.
    /// </summary>
    public sealed record KvEntry
    {
        /// <summary>P(hit) per range band (<see cref="KillerVictimTable.BandMetres"/> each, band i covers [i·band, (i+1)·band)).</summary>
        public required float[] PHitByBand { get; init; }
        /// <summary>The expected damage of one hit (health points): <c>ArmorModel.HitDamage</c> × the mean zone factor for men; the facing
        /// mix for a hull.</summary>
        public float DamagePerHit { get; init; }
        /// <summary>For a vehicle target: the share of hits that kill it (K-kill) and that immobilise it (M-kill). 0 for men.</summary>
        public float KKillShare { get; init; }
        public float MKillShare { get; init; }
        /// <summary>⭐ The EFFECTIVE aimed rate (rounds / s) of one system — ⛔ never the cyclic <c>RateOfFire</c> (RV-1).</summary>
        public float EffectiveRate { get; init; }
        /// <summary>How suited the weapon is to the class (0..1) — the weight of the fire split (C-2, RV-4).</summary>
        public float Suitability { get; init; }
        /// <summary>The farthest it engages (m).</summary>
        public float MaxRange { get; init; }
        /// <summary>The ⚠ CALIBRATED-or-DERIVED tag per column, for the review: "derived:HitModel+ArmorModel" or "calibrated".</summary>
        public string Source { get; init; } = "derived";

        /// <summary>P(hit) at <paramref name="range"/>; 0 past <see cref="MaxRange"/>.</summary>
        public float PHitAt(float range)
        {
            if (range > MaxRange || PHitByBand.Length == 0) return 0f;
            int b = Math.Clamp((int)(range / KillerVictimTable.BandMetres), 0, PHitByBand.Length - 1);
            return PHitByBand[b];
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-8007</c> (C-1) — THE killer–victim table: (weapon type, target class) → <see cref="KvEntry"/>; the target class of men is
    /// <see cref="Personnel"/>, of a vehicle its TKB platform type. ⭐ GENERATED offline (<see cref="KillerVictimGenerator"/>), committed as a
    /// data asset, LOADED ONCE (<see cref="Current"/>) — the same numbers as the physical models by construction (RV-15).
    /// Also maps each <see cref="SlotRole"/> to its personal weapon type (the slots carry no weapon).
    /// </summary>
    public sealed class KillerVictimTable
    {
        /// <summary>The range band (m).</summary>
        public const float BandMetres = 100f;
        /// <summary>The target class of men.</summary>
        public static readonly TkbTypeId Personnel = new(0);

        private readonly Dictionary<(long Weapon, long Target), KvEntry> _rows = new();
        private readonly TkbTypeId[] _personal = new TkbTypeId[16];

        /// <summary>The table in force (null = no stochastic combat — the phases do nothing). Set once at load by the host.</summary>
        public static KillerVictimTable? Current { get; set; }

        public int Count => _rows.Count;

        public void Set(TkbTypeId weapon, TkbTypeId target, KvEntry e) => _rows[(weapon, target)] = e;

        public bool TryGet(TkbTypeId weapon, TkbTypeId target, out KvEntry entry)
        {
            if (_rows.TryGetValue((weapon, target), out var e)) { entry = e; return true; }
            entry = null!;
            return false;
        }

        /// <summary>The personal weapon a slot of <paramref name="role"/> fires (0 = none).</summary>
        public TkbTypeId PersonalWeapon(SlotRole role) => (int)role < _personal.Length ? _personal[(int)role] : default;

        public void SetPersonalWeapon(SlotRole role, TkbTypeId weapon) => _personal[(int)role] = weapon;

        // ── the data asset ───────────────────────────────────────────────────────────────────────────────────────────────────
        private sealed record RowDto(long Weapon, long Target, KvEntry Entry);
        private sealed record FileDto(int Version, Dictionary<string, long> PersonalWeapons, List<RowDto> Rows);
        private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

        /// <summary>The asset text (invariant: JSON numbers).</summary>
        public string ToJson()
        {
            var personal = new Dictionary<string, long>();
            for (int i = 0; i < _personal.Length; i++) if ((long)_personal[i] != 0) personal[((SlotRole)i).ToString()] = _personal[i];
            var rows = new List<RowDto>();
            foreach (var kv in _rows) rows.Add(new RowDto(kv.Key.Weapon, kv.Key.Target, kv.Value));
            rows.Sort((a, b) => a.Weapon != b.Weapon ? a.Weapon.CompareTo(b.Weapon) : a.Target.CompareTo(b.Target));
            return JsonSerializer.Serialize(new FileDto(1, personal, rows), s_json);
        }

        public static KillerVictimTable FromJson(string json)
        {
            var dto = JsonSerializer.Deserialize<FileDto>(json, s_json) ?? throw new InvalidOperationException("empty killer-victim table");
            var t = new KillerVictimTable();
            foreach (var kv in dto.PersonalWeapons) t.SetPersonalWeapon(Enum.Parse<SlotRole>(kv.Key), new TkbTypeId(kv.Value));
            foreach (var r in dto.Rows) t.Set(new TkbTypeId(r.Weapon), new TkbTypeId(r.Target), r.Entry);
            return t;
        }
    }

    /// <summary>The weapon numbers the generator reads (from the TKB: the mount's dispersion, its round's penetration and damage).</summary>
    public readonly record struct WeaponNumbers(TkbTypeId Type, float DispersionMils, float Penetration, float DamagePerHit,
                                                float EffectiveRoundsPerSecond, float MaxRange, Penetrator Penetrator = Penetrator.Kinetic,
                                                bool FromBelow = false);   // ⭐ CE-8017: a mine — its rows are BOTTOM attacks (KillerVictimGenerator.BottomAttack)

    /// <summary>The target numbers the generator reads: men (<see cref="KillerVictimTable.Personnel"/>) or a hull (its armour per facing,
    /// its health, the radius it presents) and the aspect mix it is seen from.</summary>
    public readonly record struct TargetNumbers(TkbTypeId Type, float Radius, float Health, float ArmourFront = 0f, float ArmourSide = 0f,
                                                float ArmourRear = 0f, float ArmourTop = 0f, float FrontShare = 0.5f, float SideShare = 0.4f,
                                                float RearShare = 0.1f, float ArmourBottom = 0f)
    {
        /// <summary>⭐ The belly a bottom attack meets — 0 ⇒ the rear (the rule <c>ArmorModel.ArmourFor(Facing.Bottom)</c> uses, CE-4624 MR-10).</summary>
        public float BellyArmour => ArmourBottom > 0f ? ArmourBottom : ArmourRear;

        public bool IsPersonnel => (long)Type == 0;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-8007</c> (C-1, RV-15) — the killer–victim table GENERATOR harness: small arms in CLOSED FORM from the physical models —
    /// P(hit) per band = <c>HitModel.HitChance(σ, range, radius)</c> (σ = the mount's dispersion, standing, unsuppressed); the damage per hit
    /// = <c>ArmorModel.HitDamage(penetration, damage, armour)</c> over the facing mix (a man: × <see cref="MeanZoneFactor"/>). ⚠ Guided
    /// missiles, HE against men and the no-dispersion mounts are to be generated by RUNNING the physical models (RV-15) — the harness takes
    /// their measured rows through <see cref="KillerVictimTable.Set"/>. ⭐ The BOTTOM row (<see cref="BottomAttack"/>, a mine under the hull) is
    /// generated against <see cref="TargetNumbers.BellyArmour"/> — the TKB's <c>ArmorBottom</c> (minefield MS-T1, CE-4624), 0 ⇒ the rear, as
    /// <c>ArmorModel.ArmourFor(Facing.Bottom)</c>.
    /// </summary>
    public static class KillerVictimGenerator
    {
        /// <summary>CALIBRATED — where a man is hit (head 0.1 · torso 0.4 · arms 0.2 · legs 0.3) × the zone multipliers (4 · 1 · 0.4 · 0.5).</summary>
        public const float MeanZoneFactor = 0.1f * 4f + 0.4f * 1f + 0.2f * 0.4f + 0.3f * 0.5f;

        /// <summary>CALIBRATED — the K-kill share of a penetrating hit on a hull; the rest of the damage immobilises in this share.</summary>
        public const float KKillOfPenetrating = 0.35f, MKillOfPenetrating = 0.25f;

        /// <summary>One row by closed form.</summary>
        public static KvEntry SmallArms(in WeaponNumbers w, in TargetNumbers t)
        {
            int bands = Math.Max(1, (int)MathF.Ceiling(w.MaxRange / KillerVictimTable.BandMetres));
            var p = new float[bands];
            float sigma = w.DispersionMils / 1000f;
            for (int b = 0; b < bands; b++)
            {
                float range = (b + 0.5f) * KillerVictimTable.BandMetres;
                p[b] = HitModel.HitChance(sigma, range, t.Radius);
            }
            float damage, kk = 0f, mk = 0f, suit;
            if (t.IsPersonnel)
            {
                damage = ArmorModel.HitDamage(w.Penetration, w.DamagePerHit, 0f) * MeanZoneFactor;
                suit = 1f;
            }
            else
            {
                float wPen = w.Penetration, wDmg = w.DamagePerHit;
                float pen = Mix(t, f => ArmorModel.PenetrationChance(wPen, f));
                damage = Mix(t, f => ArmorModel.HitDamage(wPen, wDmg, f));
                kk = pen * KKillOfPenetrating;
                mk = pen * MKillOfPenetrating;
                suit = pen;   // a round that never penetrates is wasted on the hull
            }
            return new KvEntry
            {
                PHitByBand = p, DamagePerHit = damage, KKillShare = kk, MKillShare = mk, EffectiveRate = w.EffectiveRoundsPerSecond,
                Suitability = suit, MaxRange = w.MaxRange, Source = "derived:HitModel.HitChance+ArmorModel.HitDamage; rate calibrated",
            };
        }

        /// <summary>
        /// ⭐ CE-8007 / CE-8017 — the BOTTOM row: a burst UNDER the target (a mine). No dispersion — the weapon is where the target is, so P(hit)
        /// is 1 in band 0 and nothing beyond; the damage meets the belly (<see cref="TargetNumbers.BellyArmour"/>); a man takes the round's
        /// damage whole (the burst is below him, no zone mix). The rate is one detonation (the weapon is spent).
        /// </summary>
        public static KvEntry BottomAttack(in WeaponNumbers w, in TargetNumbers t)
        {
            float damage, kk = 0f, mk = 0f, suit = 1f;
            if (t.IsPersonnel) damage = ArmorModel.HitDamage(w.Penetration, w.DamagePerHit, 0f);
            else
            {
                float pen = ArmorModel.PenetrationChance(w.Penetration, t.BellyArmour);
                damage = ArmorModel.HitDamage(w.Penetration, w.DamagePerHit, t.BellyArmour);
                kk = pen * KKillOfPenetrating;
                mk = pen * MKillOfPenetrating + (1f - pen) * MKillOfPenetrating;   // a belly blast immobilises even when it does not penetrate
                suit = MathF.Max(pen, 0.05f);
            }
            return new KvEntry
            {
                PHitByBand = new[] { 1f }, DamagePerHit = damage, KKillShare = kk, MKillShare = mk, EffectiveRate = 1f, Suitability = suit,
                MaxRange = KillerVictimTable.BandMetres, Source = "derived:ArmorModel.HitDamage(belly); bottom attack",
            };
        }

        /// <summary>The facing mix of a per-facing number (front / side / rear by the target's shares).</summary>
        private static float Mix(TargetNumbers t, Func<float, float> perArmour)
        {
            float sum = t.FrontShare + t.SideShare + t.RearShare;
            if (sum <= 0f) return perArmour(t.ArmourFront);
            return (t.FrontShare * perArmour(t.ArmourFront) + t.SideShare * perArmour(t.ArmourSide) + t.RearShare * perArmour(t.ArmourRear)) / sum;
        }

        /// <summary>The whole table over <paramref name="weapons"/> × <paramref name="targets"/>, closed form.</summary>
        public static KillerVictimTable Build(IEnumerable<WeaponNumbers> weapons, IEnumerable<TargetNumbers> targets)
        {
            var table = new KillerVictimTable();
            var ts = new List<TargetNumbers>(targets);
            foreach (var w in weapons)
                foreach (var t in ts)
                    table.Set(w.Type, t.Type, w.FromBelow ? BottomAttack(in w, in t) : SmallArms(in w, in t));
            return table;
        }
    }
}
