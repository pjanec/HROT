using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// ⭐ CE-1120 (docs/designs/audio/DESIGN_FPS_Audio.md AU-5, §5) — the SOUND types, the twin of <see cref="EffectTkbCatalog"/>: each a TKB
    /// entry with its <see cref="SoundDto"/>, hidden from the palette; a munition's sounds follow its EFFECT sizes (the flash a shot draws
    /// picks its sound, the fireball a burst draws picks its bang), so what is seen and what is heard always agree.
    /// </summary>
    public static class SoundTkbCatalog
    {
        public const long ShotSmall = 7301, ShotMedium = 7302, ShotLarge = 7303;
        public const long ShotFarSmall = 7304, ShotFarMedium = 7305, ShotFarLarge = 7306;
        public const long Burst = 7307;
        public const long ExplosionSmall = 7311, ExplosionMedium = 7312, ExplosionLarge = 7313;
        public const long BulletPass = 7321;
        public const long ImpactSoft = 7331, ImpactHard = 7332;
        public const long Footsteps = 7341, WheeledEngine = 7342, TrackedEngine = 7343, Rotor = 7344, Jet = 7345;
        public const long Reload = 7351, EmptyClick = 7352, Breathing = 7353, Door = 7354;
        public const long Wind = 7361, Birds = 7362, Insects = 7363, Town = 7364, BattleRumble = 7365;

        /// <summary>The voice priorities (AU-7): higher keeps playing when the voices run out.</summary>
        public const byte PriorityOwn = 7, PriorityShotNear = 6, PriorityExplosion = 5, PriorityBulletPass = 4, PriorityShotFar = 3,
            PriorityImpact = 3, PriorityMoving = 2, PriorityAmbient = 1;

        /// <summary>The built-in sound types (§5): every one a generated placeholder until a file is named.</summary>
        public static readonly IReadOnlyDictionary<long, (string Name, SoundDto Sound)> BuiltIn =
            new Dictionary<long, (string, SoundDto)>
            {
                [ShotSmall]       = ("Sound: shot small",       S(SoundSynth.ShotSmall,       15f, SoundCategory.Weapons,    PriorityShotNear, 0f)),
                [ShotMedium]      = ("Sound: shot medium",      S(SoundSynth.ShotMedium,      25f, SoundCategory.Weapons,    PriorityShotNear, 2f)),
                [ShotLarge]       = ("Sound: shot large",       S(SoundSynth.ShotLarge,       40f, SoundCategory.Weapons,    PriorityShotNear, 4f)),
                [ShotFarSmall]    = ("Sound: shot far small",   S(SoundSynth.ShotFarSmall,   120f, SoundCategory.Weapons,    PriorityShotFar, -4f)),
                [ShotFarMedium]   = ("Sound: shot far medium",  S(SoundSynth.ShotFarMedium,  150f, SoundCategory.Weapons,    PriorityShotFar, -2f)),
                [ShotFarLarge]    = ("Sound: shot far large",   S(SoundSynth.ShotFarLarge,   200f, SoundCategory.Weapons,    PriorityShotFar, 0f)),
                [Burst]           = ("Sound: burst",            S(SoundSynth.Burst,           15f, SoundCategory.Weapons,    PriorityShotNear, 0f)),
                [ExplosionSmall]  = ("Sound: explosion small",  S(SoundSynth.ExplosionSmall,  25f, SoundCategory.Explosions, PriorityExplosion, 2f)),
                [ExplosionMedium] = ("Sound: explosion medium", S(SoundSynth.ExplosionMedium, 40f, SoundCategory.Explosions, PriorityExplosion, 4f)),
                [ExplosionLarge]  = ("Sound: explosion large",  S(SoundSynth.ExplosionLarge,  60f, SoundCategory.Explosions, PriorityExplosion, 6f)),
                [BulletPass]      = ("Sound: bullet pass",      S(SoundSynth.BulletPass,       3f, SoundCategory.Impacts,    PriorityBulletPass, -2f, range: 30f)),
                [ImpactSoft]      = ("Sound: impact soft",      S(SoundSynth.ImpactSoft,       5f, SoundCategory.Impacts,    PriorityImpact, -6f, range: 120f)),
                [ImpactHard]      = ("Sound: impact hard",      S(SoundSynth.ImpactHard,       5f, SoundCategory.Impacts,    PriorityImpact, -4f, range: 200f)),
                [Footsteps]       = ("Sound: footsteps",        S(SoundSynth.Footsteps,        4f, SoundCategory.Vehicles,   PriorityMoving, -18f, loop: true)),   // ⭐ CE-1141 — was -6 dB: "way less loud … too close to the rifle shot"
                [WheeledEngine]   = ("Sound: wheeled engine",   S(SoundSynth.WheeledEngine,   10f, SoundCategory.Vehicles,   PriorityMoving, -2f, loop: true)),
                [TrackedEngine]   = ("Sound: tracked engine",   S(SoundSynth.TrackedEngine,   15f, SoundCategory.Vehicles,   PriorityMoving, 0f, loop: true)),
                [Rotor]           = ("Sound: rotor",            S(SoundSynth.Rotor,           25f, SoundCategory.Vehicles,   PriorityMoving, 2f, loop: true)),
                [Jet]             = ("Sound: jet",              S(SoundSynth.Jet,             40f, SoundCategory.Vehicles,   PriorityMoving, 2f, loop: true)),
                [Reload]          = ("Sound: reload",           S(SoundSynth.Reload,           1f, SoundCategory.Own,        PriorityOwn, -4f)),
                [EmptyClick]      = ("Sound: empty click",      S(SoundSynth.EmptyClick,       1f, SoundCategory.Own,        PriorityOwn, -6f)),
                [Breathing]       = ("Sound: breathing",        S(SoundSynth.Breathing,        1f, SoundCategory.Own,        PriorityOwn, -12f, loop: true)),
                [Door]            = ("Sound: door",             S(SoundSynth.Door,             4f, SoundCategory.Impacts,    PriorityImpact, -4f, range: 60f)),
                [Wind]            = ("Sound: wind",             S(SoundSynth.Wind,             1f, SoundCategory.Ambient,    PriorityAmbient, -14f, loop: true)),
                [Birds]           = ("Sound: birds",            S(SoundSynth.Birds,            1f, SoundCategory.Ambient,    PriorityAmbient, -16f, loop: true)),
                [Insects]         = ("Sound: insects",          S(SoundSynth.Insects,          1f, SoundCategory.Ambient,    PriorityAmbient, -20f, loop: true)),
                [Town]            = ("Sound: town",             S(SoundSynth.Town,             1f, SoundCategory.Ambient,    PriorityAmbient, -18f, loop: true)),
                [BattleRumble]    = ("Sound: battle rumble",    S(SoundSynth.BattleRumble,     1f, SoundCategory.Ambient,    PriorityAmbient, -8f, loop: true)),
            };

        private static SoundDto S(SoundSynth synth, float reference, SoundCategory category, byte priority, float db, bool loop = false, float range = 0f)
            => new() { Synth = synth, ReferenceM = reference, Category = category, Priority = priority, VolumeDb = db, Loop = loop, RangeM = range };

        /// <summary>How a sound type sounds: its TKB <see cref="SoundDto"/>, else the built-in one of that id; null for a type that is no sound.</summary>
        public static SoundDto? SoundOf(ITkbDatabase? tkb, long soundType)
        {
            if (soundType == 0) return null;
            if (tkb != null && tkb.TryGetByType(soundType, out var t) && t?.GetDescriptor<SoundDto>() is { } s) return s;
            return BuiltIn.TryGetValue(soundType, out var b) ? b.Sound : null;
        }

        /// <summary>
        /// ⭐ AU-5 — the sounds of a SHOT: the munition's (or the type's) <see cref="SoundSetDto"/> first, else by the size of the flash the
        /// shot draws (<see cref="EffectTkbCatalog.EffectsFor"/>): small, medium or large; a burst for an automatic weapon. No flash (a bomb
        /// leaving its rack) ⇒ no shot sound.
        /// </summary>
        public static (long Near, long Far, long Burst) ShotFor(ITkbDatabase? tkb, long ammoType, float damagePerHit, long shooterType = 0)
        {
            var own = SetOf(tkb, ammoType) ?? SetOf(tkb, shooterType);
            var flash = EffectTkbCatalog.EffectsFor(tkb, ammoType, damagePerHit).MuzzleFlash;
            var (near, far) = flash switch
            {
                EffectTkbCatalog.MuzzleFlashSmall => (ShotSmall, ShotFarSmall),
                EffectTkbCatalog.MuzzleFlashMedium => (ShotMedium, ShotFarMedium),
                EffectTkbCatalog.MuzzleFlashLarge => (ShotLarge, ShotFarLarge),
                _ => (0L, 0L),
            };
            return (Pick(own?.Shot, near), Pick(own?.ShotFar, far), Pick(own?.Burst, near != 0 ? Burst : 0));
        }

        /// <summary>⭐ AU-5 — the bang of a DETONATION: the munition's set first, else by the size of the fireball it draws.</summary>
        public static long DetonationFor(ITkbDatabase? tkb, long ammoType, float damage)
        {
            var own = SetOf(tkb, ammoType);
            long bang = EffectTkbCatalog.EffectsFor(tkb, ammoType, damage).Explosion switch
            {
                EffectTkbCatalog.ExplosionSmall => ExplosionSmall,
                EffectTkbCatalog.ExplosionMedium => ExplosionMedium,
                EffectTkbCatalog.ExplosionLarge => ExplosionLarge,
                _ => 0L,
            };
            return Pick(own?.Detonation, bang);
        }

        /// <summary>⭐ AU-5 — the loop a MOVER makes: the type's set first, else by its sound class (a fixed-wing flyer ⇒ the jet).</summary>
        public static long MovingFor(ITkbDatabase? tkb, long type, SoundSourceClass cls, bool fixedWing)
        {
            var own = SetOf(tkb, type);
            long byClass = fixedWing ? Jet : cls switch
            {
                SoundSourceClass.Footsteps => Footsteps,
                SoundSourceClass.WheeledEngine => WheeledEngine,
                SoundSourceClass.TrackedEngine => TrackedEngine,
                SoundSourceClass.Rotor => Rotor,
                _ => 0L,
            };
            return Pick(own?.Moving, byClass);
        }

        private static long Pick(long? own, long fallback) => own is > 0 ? own.Value : fallback;

        private static SoundSetDto? SetOf(ITkbDatabase? tkb, long type)
            => type != 0 && tkb != null && tkb.TryGetByType(type, out var t) ? t?.GetDescriptor<SoundSetDto>() : null;

        /// <summary>Registers the sound types (hidden from the palette). ⚠ Once per database.</summary>
        public static void RegisterAll(ITkbDatabase tkb)
        {
            if (tkb == null) throw new ArgumentNullException(nameof(tkb));
            foreach (var (type, (name, sound)) in BuiltIn)
            {
                var t = new TkbTemplate(name, type);
                t.AddDescriptor(new TkbMasterDto { CustomName = name, HideFromPalette = true });
                t.AddDescriptor(sound);
                tkb.Register(t);
            }
        }
    }
}
