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
    }

    /// <summary>
    /// ⭐⭐ <b>An EDGE in what a unit senses</b> (docs/DESIGN_Sensors_And_Doctrine.md §7.3, <c>CE-3039</c>) — published on the
    /// unit's BRAIN node by the system that already holds the fact (one producer per fact): <c>ActiveSensorTracksUpdateSystem</c>
    /// (Acquired / Lost), <c>EqsResultUpdateSystem</c> (TopChanged), <c>ThreatEvaluationSystem</c> (FirstThreat / AllClear / Hit).
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
