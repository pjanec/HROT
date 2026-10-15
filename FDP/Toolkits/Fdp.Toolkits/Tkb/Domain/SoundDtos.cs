using System.Collections.Generic;
using Fdp.Toolkit.Tkb.Attributes;
using StructEdit.Core.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>⭐ CE-1120 (Hrot docs/designs/audio/DESIGN_FPS_Audio.md AU-9) — the operator's volume group a sound belongs to.</summary>
    public enum SoundCategory : byte
    {
        Weapons = 0,
        Explosions = 1,
        Vehicles = 2,
        Ambient = 3,
        /// <summary>The possessed unit's own sounds (his weapon, his steps, his breathing, his reload).</summary>
        Own = 4,
        Impacts = 5,
    }

    /// <summary>
    /// ⭐ CE-1120 (AU-8) — a sound generated in code when the sound type names no file: the placeholders every built-in sound starts
    /// with (no licence, no binary in the repository). A CC0 recording replaces one by naming it in <see cref="SoundDto.Files"/>.
    /// </summary>
    public enum SoundSynth : byte
    {
        None = 0,
        ShotSmall, ShotMedium, ShotLarge,
        ShotFarSmall, ShotFarMedium, ShotFarLarge,
        Burst,
        ExplosionSmall, ExplosionMedium, ExplosionLarge,
        BulletPass, ImpactSoft, ImpactHard,
        Footsteps, WheeledEngine, TrackedEngine, Rotor, Jet,
        Reload, EmptyClick, Breathing, Door,
        Wind, Birds, Insects, Town, BattleRumble,
    }

    /// <summary>
    /// ⭐ CE-1120 (Hrot docs/designs/audio/DESIGN_FPS_Audio.md AU-5, §5) — a SOUND TYPE's look, the twin of <see cref="EffectVisualDto"/>:
    /// what plays (a file, one of several picked at random, else a generated placeholder), how loud, how it falls off and how it loops.
    /// </summary>
    [TkbDescriptor("Sound.Clip")]
    public record SoundDto
    {
        /// <summary>Files under the audio asset folder (OGG / WAV); one is picked per play. Empty ⇒ <see cref="Synth"/>.</summary>
        public List<string> Files { get; init; } = new();

        /// <summary>The generated placeholder used while no file is named.</summary>
        public SoundSynth Synth { get; init; }

        /// <summary>Loudness relative to the others (dB; 0 = as authored).</summary>
        [EditUnit("dB")] public float VolumeDb { get; init; }

        /// <summary>Within this distance the sound is at full level (m); beyond it, it falls with distance.</summary>
        [EditUnit("m")] public float ReferenceM { get; init; } = 10f;

        /// <summary>Where it reaches silence (m); 0 ⇒ the source's acoustic range (the AI's hearing range, AU-4).</summary>
        [EditUnit("m")] public float RangeM { get; init; }

        /// <summary>A continuous sound (an engine, a bed) rather than a one-shot.</summary>
        public bool Loop { get; init; }

        /// <summary>A random pitch change per play (fraction), so repeats differ.</summary>
        public float PitchJitter { get; init; } = 0.05f;

        public SoundCategory Category { get; init; }

        /// <summary>Who wins when the voices run out (AU-7): higher keeps playing.</summary>
        public byte Priority { get; init; }
    }

    /// <summary>
    /// ⭐ CE-1120 (AU-5) — the sounds a munition / weapon / type makes, the twin of <see cref="MunitionEffectsDto"/>; a zero field takes
    /// the default of its class (the calibre class of the round, the sound class of the mover).
    /// </summary>
    [TkbDescriptor("Sound.Set")]
    public record SoundSetDto
    {
        public long Shot { get; init; }
        public long ShotFar { get; init; }
        public long Burst { get; init; }
        public long Detonation { get; init; }
        public long Moving { get; init; }
    }
}
