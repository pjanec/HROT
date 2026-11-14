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

        /// <summary>⭐ <c>CE-3063</c> — the <c>SoundSourceClass</c> of its movement, its shots and its detonations (from the TKB).</summary>
        public byte MovingClass, FiringClass, DetonationClass;
        /// <summary>⭐ <c>CE-3283</c> (Q95 A) — the <c>SoundFamily</c> of its shots and bursts (TKB data), for a trained ear.</summary>
        public byte Family;

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

    /// <summary>
    /// ⭐ AQ104 E ③ (R-2079; Sensing §8.8) — the door state this node's <see cref="SoundEmissionSystem"/> last heard on a door entity: when
    /// the replicated <c>DoorState</c> differs, the door made a sound (once) and this follows it. Every node with a sound system keeps its
    /// own, so the sound is made where it is heard, from replicated state alone (never from the actor's door command, which lives on the
    /// actor's node). ⭐ A system's working memory is a component (R-3039): a checkpoint carries it, so a restore makes no false bang.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.DoorHeard)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct DoorHeard
    {
        public Fdp.Toolkit.Terrain.TerrainDoorState Last;
    }

    /// <summary>⭐ AQ104 E ③ — how far a door's sounds carry (m): a latch, a door swung, a door broken in.</summary>
    public static class DoorSounds
    {
        public const float LockRange = 5f, SwingRange = 15f, BreachRange = 40f;

        /// <summary>The carry of a door going from <paramref name="from"/> to <paramref name="to"/>; 0 = no sound.</summary>
        public static float Carry(Fdp.Toolkit.Terrain.TerrainDoorState from, Fdp.Toolkit.Terrain.TerrainDoorState to)
        {
            if (from == to) return 0f;
            if (to == Fdp.Toolkit.Terrain.TerrainDoorState.Destroyed) return BreachRange;
            bool wasOpen = from is Fdp.Toolkit.Terrain.TerrainDoorState.Open or Fdp.Toolkit.Terrain.TerrainDoorState.Destroyed;
            bool isOpen = to == Fdp.Toolkit.Terrain.TerrainDoorState.Open;
            return wasOpen != isOpen ? SwingRange : LockRange;   // opened / shut · locked / unlocked
        }

        /// <summary>Who stands near enough a door to have worked it (<c>DoorRules.ReachMetres</c>, one storey): his force and his sound —
        /// the listener's "not my own side" rule needs one. Nobody ⇒ the door itself, of no side.</summary>
        public const float ActorSearchMetres = Fdp.Toolkit.Terrain.DoorRules.ReachMetres + 1f;
    }

    /// <summary>The kind of sound heard (carried in the acoustic answer's flags and on <c>SoundContactEvent</c>).</summary>
    public enum SoundKind : byte
    {
        Movement   = 1,
        Shot       = 2,
        Detonation = 3,
    }
}
