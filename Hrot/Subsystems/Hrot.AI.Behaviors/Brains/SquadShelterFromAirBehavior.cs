using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Events;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-7029</c> — the squad's air-cover reaction state.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SquadShelterFromAirState
    {
        /// <summary>1 once every live man has been sent to shelter.</summary>
        public byte Sent;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-7029</c> (City Battle §11 R-1d; 📄 Vehicle Exposure Cycle §8 — the vehicle's own <c>ShelterFromAir</c>) — a SQUAD under a drone: the
    /// reaction its standing orders start when a man senses one (<see cref="Fdp.Toolkit.Squad.Systems.SquadSensing.AirThreat"/> re-raises his
    /// <c>AirThreat</c> on the commander, as a hit becomes <c>SquadUnderFire</c>). Every live man is sent the library <c>ShelterFromAir</c> (overhead
    /// cover when there is some, else keep moving; fire at the drone while it is seen; he ends himself once his sky is clear); the reaction runs
    /// while the squad still senses the drone and ends <see cref="ClearSeconds"/> after the last sensing — then the task resumes and re-sends its
    /// orders. ⭐ A man under command never shelters on his own (R-2062): this is his unit's answer.
    /// <para>⭐ Parallel (R-4212): the commander changes only its own state; each man's behaviour is an ORDER (<see cref="CommandOrders.Assign"/>,
    /// a command), and the roster / the man's fight state are declared reads.</para>
    /// </summary>
    public static class SquadShelterFromAirBehavior
    {
        /// <summary>The reaction ends this long (s) after the squad last sensed the drone.</summary>
        public const float ClearSeconds = 10f;

        [StructLayout(LayoutKind.Sequential)]
        public struct Blackboard
        {
            public SquadShelterFromAirState State;
        }

        [BehaviorResolver(BehaviorNames.SquadShelterFromAir)]
        public static void Resolve(in NoParams authored, ref Blackboard block, EntityRepository world, Entity self)
            => block = default;

        [Behavior(BehaviorNames.SquadShelterFromAir, Params = typeof(NoParams), Categories = BehaviorCategory.Commander,
                  Description = "Every man of the squad takes overhead cover from a drone (a reaction).")]
        [BTreeDefinition(BehaviorNames.SquadShelterFromAir, Curated = true, Group = "Library/Reactions", ParamsType = typeof(Blackboard))]
        public static BTreeBuilder<Blackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<Blackboard, BTreeContext>()
                .Action(bb => bb.State, Run);

        /// <summary>One tick: send every live man to shelter (once), then hold until the sky has been clear for <see cref="ClearSeconds"/>.</summary>
        [SharedAiAction]
        public static NodeStatus Run(ref SquadShelterFromAirState s, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<UnitRoster>(self)) return NodeStatus.Failure;
            if (s.Sent == 0)
            {
                ref readonly var roster = ref world.GetComponentRO<UnitRoster>(self);
                for (int i = 0; i < roster.Count; i++)
                {
                    var man = roster.SubordinateEntities[i];
                    if (man.IsNull || !world.IsAlive(man) || SquadManeuverRunner.IsUnit(world, man)
                        || !Fdp.Toolkit.Squad.CombatEffective.IsInTheFight(world, man)) continue;
                    CommandOrders.Assign(world, self, man, BehaviorNames.ShelterFromAir, "{}");
                }
                s.Sent = 1;
            }
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;
            bool fresh = world.HasComponent<RecentSenses>(self)
                         && world.GetComponentRO<RecentSenses>(self).TryGetLast(SensorChange.AirThreat, out double at)
                         && now - at < ClearSeconds;
            return fresh ? NodeStatus.Running : NodeStatus.Success;
        }
    }
}
