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

        // ── 5d-2 — a behaviour names a door by key (TerrainObjectRef) and acts on it (DoorNodes) ──────────────────────────────

        // A closed 10 x 8 m room at (20,20); its one door ("front", 1.2 m) is in the south wall, centre (25, 20).
        private const string OneDoorRoom = """
            {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,60,60],"groundZ":0},"features":[
              {"type":"Feature","properties":{"kind":"building","label":"R","doors":{"front":"locked"},"building":{
                 "footprint":[[0,0],[10,0],[10,8],[0,8]],
                 "storeys":[{"height":3,"walls":[
                      {"from":[0,0],"to":[10,0],"openings":[{"kind":"door","at":4.4,"width":1.2,"doorId":"front"}]},
                      {"from":[10,0],"to":[10,8]},{"from":[10,8],"to":[0,8]},{"from":[0,8],"to":[0,0]}]}]}},
               "geometry":{"type":"Point","coordinates":[20,20]}}]}
            """;

        private static EntityRepository KeyedWorld(out Entity door)
        {
            var w = World();
            w.RegisterManagedComponent<TerrainObjectKey>();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.LocomotionChannel>();
            w.SetSingletonManaged(TerrainWorldParser.Parse(OneDoorRoom, "range"));
            door = Door(w, L, new Vector3(25, 20, 0));
            w.SetManagedComponent(door, new TerrainObjectKey { Key = "range/R/front" });
            return w;
        }

        private static Entity Walker(EntityRepository w, Vector3 at)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            w.AddComponent(e, new InteractionChannel());
            w.AddComponent(e, new Fdp.Toolkit.Behavior.Components.LocomotionChannel());
            return e;
        }

        /// <summary>⭐ 5d-2 (K4) — the reference IS the key: JSON is the bare key string, it resolves through the door entity, and a
        /// key that does not fit is refused rather than cut.</summary>
        [Fact]
        public void Stage5d2_TerrainObjectRef_IsTheKeyInJson_AndResolvesThroughTheDoorEntity()
        {
            var opts = Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;
            var p = new OperateDoorParams { Door = new TerrainObjectRef("range/R/front"), Verb = DoorVerb.Unlock };
            string json = System.Text.Json.JsonSerializer.Serialize(p, opts);
            Assert.Contains("\"Door\":\"range/R/front\"", json);
            Assert.Contains("\"Unlock\"", json);
            Assert.DoesNotContain("Started", json);                                   // runtime, never authored
            var back = System.Text.Json.JsonSerializer.Deserialize<OperateDoorParams>("{\"Door\":\"range/R/front\",\"Verb\":\"Lock\"}", opts);
            Assert.Equal(p.Door, back.Door);
            Assert.Equal(DoorVerb.Lock, back.Verb);

            using var w = KeyedWorld(out var door);
            Assert.Equal(door, p.Door.Resolve(w));
            Assert.True(new TerrainObjectRef("range/R/back").Resolve(w).IsNull);
            Assert.True(TerrainObjectRef.None.Resolve(w).IsNull);
            Assert.Throws<System.ArgumentException>(() => new TerrainObjectRef(new string('k', 64)));
        }

        /// <summary>
        /// ⭐⭐ 5d-2 — OperateDoor puts the verb's action (id 4–8) and the resolved door on the interaction channel, stays Running
        /// while the executor works, reports its answer ONCE, and is then free to run again; an unknown key fails at once.
        /// </summary>
        [Fact]
        public void Stage5d2_OperateDoor_PutsTheVerbOnTheChannel_AndReportsTheExecutorsAnswerOnce()
        {
            using var w = KeyedWorld(out var door);
            var actor = Walker(w, new Vector3(25, 18.8f, 0));                        // 1.2 m from the doorway: in reach
            var p = new OperateDoorParams { Door = new TerrainObjectRef("range/R/front"), Verb = DoorVerb.Unlock };
            var ex = new DoorActionExecutor(DoorVerb.Unlock);
            var owner = new DoorCommandSystem();

            Assert.Equal(NodeStatus.Running, DoorNodes.OperateDoor(ref p, actor, w));
            ref var ch = ref w.GetComponentRW<InteractionChannel>(actor);
            Assert.Equal(BehaviorConstantsIds.Unlock, ch.ActiveAction);
            Assert.Equal(door, ReadTarget(ref ch));
            ex.OnEnter(actor, ref ch, w);

            int commands = 0;
            NodeStatus s = NodeStatus.Running;
            for (int i = 0; i < 40 && s == NodeStatus.Running; i++) s = Tick(w, ex, actor, 0.1f, owner, ref commands);
            Assert.Equal(NodeStatus.Success, s);
            Assert.Equal(C, w.GetComponentRO<DoorState>(door).State);
            Assert.Equal(1, commands);

            Assert.Equal(NodeStatus.Success, DoorNodes.OperateDoor(ref p, actor, w));   // the answer, once
            Assert.Equal(0u, p.Started);
            Assert.Equal(NodeStatus.Running, DoorNodes.OperateDoor(ref p, actor, w));   // then a fresh activation

            var unknown = new OperateDoorParams { Door = new TerrainObjectRef("range/R/nope"), Verb = DoorVerb.Open };
            Assert.Equal(NodeStatus.Failure, DoorNodes.OperateDoor(ref unknown, actor, w));
        }

        /// <summary>⭐ 5d-2 — MoveToDoor walks to the doorway's near side ON THE ACTOR'S SIDE of the wall, and succeeds in reach.</summary>
        [Fact]
        public void Stage5d2_MoveToDoor_AimsAtTheActorsSideOfTheWall_AndSucceedsInReach()
        {
            using var w = KeyedWorld(out var door);
            var key = new TerrainObjectRef("range/R/front");
            var outside = Walker(w, new Vector3(25, 10, 0));
            var inside = Walker(w, new Vector3(23, 24, 0));

            var p = new MoveToDoorParams { Door = key };
            Assert.Equal(NodeStatus.Running, DoorNodes.MoveToDoor(ref p, outside, w));
            var to = ReadMoveTo(w, outside);
            Assert.Equal(25f, to.X, 2);
            Assert.Equal(20f - DoorNodes.ApproachMetres, to.Y, 2);                    // south of the south wall: the actor's side
            var q = new MoveToDoorParams { Door = key };
            DoorNodes.MoveToDoor(ref q, inside, w);
            Assert.Equal(20f + DoorNodes.ApproachMetres, ReadMoveTo(w, inside).Y, 2);  // inside: north of it

            w.SetComponent(outside, new SimTransform { Position = new Vector3(25, 19, 0), Rotation = Quaternion.Identity });
            Assert.Equal(NodeStatus.Success, DoorNodes.MoveToDoor(ref p, outside, w));
        }

        private static class BehaviorConstantsIds { public const ushort Unlock = Fdp.Toolkit.Behavior.BehaviorConstants.ActionIdUnlockDoor; }

        private static unsafe Entity ReadTarget(ref InteractionChannel ch)
        {
            fixed (byte* b = ch.Params) return ((OpenDoorParams*)b)->TargetDoor;
        }

        private static unsafe Vector3 ReadMoveTo(EntityRepository w, Entity e)
        {
            var ch = w.GetComponent<Fdp.Toolkit.Behavior.Components.LocomotionChannel>(e);
            return ((Fdp.Toolkit.Navigation.MoveToParams*)ch.Params)->Destination;
        }
    }
}
