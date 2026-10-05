using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Perception.Signatures
{
    /// <summary>
    /// ⭐ <c>CE-3062</c> — the sounds an entity is making right now (docs/DESIGN_Thermal_And_Acoustic_Sensing.md §6 C, R-205).
    /// Stamped from the TKB <c>AcousticSignatureDto</c>; the "right now" fields are written by <see cref="SoundEmissionSystem"/>
    /// on the main loop and read by the acoustic sensor on its background snapshot. ⭐ Per-entity state, not a shared buffer: the
    /// solver reads a snapshot, so a shared list written by the main loop would be a data race.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.AcousticEmitter)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct AcousticEmitter
    {
        // From the TKB.
        public float MovingAudibleRange;
        public float ReferenceSpeed;
        public float FiringAudibleRange;
        public float DetonationAudibleRange;

        /// <summary>How far its movement carries this tick (0 = still).</summary>
        public float CurrentMovingRange;

        /// <summary>Seconds left in which its last shot can still be heard; where it was fired from.</summary>
        public float ShotTimeLeft;
        public float ShotX, ShotY, ShotZ;

        /// <summary>Seconds left in which its last munition's detonation can still be heard; where it burst.</summary>
        public float DetonationTimeLeft;
        public float DetonationX, DetonationY, DetonationZ;

        // The position last tick — speed is measured as distance moved.
        public float LastX, LastY, LastZ;
        [MarshalAs(UnmanagedType.I1)] public bool HasLast;
    }

    /// <summary>The kind of sound heard (carried in the acoustic answer's flags and on <c>SoundContactEvent</c>).</summary>
    public enum SoundKind : byte
    {
        Movement   = 1,
        Shot       = 2,
        Detonation = 3,
    }
}
