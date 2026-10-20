using Fdp.Toolkit.Tkb.Attributes;
using StructEdit.Core.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// ⭐ <c>CE-3061</c> / <c>CE-3062</c> — what an entity type gives OFF to other units' sensors: its heat and its noise
    /// (docs/DESIGN_Thermal_And_Acoustic_Sensing.md §4, R-205). Absent ⇒ the entity is cold and silent.
    /// </summary>
    [TkbDescriptor("Perception.Signatures")]
    public record SignaturesDto
    {
        /// <summary>Heat: a base signature plus heat built up by running and firing.</summary>
        public ThermalSignatureDto? Thermal { get; init; }

        /// <summary>Noise: how far moving, firing and detonations carry (🔒 user, 2026-10-05: "Hearability params to be added to to the tkb").</summary>
        public AcousticSignatureDto? Acoustic { get; init; }
    }

    /// <summary>
    /// ⭐ <c>CE-3061</c> — heat (🔒 user, 2026-10-05: "Hot when running or firing - pls implement some simple heat accumulation and cooldown").
    /// <c>Signature = BaseSignature + Heat × (1 − BaseSignature)</c>, Heat in 0..1.
    /// </summary>
    public record ThermalSignatureDto
    {
        /// <summary>The signature of the entity at rest and cold (0..1).</summary>
        [EditRange(0, 1)]
        public float BaseSignature { get; init; } = 0.3f;

        /// <summary>Heat gained per second while moving at <see cref="AcousticSignatureDto.ReferenceSpeed"/> or faster (scaled down when slower).</summary>
        [EditRange(0, 1)]
        public float RunningHeatPerSecond { get; init; } = 0.05f;

        /// <summary>Heat gained per shot fired.</summary>
        [EditRange(0, 1)]
        public float FiringHeatPerShot { get; init; } = 0.02f;

        /// <summary>Fraction of the heat lost per second (exponential cooldown).</summary>
        [EditRange(0, 1)]
        public float CooldownPerSecond { get; init; } = 0.02f;

        /// <summary>The speed counted as "running" for heat (m/s); 0 ⇒ 5 m/s.</summary>
        [EditUnit("m/s")]
        public float ReferenceSpeed { get; init; } = 5f;
    }

    /// <summary>⭐ <c>CE-3062</c> — how far this entity's sounds carry (metres). 0 ⇒ that sound is not made.</summary>
    public record AcousticSignatureDto
    {
        /// <summary>How far movement carries when moving at <see cref="ReferenceSpeed"/> (scaled with speed, below it).</summary>
        [EditUnit("m")]
        public float MovingAudibleRange { get; init; }

        /// <summary>The speed at which movement is loudest (m/s); 0 ⇒ 5 m/s.</summary>
        [EditUnit("m/s")]
        public float ReferenceSpeed { get; init; } = 5f;

        /// <summary>How far one of its shots carries.</summary>
        [EditUnit("m")]
        public float FiringAudibleRange { get; init; }

        /// <summary>How far a detonation of its munition carries.</summary>
        [EditUnit("m")]
        public float DetonationAudibleRange { get; init; }

        /// <summary>⭐ <c>CE-3063</c> (R-207) — what its movement SOUNDS like to a listener (footsteps, an engine…).</summary>
        public SoundSourceClass MovingClass { get; init; }

        /// <summary>⭐ <c>CE-3063</c> — what its shots sound like (small arms, a heavy weapon).
        /// ⚠ One per entity type, not per weapon mount: a tank's coax reads as its main gun.</summary>
        public SoundSourceClass FiringClass { get; init; }

        /// <summary>⭐ <c>CE-3063</c> — what a detonation of its munition sounds like; <see cref="SoundSourceClass.Unknown"/> ⇒ an explosion.</summary>
        public SoundSourceClass DetonationClass { get; init; }

        /// <summary>⭐ <c>CE-3283</c> (Q95 A) — the weapon FAMILY a trained ear tells its shots and bursts by (a ZU-23 is an AA gun, not a heavy MG):
        /// data, because the role is not in the weapon's category. A Regular hears the family; a Veteran the type itself.</summary>
        public SoundFamily Family { get; init; }

        /// <summary>⭐ <c>CE-3285</c> (Q95 B3) — the frequency band of its movement / shots / bursts: how much an obstacle stops it (World Query
        /// Seam WQ-E scales its loss by it). <see cref="SoundBand.Unset"/> ⇒ <see cref="SoundBands.Of"/> the class's. ⛔ Read by nothing yet.</summary>
        public SoundBand MovingBand { get; init; }

        /// <summary>⭐ <c>CE-3285</c> — the band of its shots (see <see cref="MovingBand"/>).</summary>
        public SoundBand FiringBand { get; init; }

        /// <summary>⭐ <c>CE-3285</c> — the band of its bursts (see <see cref="MovingBand"/>).</summary>
        public SoundBand DetonationBand { get; init; }
    }

    /// <summary>
    /// ⭐ <c>CE-3283</c> (Q95 A; Sensing §8.4) — the weapon family a Regular ear tells a shot or a burst by: finer than the class, coarser than the
    /// type. 3 bits on the acoustic answer (flags 13–15) — at most 8 values.
    /// </summary>
    /// ⚠ The TKB DTOs become DDS IDL, where enumerators share ONE scope: no member may repeat another enum's (<c>SoundSourceClass</c> has
    /// <c>Unknown</c> and <c>SmallArms</c> — hence <see cref="Untold"/> and <see cref="Rifle"/>).
    public enum SoundFamily : byte
    {
        /// <summary>No family (the ear could not tell, or the source has none).</summary>
        Untold    = 0,
        Mortar    = 1,
        /// <summary>A gun: a howitzer, a tank's or a field gun.</summary>
        Gun       = 2,
        HeavyMg   = 3,
        /// <summary>An anti-aircraft gun (the ZU-23).</summary>
        AaGun     = 4,
        /// <summary>A rocket or missile launcher (an RPG, an ATGM, a MANPADS).</summary>
        Launcher  = 5,
        /// <summary>Small arms: a rifle, a pistol, a light machine gun.</summary>
        Rifle     = 6,
    }

    /// <summary>
    /// ⭐ <c>CE-3285</c> (Q95 B3, approved) — a sound's band: bass bends round hills and passes walls, a faint high sound is stopped by them. One
    /// propagation model scales its obstacle loss by the band (WQ-E): <see cref="Low"/> ×0.2, <see cref="Mid"/> ×0.6, <see cref="High"/> ×1.0.
    /// </summary>
    public enum SoundBand : byte
    {
        Unset = 0,
        /// <summary>A blast, a gun's report.</summary>
        Low   = 1,
        /// <summary>A rifle, an engine.</summary>
        Mid   = 2,
        /// <summary>Footsteps, voices, a bolt worked.</summary>
        High  = 3,
    }

    /// <summary>⭐ <c>CE-3285</c> — the band a sound class has when its TKB states none (Q95 B3's table).</summary>
    public static class SoundBands
    {
        public static SoundBand Of(SoundSourceClass cls) => cls switch
        {
            SoundSourceClass.HeavyWeapon or SoundSourceClass.Explosion => SoundBand.Low,
            SoundSourceClass.Footsteps => SoundBand.High,
            SoundSourceClass.Unknown => SoundBand.Unset,
            _ => SoundBand.Mid,   // small arms, engines, rotors
        };
    }

    /// <summary>
    /// ⭐ <c>CE-3063</c> (R-207, 🔒 user 2026-10-05: <i>"the vehicle engine or human steps or shot sounds usually carry at least
    /// some kind-like identification, it is almost never a completely anonymous - and allows to estimate danger level"</i>) —
    /// the COARSE class a listener can tell a sound by, never an identity. Carried from the emitter to the Brain's memory; the
    /// memory merges only compatible classes and the AI judges danger from it at read time. <see cref="Unknown"/> is
    /// compatible with every class. docs/DESIGN_Thermal_And_Acoustic_Sensing.md §6.1a.
    /// </summary>
    public enum SoundSourceClass : byte
    {
        Unknown       = 0,
        Footsteps     = 1,
        WheeledEngine = 2,
        TrackedEngine = 3,
        SmallArms     = 4,
        HeavyWeapon   = 5,
        Explosion     = 6,
        /// <summary>⭐ M7 (DESIGN_FPV_Drone §2) — rotors: a drone or a helicopter, heard while it flies (hovering included).</summary>
        Rotor         = 7,
    }
}
