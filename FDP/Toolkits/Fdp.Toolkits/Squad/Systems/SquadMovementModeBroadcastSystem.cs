using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Components;

namespace Fdp.Toolkit.Squad.Systems
{
    /// <summary>
    /// Reads bits 8-9 of <see cref="SquadCognitiveState.Flags"/> from the commander's
    /// blackboard and broadcasts the resulting <see cref="MovementMode"/> to all members
    /// by writing their <see cref="MovementModeIntent"/> component.
    /// </summary>
    public static unsafe class SquadMovementModeBroadcastSystem
    {
        private const uint MovementModeMask  = 0x0300u;
        private const int  MovementModeShift = 8;

        public static void Run(EntityRepository repo, Entity commander)
        {
            if (!repo.HasComponent<UnitRoster>(commander)) return;
            if (!repo.HasComponent<Blackboard1024>(commander)) return;

            ref readonly var state = ref SquadCognitiveState.Project(
                ref repo.GetComponentRW<Blackboard1024>(commander));
            var mode = (MovementMode)((state.Flags & MovementModeMask) >> MovementModeShift);

            ref readonly var roster = ref repo.GetComponentRO<UnitRoster>(commander);
            for (int m = 0; m < roster.Count; m++)
            {
                var member = new Entity((ulong)roster.SubordinateEntities[m]);
                if (!repo.HasComponent<MovementModeIntent>(member)) continue;
                repo.GetComponentRW<MovementModeIntent>(member).Mode = mode;
            }
        }
    }
}
