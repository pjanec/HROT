using System;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// ⭐ CE-1042 E1 — the built-in realism EFFECT types (docs/DESIGN_Visual_Effects.md VE-C, VE-D): a muzzle flash, an explosion and a
    /// decal in small / medium / large, and a tracer — each a TKB type whose <see cref="EffectVisualDto"/> says how it looks. The two
    /// built-in munitions name theirs (<see cref="MunitionEffectsDto"/>); a mount that loads no ammo type (rifles, cannons today)
    /// gets the default for its calibre class (<see cref="EffectsFor"/>).
    /// <para>Effect entities are local presentation — made on every map host from the fire / hit events, never replicated or saved
    /// (VE-A, VE-B) — so these ids never reach the wire; they are still permanent like every TKB id. Hidden from the palette.</para>
    /// </summary>
    public static class EffectTkbCatalog
    {
        public const long MuzzleFlashSmall = 7101, MuzzleFlashMedium = 7102, MuzzleFlashLarge = 7103;
        public const long ExplosionSmall = 7111, ExplosionMedium = 7112, ExplosionLarge = 7113;
        /// <summary>⭐ CE-1231 (docs/DESIGN_Visual_Effects.md §11 CH-1, R-1016) — the burst of 5 kg of explosive and more: an IED, a suicide vest, a
        /// car bomb, a 155 mm shell, a bomb.</summary>
        public const long ExplosionHuge = 7114;
        /// <summary>⭐ CE-4145 (docs/DESIGN_Visual_Effects.md §12 K-2) — the dust puff of an INERT (kinetic) round: a 12.7 mm / 25 mm AP (medium), an
        /// APFSDS (large). Not an explosion — dust.</summary>
        public const long ImpactPuffMedium = 7115, ImpactPuffLarge = 7116;
        public const long DecalSmall = 7121, DecalMedium = 7122, DecalLarge = 7123;
        /// <summary>⭐ CE-4145 (§12 K-2) — the mark of an inert round: a bullet hole (8 cm for a 12.7 mm) and a gouge (30 cm for an APFSDS).</summary>
        public const long DecalHoleMedium = 7124, DecalGouge = 7125;
        public const long Tracer = 7131;
        /// <summary>⭐ W3 (docs/DESIGN_Visual_Effects.md §7 VE-P) — what a destroyed vehicle burns with: flames ≈ 60 s, smoke ≈ 180 s
        /// fading (U6: "Durations ok").</summary>
        public const long Fire = 7141, Smoke = 7151;

        /// <summary>⭐ CE-1090 (docs/DESIGN_Map_3D_Mode.md §6n N-4) — the dust thrown BEHIND a recoilless launcher, and the ground blast round a
        /// howitzer when it fires.</summary>
        public const long Backblast = 7161, GunBlast = 7162;

        /// <summary>⭐ CE-1115 (docs/DESIGN_Visual_Effects.md §9) — a decoy flare burning as it falls, and a chaff cloud.</summary>
        public const long Flare = 7171, ChaffCloud = 7172;

        /// <summary>The calibre classes the default effect set is chosen by, from a round's damage per hit (or, ⭐ CE-1231, its explosive).</summary>
        public enum CalibreClass : byte { Small, Medium, Large, Huge }

        /// <summary>Below this damage per hit a round is SMALL calibre (rifles: 25).</summary>
        public const float MediumDamageFrom = 50f;

        /// <summary>From this damage per hit a round is LARGE calibre (tank guns 1100–1200, ATGM 2000; the 25 mm cannon 60 and the
        /// RPG 400 are medium).</summary>
        public const float LargeDamageFrom = 500f;

        /// <summary>⭐ CE-1231 (§11 CH-1) — from these explosive masses (kg) a burst is medium, large, huge: chosen so a round that HITS (sized by
        /// its damage) and the same round that MISSES (sized by its warhead) look alike — the 25 mm HEI (0.03 kg, damage 60) medium both ways,
        /// the RPG (0.73 kg, 400) medium, the 120 mm HEAT (1.6 kg, 1200) and the TOW (3.1 kg, 2000) large.</summary>
        public const float MediumExplosiveFromKg = 0.01f, LargeExplosiveFromKg = 1f, HugeExplosiveFromKg = 5f;

        /// <summary>⭐ CE-1231 (§11 CH-1) — the class of a burst of <paramref name="explosiveKg"/> of explosive.</summary>
        public static CalibreClass ClassOfExplosive(float explosiveKg)
            => explosiveKg >= HugeExplosiveFromKg ? CalibreClass.Huge : explosiveKg >= LargeExplosiveFromKg ? CalibreClass.Large
             : explosiveKg >= MediumExplosiveFromKg ? CalibreClass.Medium : CalibreClass.Small;

        /// <summary>The calibre class of a round with <paramref name="damagePerHit"/> (0 = unknown ⇒ small).</summary>
        public static CalibreClass ClassOf(float damagePerHit)
            => damagePerHit >= LargeDamageFrom ? CalibreClass.Large : damagePerHit >= MediumDamageFrom ? CalibreClass.Medium : CalibreClass.Small;

        /// <summary>The default effect set of a calibre class.</summary>
        public static MunitionEffectsDto DefaultFor(CalibreClass c) => c switch
        {
            CalibreClass.Huge => new MunitionEffectsDto { MuzzleFlash = MuzzleFlashLarge, Explosion = ExplosionHuge, Decal = DecalLarge, Tracer = Tracer },   // the crater
            CalibreClass.Large => new MunitionEffectsDto { MuzzleFlash = MuzzleFlashLarge, Explosion = ExplosionLarge, Decal = DecalLarge, Tracer = Tracer },
            CalibreClass.Medium => new MunitionEffectsDto { MuzzleFlash = MuzzleFlashMedium, Explosion = ExplosionMedium, Decal = DecalMedium, Tracer = Tracer },
            _ => new MunitionEffectsDto { MuzzleFlash = MuzzleFlashSmall, Explosion = ExplosionSmall, Decal = DecalSmall },
        };

        /// <summary>⭐ <c>CE-4145</c> (§12 K-2, K-3) — the set of an INERT round of calibre class <paramref name="c"/>: the class's muzzle flash and tracer
        /// (K-3), but dust and a hole instead of its fireball and crater. Small is the rifle default, already dust.</summary>
        public static MunitionEffectsDto KineticFor(CalibreClass c) => c switch
        {
            CalibreClass.Large or CalibreClass.Huge => DefaultFor(CalibreClass.Large) with { Explosion = ImpactPuffLarge, Decal = DecalGouge },
            CalibreClass.Medium => DefaultFor(CalibreClass.Medium) with { Explosion = ImpactPuffMedium, Decal = DecalHoleMedium },
            _ => DefaultFor(CalibreClass.Small),
        };

        /// <summary>⭐ <c>CE-4145</c> (§12 K-1) — a round whose ammo type the TKB KNOWS and whose warhead carries no explosive.</summary>
        public static bool IsKinetic(ITkbDatabase? tkb, long ammoType)
            => tkb != null && ammoType != 0 && tkb.TryGetByType(ammoType, out _) && ExplosiveKgOf(tkb, ammoType) <= 0f;

        /// <summary>
        /// ⭐ VE-D — the effects a round makes: its AMMO type's <see cref="MunitionEffectsDto"/> when it has one, else the default of
        /// its calibre class. The ONE answer the spawn system asks (E2).
        /// <para>⭐⭐ CE-1231 (docs/DESIGN_Visual_Effects.md §11 CH-1, R-1016) — a round that comes with NO damage (an off-target burst, any
        /// burst from another node — neither carries one — and a charge, whose mount has none) bursts and marks by its warhead's EXPLOSIVE
        /// MASS (<see cref="Fdp.Toolkit.Tkb.Parameters.ParameterResolver.Warhead(ITkbDatabase?, long, Fdp.Toolkit.Tkb.Reference.ReferenceLibrary?)"/>,
        /// the one warhead lookup); its flash and tracer stay those of its damage class. ⛔ Was: every such burst drew the 0.8 m puff — the IED,
        /// the vest, a 155 mm shell, a bomb.</para>
        /// </summary>
        public static MunitionEffectsDto EffectsFor(ITkbDatabase? tkb, long ammoType, float damagePerHit)
        {
            if (tkb != null && ammoType != 0 && tkb.TryGetByType(ammoType, out var ammo) && ammo?.GetDescriptor<MunitionEffectsDto>() is { } set)
                return set;
            // ⭐ CE-4145 (§12 K-1) — an INERT round never explodes, whatever its damage: dust and a hole (an unknown ammo keeps the damage default)
            if (damagePerHit > 0f && IsKinetic(tkb, ammoType)) return KineticFor(ClassOf(damagePerHit));
            var byDamage = DefaultFor(ClassOf(damagePerHit));
            if (damagePerHit > 0f) return byDamage;
            float kg = ExplosiveKgOf(tkb, ammoType);
            if (kg <= 0f) return byDamage;
            var burst = DefaultFor(ClassOfExplosive(kg));
            return byDamage with { Explosion = burst.Explosion, Decal = burst.Decal };
        }

        /// <summary>⭐ CE-1231 — the explosive mass (kg) of a munition's warhead; 0 when it has none (a kinetic round) or is unknown.</summary>
        public static float ExplosiveKgOf(ITkbDatabase? tkb, long ammoType)
            => tkb == null || ammoType == 0 ? 0f : Fdp.Toolkit.Tkb.Parameters.ParameterResolver.Warhead(tkb, ammoType).Warhead?.ExplosiveKg ?? 0f;

        /// <summary>The built-in effect types' looks — what <see cref="RegisterAll"/> puts in the TKB, and what
        /// <see cref="VisualOf"/> answers for a host with no TKB (the replay browser — PLAN D4).</summary>
        public static readonly System.Collections.Generic.IReadOnlyDictionary<long, (string Name, EffectVisualDto Visual)> BuiltIn =
            new System.Collections.Generic.Dictionary<long, (string, EffectVisualDto)>
            {
                [MuzzleFlashSmall]  = ("Muzzle flash small",  V(EffectKind.MuzzleFlash, 0.06f, 0.5f, "#FFE2A0", 0.03f)),
                [MuzzleFlashMedium] = ("Muzzle flash medium", V(EffectKind.MuzzleFlash, 0.10f, 1.4f, "#FFD27F", 0.05f)),
                [MuzzleFlashLarge]  = ("Muzzle flash large",  V(EffectKind.MuzzleFlash, 0.18f, 4.0f, "#FFC060", 0.08f)),
                [ExplosionSmall]    = ("Explosion small",     V(EffectKind.Explosion, 0.35f, 0.8f, "#C8B89A", 0.2f)),   // an impact puff
                [ExplosionMedium]   = ("Explosion medium",    V(EffectKind.Explosion, 0.9f, 5f, "#FF8A30", 0.4f)),
                [ExplosionLarge]    = ("Explosion large",     V(EffectKind.Explosion, 1.6f, 12f, "#FF6A20", 0.7f)),
                [ExplosionHuge]     = ("Explosion huge",      V(EffectKind.Explosion, 2.2f, 20f, "#F07028", 1.0f)),   // ⭐ CE-1231 — an IED, a vest, a car bomb
                [ImpactPuffMedium]  = ("Impact puff medium",  V(EffectKind.Explosion, 0.45f, 1.4f, "#C8B89A", 0.2f)),   // ⭐ CE-4145 — an inert 12.7 / 25 mm round: dust
                [ImpactPuffLarge]   = ("Impact puff large",   V(EffectKind.Explosion, 0.7f, 3f, "#B8A88A", 0.3f)),      // ⭐ CE-4145 — an APFSDS: a bigger cloud of dust
                [DecalSmall]        = ("Decal small",         V(EffectKind.Decal, 60f, 0.06f, "#1E1A16C0", 10f)),       // a bullet mark — ~5 cm across (CE-1336)
                [DecalMedium]       = ("Decal medium",        V(EffectKind.Decal, 180f, 1.8f, "#18140FD0", 30f)),
                [DecalLarge]        = ("Decal large",         V(EffectKind.Decal, 300f, 4.5f, "#120F0CE0", 60f)),       // a crater
                [DecalHoleMedium]   = ("Decal hole medium",   V(EffectKind.Decal, 60f, 0.08f, "#1E1A16C0", 10f)),      // ⭐ CE-4145 — a 12.7 mm hole, 8 cm
                [DecalGouge]        = ("Decal gouge",         V(EffectKind.Decal, 60f, 0.3f, "#1E1A16C0", 10f)),       // ⭐ CE-4145 — an APFSDS gouge, 30 cm
                [Tracer]            = ("Tracer",              V(EffectKind.Tracer, 0.25f, 0.12f, "#FFB040", 0.1f)),
                [Fire]              = ("Wreck fire",          V(EffectKind.Fire, 60f, 3.2f, "#FF8A2A", 15f)),          // sized for a tank, scaled by the wreck
                [Smoke]             = ("Wreck smoke",         V(EffectKind.Smoke, 180f, 3f, "#2E2B28B0", 60f)),
                [Backblast]         = ("Backblast",           V(EffectKind.Explosion, 0.8f, 2.6f, "#D8D0C0", 0.6f)),     // ⭐ CE-1090 — a dust cloud
                [GunBlast]          = ("Gun blast",           V(EffectKind.Explosion, 1.1f, 6.0f, "#C8BA9C", 0.8f)),
                [Flare]             = ("Decoy flare",         V(EffectKind.Flare, 5f, 2.2f, "#FFE9A8", 1.0f)),           // ⭐ CE-1115 — its life is the expendable's
                [ChaffCloud]        = ("Chaff cloud",         V(EffectKind.Chaff, 8f, 15f, "#8A8A84C8", 3f)),
            };

        private static EffectVisualDto V(EffectKind kind, float duration, float size, string colour, float fade)
            => new() { Kind = kind, Duration = duration, Size = size, ColorHex = colour, FadeSeconds = fade };

        /// <summary>⭐ How an effect type looks: its TKB <see cref="EffectVisualDto"/>, else the built-in one of that id; null for
        /// a type that is no effect.</summary>
        public static EffectVisualDto? VisualOf(ITkbDatabase? tkb, long effectType)
        {
            if (tkb != null && tkb.TryGetByType(effectType, out var t) && t?.GetDescriptor<EffectVisualDto>() is { } v) return v;
            return BuiltIn.TryGetValue(effectType, out var b) ? b.Visual : null;
        }

        /// <summary>Registers the effect types and the built-in munitions' effect sets. ⚠ Once per database, after
        /// <see cref="MunitionTkbCatalog.RegisterAll"/>.</summary>
        public static void RegisterAll(ITkbDatabase tkb)
        {
            if (tkb == null) throw new ArgumentNullException(nameof(tkb));
            foreach (var (type, (name, visual)) in BuiltIn)
            {
                var t = new TkbTemplate(name, type);
                t.AddDescriptor(new TkbMasterDto { CustomName = name, HideFromPalette = true });
                t.AddDescriptor(visual);
                tkb.Register(t);
            }

            // The two built-in munitions (CE-1032): a hand grenade bursts medium with no flash; the mortar bomb fires with a
            // medium flash and bursts large.
            Set(tkb, MunitionTkbCatalog.TkbM67Grenade, new MunitionEffectsDto { Explosion = ExplosionMedium, Decal = DecalMedium });
            Set(tkb, MunitionTkbCatalog.Tkb81mmMortarHe, new MunitionEffectsDto { MuzzleFlash = MuzzleFlashMedium, Explosion = ExplosionLarge, Decal = DecalLarge });
            // ⭐ CE-1115 (§9 DV-1) — the expendables (backend's 6034 / 6035, BS-1 §5.4c): their release draws a moving decoy, no flash
            Set(tkb, MunitionTkbCatalog.TkbFlare, new MunitionEffectsDto { Release = Flare });
            Set(tkb, MunitionTkbCatalog.TkbChaff, new MunitionEffectsDto { Release = ChaffCloud });
        }

        private static void Set(ITkbDatabase tkb, long ammoType, MunitionEffectsDto set)
        {
            if (tkb.TryGetByType(ammoType, out var t) && t != null) t.AddDescriptor(set);
        }
    }
}
