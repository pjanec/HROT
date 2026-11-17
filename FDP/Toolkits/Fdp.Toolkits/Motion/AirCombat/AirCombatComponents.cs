using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Motion
{
    // ⭐⭐ CE-10000 (air lane, the contract; docs/designs/air-attack/DESIGN_Air_Combat.md §3, §4a, §7) — the air-combat vocabulary. Every type
    //   here is UNMANAGED (recorded as bytes, CLAUDE.md "NEW COMPONENTS"). The brain and the motion node exchange exactly two replicated
    //   components: the order DOWN (AirCombatSteer, brain group) and the state + live threat UP (FighterPilotState, muscle group).
    //   ⛔ Enum values are permanent (recorded, on the wire).

    /// <summary>⭐ AC-5 — the basic fighter manoeuvre the motion node flies (<c>FixedWingModel.Fight</c>): each is a desired 3-D vector + throttle.</summary>
    public enum Manoeuvre : byte
    {
        None = 0,
        /// <summary>The start: through the arena's centre at corner speed + 20 % (AC-10).</summary>
        Merge = 1,
        /// <summary>The nose ahead of the target (the gun / a short shot).</summary>
        LeadPursuit = 2,
        /// <summary>The nose on the target.</summary>
        PurePursuit = 3,
        /// <summary>The nose behind the target (closes slowly, keeps energy).</summary>
        LagPursuit = 4,
        /// <summary>Max-g turn into the threat.</summary>
        Break = 5,
        /// <summary>Unload and run (gain speed, open the range).</summary>
        Extend = 6,
        /// <summary>Trade speed for height.</summary>
        Climb = 7,
        /// <summary>Put the missile on the 3 or 9 o'clock (the radar notch, AC-6).</summary>
        Beam = 8,
        /// <summary>Turn away from the missile until it runs out of energy.</summary>
        Drag = 9,
        /// <summary>The reflex inside 2.5 s to impact: max g across the missile's path (AC-9; the motion node's own).</summary>
        LastDitch = 10,
        /// <summary>Back towards the arena (its soft edge, AC-10).</summary>
        ReturnToArena = 11,
        /// <summary>Out of the fight (winchester, damaged, outnumbered — AC-13).</summary>
        Disengage = 12,
        /// <summary>Back onto the leader's wing (<see cref="AirCombatSteer.Target"/> = the leader).</summary>
        Rejoin = 13,
    }

    /// <summary>⭐ AC-12 — the pilot's skill: reaction time, the g he will pull, the quality of his decisions, his lapse rate.</summary>
    public enum PilotSkill : byte
    {
        Rookie = 0,
        Veteran = 1,
        Ace = 2,
    }

    /// <summary>
    /// ⭐⭐ AC-2 — the brain's air-combat ORDER to its motion node: written ONLY by the brain (<c>AirCombatNodes</c>, after
    /// <c>NavigationIntent.Mode = AirCombat</c> and a bumped <c>IntentId</c> — the <c>OperatorControlBehavior.ManualControl</c> order),
    /// read every frame by <c>FixedWingModel.Fight</c>. Brain group, replicated (<c>dtAirCombatSteer</c>, 216). Unmanaged, recorded.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.AirCombatSteer)]
    // ⭐ R-10004 — SAVED with a scenario (not NoScenario): an authored steer is the motion node's feature-test seam (S1's GCAS scenarios
    //   fly a dive before the behaviour exists); the behaviour overwrites it on its first decision
    public struct AirCombatSteer
    {
        public Manoeuvre Manoeuvre;
        /// <summary>The lapse the pilot is in (the motion node flies a G-LOC wings level, a fixation's lowered floor) — AC-12.</summary>
        public PilotLapse Lapse;
        /// <summary>The bandit (or, for <see cref="Manoeuvre.Rejoin"/>, the leader). This node's entity; the wire carries its network id.</summary>
        public Entity Target;
        /// <summary>The arena entity (Null = none). This node's entity; the wire carries its network id.</summary>
        public Entity Arena;
        /// <summary>A point the manoeuvre flies at (the arena's centre, the way out); terrain-local metres.</summary>
        public Vector3 Point;
        /// <summary>0…1 (1 = full afterburner).</summary>
        public float Throttle;
        /// <summary>The floor GCAS keeps in this fight (m above the ground) — AC-4.</summary>
        public float FloorAgl;
        /// <summary>The g the pilot will pull (the skill's, ≤ the type's <c>MaxG</c>) — AC-12.</summary>
        public float MaxG;
    }

    /// <summary>
    /// ⭐⭐ AC-2 / AC-9a — the motion node's REPORT up: its g, throttle, ground-collision avoidance and the LIVE threat (every guided round
    /// whose target is this aircraft, scanned each frame). Written ONLY by the motion node (<c>FixedWingModel</c>); read by the brain
    /// (<c>AirCombatNodes</c>, <c>MissileDefence</c>) and the looks (afterburner from <see cref="Throttle"/>, vortex trails from
    /// <see cref="GLoad"/>). Muscle group, replicated (<c>dtFighterPilotState</c>, 217). Unmanaged, recorded.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.FighterPilotState)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct FighterPilotState
    {
        /// <summary>The load factor this frame (1 = level flight).</summary>
        public float GLoad;
        /// <summary>0…1 (1 = full afterburner).</summary>
        public float Throttle;
        /// <summary>1 while the ground-collision avoidance holds the stick.</summary>
        public byte GcasActive;
        /// <summary>How many times GCAS took the stick.</summary>
        public ushort GcasCount;
        /// <summary>The lowest height above the ground flown so far (m); <see cref="float.MaxValue"/> before the first frame.</summary>
        public float MinAgl;
        /// <summary>Guided rounds inbound now.</summary>
        public byte Inbound;
        /// <summary>The nearest inbound round's compass bearing from this aircraft (deg, 0 = north, clockwise).</summary>
        public float ThreatBearingDeg;
        /// <summary>The nearest inbound round's seeker.</summary>
        public GuidanceKind ThreatSeeker;
        /// <summary>Its closing speed (m/s, + closing).</summary>
        public float ThreatClosureMps;
        /// <summary>Its live time to impact (s); 0 when nothing is inbound.</summary>
        public float ThreatTtiS;
        /// <summary>When the last-ditch reflex last fired (Never = never).</summary>
        public SimInstant LastDitchAt;
        /// <summary>Rounds aimed at this aircraft that were DEFEATED (slowed below their minimum, AC-6).</summary>
        public byte Defeated;
        /// <summary>The current airspeed (m/s) — the brain's energy.</summary>
        public float SpeedMps;
        /// <summary>The recovery GCAS flies while <see cref="GcasActive"/>: 0 straight, +1 a climbing turn left, −1 right (motion-node local).</summary>
        public sbyte GcasTurn;
        /// <summary>The recovery's heading (deg, the math angle from +X east, counter-clockwise) chosen when GCAS took the stick (motion-node local).</summary>
        public float GcasHeadingDeg;
        /// <summary>How long GCAS has held the stick this time (s; motion-node local).</summary>
        public float GcasHeldS;
        /// <summary>1 while a route plane rejoins its path after a recovery (motion-node local).</summary>
        public byte Rejoin;
    }

    /// <summary>
    /// ⭐ AC-13 — the air-combat behaviour's working state (target, manoeuvre, skill, lapse, decisions). Brain, recorded, not replicated.
    /// ⭐ <see cref="Skill"/> and <see cref="ForcedLapse"/> are AUTHORED (a scenario sets a pilot's skill; a feature test forces a lapse).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.AirCombatState)]
    public struct AirCombatState
    {
        public Entity Target;
        public Manoeuvre Manoeuvre;
        public PilotSkill Skill;
        public PilotLapse Lapse;
        /// <summary>A lapse a scenario forces from the start (a feature test), None = drawn only past the stalemate.</summary>
        public PilotLapse ForcedLapse;
        public SimInstant LapseUntil;
        public SimInstant NextDecision;
        public byte Kills;
        public byte Shots;
        /// <summary>When this pilot's last missile left (Never = none) — one missile at a time per target (AC-8).</summary>
        public SimInstant LastShotAt;
        /// <summary>Seconds above 8 g so far (the G-LOC lapse).</summary>
        public float HighGSeconds;
    }

    /// <summary>
    /// ⭐ AC-9 — the ONE missile defence's state, shared by every behaviour that flies (<c>AirCombat</c>, <c>AirAttack</c>, a transit move).
    /// Written ONLY by <c>MissileDefence.Run</c> (the owner's brain). Brain, recorded, not replicated.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.MissileDefenceState)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct MissileDefenceState
    {
        public MissileDefencePhase Phase;
        public GuidanceKind Seeker;
        /// <summary>Decoys released in this defence.</summary>
        public byte Decoys;
        /// <summary>The stage the owning behaviour returns to when the defence ends.</summary>
        public byte Resume;
        public SimInstant StartedAt;
        /// <summary>The launch warning this defence answered (its <c>MissileWarning.At</c>).</summary>
        public SimInstant WarnedAt;
        public SimInstant LastDecoyAt;
        /// <summary>How many defences ran on this aircraft (a scenario reads it).</summary>
        public ushort Count;
    }

    /// <summary>
    /// ⭐⭐ AC-10 / AC-11 — the show's stage: an AUTHORED entity (centre, radius, floor, ceiling) the fighters fight inside, with the show
    /// rules (no missile beyond <see cref="MaxShotRangeM"/>) and the stalemate clock (<see cref="LastHitAt"/>, written ONLY by
    /// <c>AirArenaSystem</c>). Brain group, replicated (<c>dtAirArena</c>, 218). Unmanaged, recorded, saved with the scenario.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.AirArena)]
    public struct AirArena
    {
        /// <summary>The centre (terrain-local metres; Z unused — the floor and ceiling are above the ground there).</summary>
        public Vector3 Centre;
        public float RadiusM;
        /// <summary>The floor, m above the ground (GCAS keeps it in the fight).</summary>
        public float FloorAglM;
        /// <summary>The ceiling, m above the ground under the centre.</summary>
        public float CeilingAglM;
        /// <summary>No missile beyond this (m), even inside the employment zone; 0 ⇒ <see cref="DefaultMaxShotRangeM"/>.</summary>
        public float MaxShotRangeM;
        /// <summary>No hit for this long (s) ⇒ the pilots start to lapse; 0 ⇒ <see cref="DefaultStalemateSeconds"/>.</summary>
        public float StalemateSeconds;
        /// <summary>1 ⇒ no lapses at all (a feature test where skill alone decides).</summary>
        public byte LapsesOff;
        /// <summary>The last hit on an aircraft in the arena (Never = none yet).</summary>
        public SimInstant LastHitAt;

        public const float DefaultMaxShotRangeM = 6000f, DefaultStalemateSeconds = 90f, DefaultRadiusM = 4000f,
                           DefaultFloorAglM = 300f, DefaultCeilingAglM = 6000f;
    }
}
