using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Executors;
using Fdp.Toolkit.Combat.Components;
using Fbt;

namespace Fdp.Toolkit.Civilians
{
    /// <summary>
    /// ⭐ <c>CE-2238</c> part 2 (CIV-4, AW-7; docs/DESIGN_Civilians_And_Crowd_Control.md §5i.2) — a rioter's stone: what is in his hands
    /// (<see cref="PersonHands"/>, backend B-8) and the one throw — a <see cref="CombatConstants.ActionIdFireAtPoint"/> of ONE round on his
    /// <see cref="WeaponChannel"/> from the hands mount, which <see cref="FireAtPointExecutor"/> throws on its arc (H-5). No second firing path.
    /// </summary>
    public static class Stones
    {
        /// <summary>The executor's own cooldown for the one round (the behaviour paces the throws itself).</summary>
        public const float ExecutorCooldownSeconds = 0.5f;

        /// <summary>True when <paramref name="man"/> has something to throw in his hands that is not lethal (a stone, not a live grenade —
        /// AW-9 is the armed path).</summary>
        public static bool InHand(EntityRepository world, Entity man)
            => PersonHands.Holding(world, man, _ => false) && !PersonHands.HoldsLethal(world, man);

        /// <summary>Throws ONE round from <paramref name="man"/>'s hands at <paramref name="at"/>: a new FireAtPoint action instance on his
        /// weapon channel (so the executor re-enters). False when he has no hands mount, no weapon channel or nothing to throw.</summary>
        public static unsafe bool Throw(EntityRepository world, Entity man, Vector3 at)
        {
            int mount = PersonHands.MountIndex(world, man);
            if (mount < 0 || mount > byte.MaxValue || !world.IsComponentTypeRegistered<WeaponChannel>() || !world.HasComponent<WeaponChannel>(man)
                || !InHand(world, man)) return false;
            ref var channel = ref world.GetComponentRW<WeaponChannel>(man);
            if (world.IsComponentTypeRegistered<BehaviorState>() && world.HasComponent<BehaviorState>(man))
                channel.BehaviorInstanceId = world.GetComponentRO<BehaviorState>(man).InstanceId;
            unchecked { channel.ActionInstanceId++; if (channel.ActionInstanceId == 0) channel.ActionInstanceId = 1; }
            channel.ActiveAction = CombatConstants.ActionIdFireAtPoint;
            channel.Status = NodeStatus.Running;
            System.Runtime.CompilerServices.Unsafe.As<byte, FireAtPointParams>(ref channel.Params[0]) =
                new FireAtPointParams { Point = at, CooldownSeconds = ExecutorCooldownSeconds, Rounds = 1, Mount = (byte)mount };
            return true;
        }
    }
}
