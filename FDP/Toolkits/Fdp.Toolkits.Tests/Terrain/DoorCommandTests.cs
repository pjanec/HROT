using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;
using Fdp.Toolkit.Replication.Components;
using Fbt;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ Buildings Stage 5d (📄 docs/DESIGN_Building_Interiors.md §3j "5d") — door commands: the one state machine, the owner-only
    /// applier, and the door actions an actor performs (adjacent, taking time, one command, answered by the door's state).
    /// </summary>
    public sealed class DoorCommandTests
    {
        private const TerrainDoorState O = TerrainDoorState.Open, C = TerrainDoorState.Closed, L = TerrainDoorState.Locked, D = TerrainDoorState.Destroyed;

        [Theory]
        // from, verb, result, to
        [InlineData(C, DoorVerb.Open, DoorVerbResult.Applied, O)]
        [InlineData(O, DoorVerb.Open, DoorVerbResult.AlreadyDone, O)]
        [InlineData(L, DoorVerb.Open, DoorVerbResult.Refused, L)]
        [InlineData(D, DoorVerb.Open, DoorVerbResult.AlreadyDone, D)]
        [InlineData(O, DoorVerb.Close, DoorVerbResult.Applied, C)]
        [InlineData(C, DoorVerb.Close, DoorVerbResult.AlreadyDone, C)]
        [InlineData(L, DoorVerb.Close, DoorVerbResult.AlreadyDone, L)]
        [InlineData(D, DoorVerb.Close, DoorVerbResult.Refused, D)]
        [InlineData(O, DoorVerb.Lock, DoorVerbResult.Applied, L)]
        [InlineData(C, DoorVerb.Lock, DoorVerbResult.Applied, L)]
        [InlineData(L, DoorVerb.Lock, DoorVerbResult.AlreadyDone, L)]
        [InlineData(D, DoorVerb.Lock, DoorVerbResult.Refused, D)]
        [InlineData(L, DoorVerb.Unlock, DoorVerbResult.Applied, C)]
        [InlineData(C, DoorVerb.Unlock, DoorVerbResult.AlreadyDone, C)]
        [InlineData(O, DoorVerb.Unlock, DoorVerbResult.AlreadyDone, O)]
        [InlineData(D, DoorVerb.Unlock, DoorVerbResult.Refused, D)]
        [InlineData(O, DoorVerb.Breach, DoorVerbResult.Applied, D)]
        [InlineData(C, DoorVerb.Breach, DoorVerbResult.Applied, D)]
        [InlineData(L, DoorVerb.Breach, DoorVerbResult.Applied, D)]
        [InlineData(D, DoorVerb.Breach, DoorVerbResult.AlreadyDone, D)]
        public void Stage5d_DoorRules_EveryVerbFromEveryState(TerrainDoorState from, DoorVerb verb, DoorVerbResult result, TerrainDoorState to)
        {
            Assert.Equal(result, DoorRules.Apply(from, verb, out var next));
            Assert.Equal(to, next);
        }

        private static EntityRepository World()
        {
            var w = new EntityRepository();
            w.RegisterComponent<DoorState>();
            w.RegisterComponent<SimTransform>();
            w.RegisterComponent<InteractionChannel>();
            w.RegisterComponent<NetworkAuthority>();
            w.RegisterEvent<DoorCommandEvent>();
            return w;
        }

        private static Entity Door(EntityRepository w, TerrainDoorState state, Vector3 at, int? owner = null)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new DoorState { State = state });
            w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            if (owner is int o) w.AddComponent(e, new NetworkAuthority(primaryOwnerId: o, localNodeId: 1));
            return e;
        }

        private static void Raise(EntityRepository w, Entity door, DoorVerb verb)
        {
            w.Bus.Publish(new DoorCommandEvent { Door = door, Verb = verb });
            w.Bus.SwapBuffers();
        }

        /// <summary>⭐ 5d — only the door's owner writes its state; a node that does not own it leaves the command to the owner.</summary>
        [Fact]
        public void Stage5d_TheOwnerAppliesADoorCommand_ANonOwnerDoesNot()
        {
            using var w = World();
            var mine   = Door(w, C, Vector3.Zero);                  // no authority component: one node owns everything
            var theirs = Door(w, C, Vector3.Zero, owner: 2);        // node 2's door
            var system = new DoorCommandSystem();

            w.Bus.Publish(new DoorCommandEvent { Door = mine, Verb = DoorVerb.Lock });
            w.Bus.Publish(new DoorCommandEvent { Door = theirs, Verb = DoorVerb.Lock });
            w.Bus.SwapBuffers();
            system.Execute(w, 0.1f);

            Assert.Equal(L, w.GetComponentRO<DoorState>(mine).State);
            Assert.Equal(C, w.GetComponentRO<DoorState>(theirs).State);
            Assert.Equal(1, system.Applied);

            Raise(w, mine, DoorVerb.Open);                         // refused: a locked door does not open
            system.Execute(w, 0.1f);
            Assert.Equal(L, w.GetComponentRO<DoorState>(mine).State);
        }

        private static unsafe Entity Actor(EntityRepository w, Vector3 at, Entity door)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            var ch = new InteractionChannel();
            ((OpenDoorParams*)ch.Params)->TargetDoor = door;
            w.AddComponent(e, ch);
            return e;
        }

        private static NodeStatus Tick(EntityRepository w, DoorActionExecutor ex, Entity actor, float dt, DoorCommandSystem owner, ref int commands)
        {
            ref var ch = ref w.GetComponentRW<InteractionChannel>(actor);
            ex.Execute(actor, ref ch, w, dt);
            w.Bus.SwapBuffers();
            commands += w.Bus.Read<DoorCommandEvent>().Length;
            owner.Execute(w, dt);
            return ch.Status;
        }

        /// <summary>
        /// ⭐⭐ 5d — an Open action: the actor stands at the door, spends the action time (Running, no command yet), raises ONE command,
        /// the owner applies it, and the action succeeds when the door's state says Open.
        /// </summary>
        [Fact]
        public void Stage5d_OpenDoor_TakesItsActionTime_SendsOneCommand_AndSucceedsWhenTheDoorIsOpen()
        {
            using var w = World();
            var door  = Door(w, C, new Vector3(10, 10, 0));
            var actor = Actor(w, new Vector3(10.5f, 9f, 0), door);
            var ex = new DoorActionExecutor(DoorVerb.Open);
            var owner = new DoorCommandSystem();
            ex.OnEnter(actor, ref w.GetComponentRW<InteractionChannel>(actor), w);

            int commands = 0;
            for (int i = 0; i < 9; i++)                              // 0.9 s < 1.0 s of action time
                Assert.Equal(NodeStatus.Running, Tick(w, ex, actor, 0.1f, owner, ref commands));
            Assert.Equal(0, commands);
            Assert.Equal(C, w.GetComponentRO<DoorState>(door).State);

            var status = NodeStatus.Running;
            for (int i = 0; i < 5 && status == NodeStatus.Running; i++) status = Tick(w, ex, actor, 0.1f, owner, ref commands);
            Assert.Equal(NodeStatus.Success, status);
            Assert.Equal(1, commands);                                // exactly one command
            Assert.Equal(O, w.GetComponentRO<DoorState>(door).State);
        }

        /// <summary>⭐ 5d — the action fails without a command when the actor is not at the door, or the door refuses the verb; a verb
        /// already done succeeds at once; and no answer from the owner fails it after the timeout.</summary>
        [Fact]
        public void Stage5d_ADoorAction_FailsOutOfReach_OrRefused_SucceedsWhenAlreadyDone_AndTimesOutWithoutTheOwner()
        {
            using var w = World();
            var owner = new DoorCommandSystem();
            NodeStatus Run(DoorVerb verb, Entity door, Vector3 at, DoorCommandSystem? applier, out int commands)
            {
                var actor = Actor(w, at, door);
                var ex = new DoorActionExecutor(verb);
                ex.OnEnter(actor, ref w.GetComponentRW<InteractionChannel>(actor), w);
                commands = 0;
                var status = NodeStatus.Running;
                for (int i = 0; i < 100 && status == NodeStatus.Running; i++)
                    status = Tick(w, ex, actor, 0.1f, applier ?? new DoorCommandSystem(), ref commands);
                return status;
            }

            var closed = Door(w, C, new Vector3(0, 0, 0));
            Assert.Equal(NodeStatus.Failure, Run(DoorVerb.Open, closed, new Vector3(5, 0, 0), owner, out int c1));     // 5 m away
            Assert.Equal(0, c1);
            Assert.Equal(NodeStatus.Failure, Run(DoorVerb.Open, closed, new Vector3(0, 0, 3.2f), owner, out _));     // the storey above

            var locked = Door(w, L, new Vector3(20, 0, 0));
            Assert.Equal(NodeStatus.Failure, Run(DoorVerb.Open, locked, new Vector3(20, 1, 0), owner, out int c2));
            Assert.Equal(0, c2);

            var open = Door(w, O, new Vector3(40, 0, 0));
            Assert.Equal(NodeStatus.Success, Run(DoorVerb.Open, open, new Vector3(40, 1, 0), owner, out int c3));
            Assert.Equal(0, c3);

            var remote = Door(w, C, new Vector3(60, 0, 0), owner: 2);                                                  // nobody here applies it
            Assert.Equal(NodeStatus.Failure, Run(DoorVerb.Open, remote, new Vector3(60, 1, 0), owner, out int c4));
            Assert.Equal(1, c4);                                                                                       // sent once, then waited
        }

        /// <summary>⭐ 5d — every door verb has an executor and an action id, in one list a host registers whole.</summary>
        [Fact]
        public void Stage5d_EveryDoorVerb_HasAnExecutorUnderItsOwnActionId()
        {
            var list = Fdp.Toolkit.Behavior.BehaviorConstants.DoorActionExecutors();
            Assert.Equal(new[] { DoorVerb.Open, DoorVerb.Close, DoorVerb.Lock, DoorVerb.Unlock, DoorVerb.Breach }, System.Linq.Enumerable.Select(list, x => x.Executor.Verb));
            Assert.Equal(new ushort[] { 4, 5, 6, 7, 8 }, System.Linq.Enumerable.Select(list, x => x.Id));
        }
    }
}
