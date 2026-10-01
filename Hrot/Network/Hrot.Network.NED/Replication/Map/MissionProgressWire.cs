using System.Collections.Generic;
using Hrot.NED.Descriptors;
using Fdp.Toolkit.Behavior.Components;

namespace Hrot.Map.Common.Replication
{
    /// <summary>
    /// ⭐ <b><c>CE-483</c> (egress half) — the ONE mapping between a <see cref="MissionPlanQueue"/>'s progress and the
    /// <see cref="eTaskState"/> of each task on the <c>EntityMission</c> wire.</b> 📄
    /// <c>docs/blueprints/DESIGN_Behaviour_Fault_And_Teardown.md</c> §4c W1–W3.
    ///
    /// <para>Used by BOTH <c>EntityMissionEgressTranslator</c> (encode) and <c>EntityMissionIngressTranslator</c> (decode), so
    /// the two directions cannot drift apart. No wire change: the existing states carry everything —
    /// <c>DONE</c>/<c>FAILED</c> are the recorded outcomes, <c>ACTIVE</c> is the current phase, and a halted plan is a
    /// <c>FAILED</c> current phase with no <c>ACTIVE</c> task.</para>
    /// </summary>
    public static class MissionProgressWire
    {
        /// <summary>W1: the wire state of phase <paramref name="index"/>.</summary>
        public static eTaskState StateOf(in MissionPlanQueue queue, int index)
        {
            switch (queue.Outcomes[index])
            {
                case MissionPhaseOutcome.Done:   return eTaskState.TASK_DONE;
                case MissionPhaseOutcome.Failed: return eTaskState.TASK_FAILED;
            }
            return index == queue.CurrentPhase && queue.Halted == 0 ? eTaskState.TASK_ACTIVE : eTaskState.TASK_PLANNED;
        }

        /// <summary>
        /// W3: restore <see cref="MissionPlanQueue.Outcomes"/>, <see cref="MissionPlanQueue.CurrentPhase"/> and
        /// <see cref="MissionPlanQueue.Halted"/> from the received task states (the first <c>PhaseCount</c> of them).
        /// <list type="bullet">
        ///   <item>an <c>ACTIVE</c> task is the current phase;</item>
        ///   <item>no <c>ACTIVE</c> task, and a <c>FAILED</c> task directly followed by a <c>PLANNED</c> one ⇒ halted on the
        ///     LAST such <c>FAILED</c> task (an ordinary failure advances, so its successor would be <c>ACTIVE</c>);</item>
        ///   <item>otherwise the plan is complete: <c>CurrentPhase = PhaseCount</c>. ⚠ A fault on the LAST phase decodes as
        ///     complete — equivalent for the adapter, which has nothing to run either way.</item>
        /// </list>
        /// </summary>
        public static void Decode(IReadOnlyList<eTaskState> states, ref MissionPlanQueue queue)
        {
            int count = queue.PhaseCount;
            int active = -1, haltedOn = -1;
            for (int i = 0; i < count && i < states.Count; i++)
            {
                switch (states[i])
                {
                    case eTaskState.TASK_DONE:   queue.Outcomes[i] = MissionPhaseOutcome.Done;   break;
                    case eTaskState.TASK_FAILED: queue.Outcomes[i] = MissionPhaseOutcome.Failed; break;
                    default:                     queue.Outcomes[i] = MissionPhaseOutcome.None;   break;
                }
                if (states[i] == eTaskState.TASK_ACTIVE && active < 0) active = i;
                if (states[i] == eTaskState.TASK_FAILED && i + 1 < count && i + 1 < states.Count
                    && states[i + 1] == eTaskState.TASK_PLANNED)
                    haltedOn = i;
            }

            if (active >= 0)        { queue.CurrentPhase = (byte)active;   queue.Halted = 0; }
            else if (haltedOn >= 0) { queue.CurrentPhase = (byte)haltedOn; queue.Halted = 1; }
            else                    { queue.CurrentPhase = (byte)count;    queue.Halted = 0; }
        }

        /// <summary>W2: the progress that decides whether the egress must re-publish — current phase, phase count, halt,
        /// and the eight outcomes packed into one value.</summary>
        public static (ulong Outcomes, int Head) ProgressOf(in MissionPlanQueue queue)
        {
            ulong outcomes = 0;
            for (int i = 0; i < MissionPlanQueue.MaxPhases; i++)
                outcomes |= (ulong)(byte)queue.Outcomes[i] << (8 * i);
            return (outcomes, queue.CurrentPhase | queue.PhaseCount << 8 | queue.Halted << 16);
        }
    }
}
