using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Utility;

namespace Fdp.Toolkit.Squad.Systems
{
    /// <summary>
    /// Runs the Utility AI ManeuverSelect scorer on a squad commander at a decimated
    /// cadence (~10 Hz).  Writes the winning option id into
    /// <see cref="SquadCognitiveState.ManeuverKind"/>.
    /// </summary>
    /// <remarks>
    /// Mission-override guard: when <c>state.Flags</c> bit-0 (MissionOverrideBit) is set
    /// the scorer is skipped and <c>state.ManeuverKind</c> retains its forced value.
    /// Cadence: runs when <c>currentTick - state.Contacts.LastManeuverSelectTick >= tickInterval</c>
    /// OR on first call (<c>LastManeuverSelectTick == 0</c>).
    /// </remarks>
    public static unsafe class CommanderUtilityTickSystem
    {
        private const uint MissionOverrideBit = 1u;

        /// <param name="repo">Active ECS repository.</param>
        /// <param name="commander">Entity to evaluate (must carry Blackboard1024 and UtilityResultBuffer).</param>
        /// <param name="maneuverSelectDef">The ManeuverSelect UtilityDecisionDef to score.</param>
        /// <param name="currentTick">Current simulation tick.</param>
        /// <param name="tickInterval">Minimum ticks between re-scores (default 6 ~= 10 Hz at 60 tps).</param>
        public static void Run(
            EntityRepository repo,
            Entity commander,
            in UtilityDecisionDef maneuverSelectDef,
            uint currentTick,
            uint tickInterval = 6)
        {
            // Guards.
            if (!repo.HasComponent<Blackboard1024>(commander)) return;
            if (!repo.HasComponent<UtilityResultBuffer>(commander)) return;

            ref var state = ref SquadCognitiveState.Project(
                ref repo.GetComponentRW<Blackboard1024>(commander));

            // Mission-override: skip scoring, retain forced ManeuverKind.
            if ((state.Flags & MissionOverrideBit) != 0) return;

            // Cadence gate.
            bool firstRun = state.Contacts.LastManeuverSelectTick == 0;
            bool dwellElapsed = currentTick - state.Contacts.LastManeuverSelectTick >= tickInterval;
            if (!firstRun && !dwellElapsed) return;

            state.Contacts.LastManeuverSelectTick = currentTick;

            // Optional trace buffer.
            UtilityTraceWorkingMemory1024* tracePtr = null;
            if (repo.HasComponent<UtilityTraceWorkingMemory1024>(commander))
            {
                tracePtr = (UtilityTraceWorkingMemory1024*)Unsafe.AsPointer(
                    ref repo.GetComponentRW<UtilityTraceWorkingMemory1024>(commander));
            }

            ref var output = ref repo.GetComponentRW<UtilityResultBuffer>(commander);
            UtilityScorer.Evaluate(repo, commander, in maneuverSelectDef,
                Entity.Null, ref output, tracePtr, (ushort)currentTick);

            if (output.Count > 0)
                state.ManeuverKind = output.Top().WinningPostureId;
        }
    }
}
