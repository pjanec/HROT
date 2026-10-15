using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Perception.Events
{
    // ⚠ Its own file: the DDS code generator emits one IDL scope per source file, and IDL enum members share it —
    //   SensorChange.Acquired collided with SensorTrackStatus.Acquired in PerceptionEvents.cs.

    // ── SensorChangedEvent (CE-3039) ─────────────────────────────────────────────

    /// <summary>What changed for a unit's senses — see <see cref="SensorChangedEvent"/>.</summary>
    public enum SensorChange : byte
    {
        /// <summary>A contact entered the unit's tracks (no sensor of the unit held it before).</summary>
        Acquired    = 1,
        /// <summary>A contact left the unit's tracks (no sensor of the unit holds it any more).</summary>
        Lost        = 2,
        /// <summary>A perception sensor's best result changed (a different entity or position on top).</summary>
        TopChanged  = 3,
        /// <summary>The unit's memory went from empty to holding a contact.</summary>
        FirstThreat = 4,
        /// <summary>The unit's memory went from holding contacts to empty.</summary>
        AllClear    = 5,
        /// <summary>The unit's health dropped — it was hit (by whatever, seen or not).</summary>
        Hit         = 6,
        /// <summary>⭐ <c>CE-3064</c> — a bullet passed close to the unit without hitting it: it is being SHOT AT (R-206).</summary>
        NearMiss    = 7,

        /// <summary>⭐ <c>CE-3072</c> B0 — a danger-area sensor's NEXT AREA AHEAD changed (a new one, or none).</summary>
        AreaAhead      = 8,

        /// <summary>⭐ <c>CE-3072</c> B0 — the threat on the next area ahead rose to/above the producer's threshold.</summary>
        AreaThreatened = 9,

        /// <summary>⭐ <c>CE-3072</c> B0 — the threat on the next area ahead fell below it again.</summary>
        AreaCleared    = 10,

        /// <summary>⭐ <c>CE-2123</c> S2 — the SQUAD's shared contacts went from empty to holding one. Published on the COMMANDER by
        /// <c>SquadCoordinationSystem</c> after its merge (a soldier-leader's own <see cref="FirstThreat"/> stays its own).</summary>
        SquadContact   = 11,

        /// <summary>⭐ <c>CE-2123</c> S2 — a squad member (or the commander itself) was hit or near-missed: the SQUAD is under fire.
        /// Published on the COMMANDER.</summary>
        SquadUnderFire = 12,

        /// <summary>⭐ <c>CE-2123</c> S2 — the squad's shared contacts went from holding contacts to empty. Published on the COMMANDER.</summary>
        SquadAllClear  = 13,

        /// <summary>⭐ <c>CE-2125</c> (§7.2 L3) — the squad's leader soldier fell; the next man took over. Published on the COMMANDER.</summary>
        SquadLeaderDown = 14,

        /// <summary>⭐ <c>CE-2152</c> C-c (R-2012 K1, R-a) — a squad member is DOWN (incapacitated, alive, bleeding): once per man.
        /// Published on the COMMANDER; <see cref="SensorChangedEvent.Target"/> is the man.</summary>
        SquadMemberDown = 15,

        /// <summary>⭐ <c>CE-2165</c> V8 (DESIGN_Vehicle_Exposure_Cycle §8) — an AIR contact entered the unit's memory: an identified
        /// flyer (its <c>Locomotion</c> is in the Air domain) or a heard <c>Rotor</c> sound. Published by <c>ThreatEvaluationSystem</c>
        /// once per new air slot (a second drone raises it again; the same one staying in memory does not).</summary>
        AirThreat = 16,

        /// <summary>⭐ <c>CE-2189</c> (DESIGN_Missiles_And_Ammo §3c, AD4) — a guided missile was launched at this AIRCRAFT. Published by
        /// <c>MissileWarningSensingSystem</c> on the aircraft's brain; the details (bearing, seeker, time to impact) are in its
        /// <c>MissileWarning</c> component.</summary>
        MissileWarning = 17,
    }

    /// <summary>
    /// ⭐⭐ <b>An EDGE in what a unit senses</b> (docs/DESIGN_Sensors_And_Doctrine.md §7.3, <c>CE-3039</c>) — published on the
    /// unit's BRAIN node by the system that already holds the fact (one producer per fact): <c>ActiveSensorTracksUpdateSystem</c>
    /// (Acquired / Lost), <c>EqsResultUpdateSystem</c> (TopChanged), <c>ThreatEvaluationSystem</c> (FirstThreat / AllClear / Hit),
    /// <c>SquadCoordinationSystem</c> (SquadContact / SquadUnderFire / SquadAllClear — on the commander, CE-2123 S2).
    /// <para>⭐ Consumers react to it the way each tier already reacts to anything: a blueprint with <c>When EventFired</c>,
    /// an HSM through the runner's event bridge (<c>CE-3040</c>, behaviors lane), a doctrine by waking on it (R-195). A BTree
    /// polls instead (§7.3). Bus events live one frame.</para>
    /// </summary>
    [EventId(PerceptionConstants.SensorChangedEventId)]
    [StructLayout(LayoutKind.Sequential)]
    public struct SensorChangedEvent
    {
        /// <summary>The unit whose senses changed.</summary>
        public Entity Unit;

        /// <summary>The sensor that changed (TopChanged); <see cref="Entity.Null"/> for a unit-level change.</summary>
        public Entity Sensor;

        /// <summary>The contact concerned (Acquired / Lost / TopChanged); <see cref="Entity.Null"/> otherwise or when positional.</summary>
        public Entity Target;

        /// <summary>The sensing kind (TopChanged: the sensor's kind); 0 = not tied to one kind (a unit-level change).</summary>
        public Components.SensorModality Kind;

        /// <summary>What changed.</summary>
        public SensorChange What;
    }
}
