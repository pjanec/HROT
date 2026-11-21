using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Terrain;
using Fbt;

namespace Fdp.Toolkit.Behavior.Executors
{
    /// <summary>
    /// Parameters packed into InteractionChannel.Params for every door action (OpenDoor, CloseDoor, LockDoor, UnlockDoor,
    /// BreachDoor): the door entity. ⚠ The name stays <c>OpenDoorParams</c> — the blueprint channel-command catalog bakes it.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct OpenDoorParams
    {
        /// <summary>The door entity to interact with.</summary>
        public Entity TargetDoor;
    }

    /// <summary>
    /// ⭐ Buildings Stage 5d (📄 docs/DESIGN_Building_Interiors.md §3a "Doors", §3j "5d") — the door actions. One executor per verb,
    /// registered under its action id on the interaction channel. Replaces the Slice 1 stub that succeeded at once.
    /// <list type="number">
    ///   <item><description>the door must exist; a verb that is already done succeeds at once, one the door refuses fails
    ///   (<see cref="DoorRules"/>);</description></item>
    ///   <item><description>the actor must be ADJACENT (<see cref="DoorRules.ReachMetres"/>) — the behaviour moves it there first; out
    ///   of reach fails;</description></item>
    ///   <item><description>it spends the verb's action time (<see cref="DoorRules.ActionSeconds"/>), then raises ONE
    ///   <see cref="DoorCommandEvent"/> — the door's owner applies it, wherever it runs;</description></item>
    ///   <item><description>it succeeds when the door's state (replicated from the owner) is the one the verb leads to, and fails if
    ///   that has not happened within <see cref="ResultTimeoutSeconds"/>.</description></item>
    /// </list>
    /// ⚠ Like every executor, its work happens in <c>Execute</c> (the CE-408 contract), never in <c>OnEnter</c>.
    /// </summary>
    public sealed class DoorActionExecutor : IActionExecutor<InteractionChannel>
    {
        /// <summary>How long to wait for the owner's answer after the command is sent.</summary>
        public const float ResultTimeoutSeconds = 3f;

        private readonly DoorVerb _verb;

        public DoorActionExecutor(DoorVerb verb) => _verb = verb;

        public DoorVerb Verb => _verb;

        [StructLayout(LayoutKind.Sequential)]
        private struct DoorActionState
        {
            public float Elapsed;
            public float SinceSent;
            public byte Sent;
            public TerrainDoorState Expected;
        }

        public unsafe void OnEnter(Entity entity, ref InteractionChannel channel, EntityRepository world)
        {
            fixed (byte* p = channel.State) *(DoorActionState*)p = default;
            channel.Status = NodeStatus.Running;
        }

        public unsafe void Execute(Entity entity, ref InteractionChannel channel, EntityRepository world, float dt)
        {
            Entity door;
            fixed (byte* p = channel.Params) door = ((OpenDoorParams*)p)->TargetDoor;
            if (!world.IsAlive(door) || !world.HasComponent<DoorState>(door)) { channel.Status = NodeStatus.Failure; return; }
            var current = world.GetComponentRO<DoorState>(door).State;

            DoorActionState s;
            fixed (byte* p = channel.State) s = *(DoorActionState*)p;

            if (s.Sent != 0)
            {
                // ④ the owner's answer is the door's replicated state
                if (current == s.Expected) channel.Status = NodeStatus.Success;
                else if ((s.SinceSent += dt) > ResultTimeoutSeconds) channel.Status = NodeStatus.Failure;
            }
            else
            {
                // ①
                var result = DoorRules.Apply(current, _verb, out var target);
                if (result == DoorVerbResult.AlreadyDone) { channel.Status = NodeStatus.Success; return; }
                if (result == DoorVerbResult.Refused)     { channel.Status = NodeStatus.Failure; return; }
                // ②
                if (!InReach(world, entity, door)) { channel.Status = NodeStatus.Failure; return; }
                // ③
                if ((s.Elapsed += dt) >= DoorRules.ActionSeconds(_verb))
                {
                    world.Bus.Publish(new DoorCommandEvent { Door = door, Verb = _verb, Actor = entity });
                    s.Sent = 1;
                    s.Expected = target;
                }
            }

            fixed (byte* p = channel.State) *(DoorActionState*)p = s;
        }

        public void OnExit(Entity entity, ref InteractionChannel channel, EntityRepository world) { }

        /// <summary>True when <paramref name="actor"/> stands within reach of <paramref name="door"/>'s doorway (both need a
        /// <see cref="SimTransform"/>; one without is never in reach).</summary>
        public static bool InReach(EntityRepository world, Entity actor, Entity door)
        {
            if (!world.HasComponent<SimTransform>(actor) || !world.HasComponent<SimTransform>(door)) return false;
            var a = world.GetComponentRO<SimTransform>(actor).Position;
            var d = world.GetComponentRO<SimTransform>(door).Position;
            return Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(d.X, d.Y)) <= DoorRules.ReachMetres
                   && System.MathF.Abs(a.Z - d.Z) <= DoorRules.ReachHeightMetres;
        }
    }
}
