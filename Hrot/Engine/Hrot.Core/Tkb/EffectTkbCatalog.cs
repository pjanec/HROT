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
        public const long DecalSmall = 7121, DecalMedium = 7122, DecalLarge = 7123;
        public const long Tracer = 7131;

        /// <summary>The calibre classes the default effect set is chosen by, from a round's damage per hit.</summary>
        public enum CalibreClass : byte { Small, Medium, Large }

        /// <summary>Below this damage per hit a round is SMALL calibre (rifles: 25).</summary>
        public const float MediumDamageFrom = 50f;

        /// <summary>From this damage per hit a round is LARGE calibre (tank guns 1100–1200, ATGM 2000; the 25 mm cannon 60 and the
        /// RPG 400 are medium).</summary>
        public const float LargeDamageFrom = 500f;

        /// <summary>The calibre class of a round with <paramref name="damagePerHit"/> (0 = unknown ⇒ small).</summary>
        public static CalibreClass ClassOf(float damagePerHit)
            => damagePerHit >= LargeDamageFrom ? CalibreClass.Large : damagePerHit >= MediumDamageFrom ? CalibreClass.Medium : CalibreClass.Small;

        /// <summary>The default effect set of a calibre class.</summary>
        public static MunitionEffectsDto DefaultFor(CalibreClass c) => c switch
        {
            CalibreClass.Large => new MunitionEffectsDto { MuzzleFlash = MuzzleFlashLarge, Explosion = ExplosionLarge, Decal = DecalLarge, Tracer = Tracer },
            CalibreClass.Medium => new MunitionEffectsDto { MuzzleFlash = MuzzleFlashMedium, Explosion = ExplosionMedium, Decal = DecalMedium, Tracer = Tracer },
            _ => new MunitionEffectsDto { MuzzleFlash = MuzzleFlashSmall, Explosion = ExplosionSmall, Decal = DecalSmall },
        };

        /// <summary>
        /// ⭐ VE-D — the effects a round makes: its AMMO type's <see cref="MunitionEffectsDto"/> when it has one, else the default of
        /// its calibre class. The ONE answer the spawn system asks (E2).
        /// </summary>
        public static MunitionEffectsDto EffectsFor(ITkbDatabase? tkb, long ammoType, float damagePerHit)
        {
            if (tkb != null && ammoType != 0 && tkb.TryGetByType(ammoType, out var ammo) && ammo?.GetDescriptor<MunitionEffectsDto>() is { } set)
                return set;
            return DefaultFor(ClassOf(damagePerHit));
        }

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
                [DecalSmall]        = ("Decal small",         V(EffectKind.Decal, 60f, 0.35f, "#1E1A16C0", 10f)),       // a bullet mark
                [DecalMedium]       = ("Decal medium",        V(EffectKind.Decal, 180f, 1.8f, "#18140FD0", 30f)),
                [DecalLarge]        = ("Decal large",         V(EffectKind.Decal, 300f, 4.5f, "#120F0CE0", 60f)),       // a crater
                [Tracer]            = ("Tracer",              V(EffectKind.Tracer, 0.25f, 0.12f, "#FFB040", 0.1f)),
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
        }

        private static void Set(ITkbDatabase tkb, long ammoType, MunitionEffectsDto set)
        {
            if (tkb.TryGetByType(ammoType, out var t) && t != null) t.AddDescriptor(set);
        }
    }
}
