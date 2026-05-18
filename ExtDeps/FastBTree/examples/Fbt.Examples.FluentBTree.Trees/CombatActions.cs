using System;
using Fbt;
using Fbt.Compiler;

namespace Fbt.Examples.FluentBTree
{
    public static class CombatActions
    {
        // 3-param: ReusableConditionDelegate<int, CombatContext>
        // Returns Success if ammo > 0, Failure otherwise.
        [BTreeCondition]
        public static NodeStatus CheckAmmo(ref int ammo, ref BehaviorTreeState state, ref CombatContext ctx)
        {
            return ammo > 0 ? NodeStatus.Success : NodeStatus.Failure;
        }

        // 3-param: ReusableConditionDelegate<bool, CombatContext>
        // Returns Success if threat is visible, Failure otherwise.
        [BTreeCondition]
        public static NodeStatus HasThreat(ref bool threatVisible, ref BehaviorTreeState state, ref CombatContext ctx)
        {
            return threatVisible ? NodeStatus.Success : NodeStatus.Failure;
        }

        // 3-param: ReusableActionDelegate<int, CombatContext>
        // Decrements ammo by 1 and returns Success.
        [BTreeAction]
        public static NodeStatus AimAndFire(ref int ammo, ref BehaviorTreeState state, ref CombatContext ctx)
        {
            ammo--;
            Console.WriteLine($"[AimAndFire] Shot fired. Ammo remaining: {ammo}");
            return NodeStatus.Success;
        }

        // 4-param: NodeLogicDelegate<CombatBlackboard, CombatContext>
        // Returns Running for the first tick, Success on the second tick.
        // Uses state.AsyncData as a per-node tick counter.
        [BTreeAction]
        public static NodeStatus HoldPosition(
            ref CombatBlackboard bb,
            ref BehaviorTreeState state,
            ref CombatContext ctx,
            int param)
        {
            ulong tick = state.AsyncData + 1;
            state.AsyncData = tick;
            if (tick < 2)
            {
                Console.WriteLine("[HoldPosition] Holding...");
                return NodeStatus.Running;
            }
            state.AsyncData = 0;
            Console.WriteLine("[HoldPosition] Done holding.");
            return NodeStatus.Success;
        }
    }
}
