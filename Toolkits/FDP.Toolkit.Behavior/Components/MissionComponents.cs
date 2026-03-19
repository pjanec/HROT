using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Kernel;

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
        /// <summary>Advances when a <see cref="Events.DoctrineFinishedEvent"/> is received for this entity,
        /// indicating the doctrine's BTree root evaluated to Success or Failure.</summary>
        DoctrineFinished   = 4,
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
    /// <remarks>
    /// <para>
    /// <b>⚠ MUTATION WARNING — <c>[InlineArray]</c> C# 12 Defensive-Copy Trap</b>
    /// </para>
    /// <para>
    /// Because this type is decorated with <c>[InlineArray]</c>, the C# compiler may emit an
    /// <c>ldobj</c> IL instruction when the indexer (<c>[i]</c>) is evaluated on a <c>ref</c>
    /// variable. This copies the entire 96-byte buffer to a temporary on the evaluation stack
    /// before applying the index, meaning <b>any mutation via the indexer is silently
    /// discarded</b> — the ECS chunk is never written to.
    /// </para>
    /// <para>
    /// ❌ <b>Broken — mutation is lost:</b>
    /// <code>
    /// ref var q = ref repo.GetComponentRW&lt;MissionPlanQueue&gt;(entity);
    /// q.Phases[0] = somePhase;   // writes to a JIT temporary — silently ignored!
    /// </code>
    /// </para>
    /// <para>
    /// ✅ <b>Safe — Span cast (zero-allocation, guaranteed in-place write):</b>
    /// <code>
    /// ref var q = ref repo.GetComponentRW&lt;MissionPlanQueue&gt;(entity);
    /// Span&lt;MissionPhase&gt; phases = q.Phases;   // C# 12 InlineArray→Span holds a real pointer
    /// phases[0] = somePhase;
    /// </code>
    /// </para>
    /// <para>
    /// ✅ <b>Safe — Get ➔ Mutate ➔ SetComponent (clearest for low-frequency writes):</b>
    /// <code>
    /// var q = repo.GetComponent&lt;MissionPlanQueue&gt;(entity);   // read a local copy
    /// q.Phases[0] = somePhase;                                  // mutate the copy
    /// q.PhaseCount = 1;
    /// repo.SetComponent(entity, q);                             // write the copy back
    /// </code>
    /// </para>
    /// </remarks>
    [InlineArray(8)]
    public struct MissionPhaseBuffer
    {
        private MissionPhase _element;
    }

    /// <inheritdoc cref="MissionPhaseBuffer"/>
    /// <remarks>
    /// <para>
    /// <b>⚠ MUTATION WARNING — <c>[InlineArray]</c> Defensive-Copy Trap on <see cref="Phases"/></b>
    /// </para>
    /// <para>
    /// The <see cref="Phases"/> field is a <c>[InlineArray]</c> struct. Writing to its elements
    /// via an index expression on a <c>GetComponentRW</c> ref may silently fail because the
    /// C# compiler can emit an <c>ldobj</c> that copies the buffer to a temporary before
    /// applying <c>[i]</c>. The mutation hits the temporary, not ECS chunk memory.
    /// </para>
    /// <para>
    /// <b>Rule of thumb for this component:</b> prefer the Get ➔ Mutate ➔
    /// <c>SetComponent</c> pattern (mission phases change at most once per second — the
    /// extra 98-byte copy is irrelevant), or cast <c>Phases</c> to a
    /// <c>Span&lt;MissionPhase&gt;</c> before indexing. See <see cref="MissionPhaseBuffer"/>
    /// remarks for worked examples.
    /// </para>
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.MissionPlanQueue)]
    public struct MissionPlanQueue
    {
        /// <summary>Maximum number of phases in a mission plan.</summary>
        public const int MaxPhases = 8;

        /// <summary>Inline storage for up to <see cref="MaxPhases"/> phases.</summary>
        /// <remarks>
        /// <b>⚠ Do not write to this field's elements via a bare <c>GetComponentRW</c> ref.</b>
        /// The <c>[InlineArray]</c> indexer may produce a JIT defensive copy, silently discarding
        /// your mutation. Use the <c>Span&lt;MissionPhase&gt;</c> cast or the
        /// Get ➔ Mutate ➔ <c>SetComponent</c> pattern instead.
        /// See the type-level remarks on <see cref="MissionPhaseBuffer"/> for details.
        /// </remarks>
        public MissionPhaseBuffer Phases;

        /// <summary>Index of the currently active phase (0-based).</summary>
        public byte CurrentPhase;

        /// <summary>Total number of defined phases (&lt;= <see cref="MaxPhases"/>).</summary>
        public byte PhaseCount;

        /// <summary>Elapsed simulation time (seconds) for the current phase's <see cref="MissionTrigger.TimerElapsed"/> trigger.</summary>
        public float PhaseElapsedSeconds;
    }
}
