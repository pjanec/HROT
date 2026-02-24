using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FDP.Toolkit.Behavior.Components
{
    /// <summary>
    /// Condition that must be met for a <see cref="MissionPhase"/> to advance to the next phase.
    /// </summary>
    public enum MissionTrigger : byte
    {
        /// <summary>Advances when <see cref="MissionPlanQueue.PhaseElapsedSeconds"/> >= <see cref="MissionPhase.TriggerParam"/>.</summary>
        TimerElapsed       = 0,
        /// <summary>Advances when the entity's <c>NavState.HasArrived</c> == 1.</summary>
        ReachedDestination = 1,
        /// <summary>Advances when the entity's <c>TargetMemory</c> contains at least one entry with ThreatScore > 0.</summary>
        UnderAttack        = 2,
        /// <summary>Advances when <c>Health.Current / Health.Max</c> &lt;= <see cref="MissionPhase.TriggerParam"/>.</summary>
        HealthCritical     = 3,
    }

    /// <summary>
    /// One phase in a mission plan.
    /// Layout: int(4) + byte(1) + pad(3) + float(4) = 12 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MissionPhase
    {
        /// <summary>Doctrine hash to activate when this phase becomes current.</summary>
        public int DoctrineId;

        /// <summary>Condition that must be met to advance to the next phase.</summary>
        public MissionTrigger Trigger;

        // 3 bytes implicit padding (LayoutKind.Sequential aligns TriggerParam to offset 8)

        /// <summary>
        /// Trigger parameter whose meaning depends on <see cref="Trigger"/>:
        /// <list type="bullet">
        ///   <item><see cref="MissionTrigger.TimerElapsed"/>: duration in seconds.</item>
        ///   <item><see cref="MissionTrigger.ReachedDestination"/>: unused (0).</item>
        ///   <item><see cref="MissionTrigger.HealthCritical"/>: health fraction threshold [0..1].</item>
        ///   <item><see cref="MissionTrigger.UnderAttack"/>: unused (0).</item>
        /// </list>
        /// </summary>
        public float TriggerParam;
    }

    /// <summary>
    /// Fixed queue of up to 8 mission phases stored inline.
    /// <para>
    /// <c>sizeof(MissionPhase)</c> == 12 bytes (int:4 + MissionTrigger:1 + pad:3 + float:4).
    /// The inline buffer therefore occupies <c>8 × 12 = 96</c> bytes.
    /// </para>
    /// <para>
    /// Access phases via <c>queue.Phases[i]</c> (no <c>unsafe</c> required).
    /// <c>CurrentPhase</c> is the index of the active phase; <c>PhaseCount</c> is the total
    /// number of phases defined. The mission is complete when <c>CurrentPhase &gt;= PhaseCount</c>.
    /// </para>
    /// </summary>
    [InlineArray(8)]
    public struct MissionPhaseBuffer
    {
        private MissionPhase _element;
    }

    /// <inheritdoc cref="MissionPhaseBuffer"/>
    [StructLayout(LayoutKind.Sequential)]
    public struct MissionPlanQueue
    {
        /// <summary>Maximum number of phases in a mission plan.</summary>
        public const int MaxPhases = 8;

        /// <summary>Inline storage for up to <see cref="MaxPhases"/> phases.</summary>
        public MissionPhaseBuffer Phases;

        /// <summary>Index of the currently active phase (0-based).</summary>
        public byte CurrentPhase;

        /// <summary>Total number of defined phases (&lt;= <see cref="MaxPhases"/>).</summary>
        public byte PhaseCount;

        /// <summary>Elapsed simulation time (seconds) for the current phase's <see cref="MissionTrigger.TimerElapsed"/> trigger.</summary>
        public float PhaseElapsedSeconds;
    }
}
