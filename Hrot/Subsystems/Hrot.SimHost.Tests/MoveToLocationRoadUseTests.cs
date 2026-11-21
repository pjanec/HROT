using System.Text.Json;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Hrot.AI.Behaviors.Brains;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>CE-3130</c> (R-230) — the authored <c>MoveToLocation</c> carries the actor's road use from the scenario/mission JSON
    /// to the MoveTo it issues. 📄 docs/designs/navig-2/Navigation_Design_v2_0.md §5.2a D1.
    /// </summary>
    public sealed class MoveToLocationRoadUseTests
    {
        [Theory]
        [InlineData("{\"target\":[10,20,0],\"roadUse\":\"Never\"}", RoadUse.Never)]
        [InlineData("{\"target\":[10,20,0],\"roadUse\":\"StronglyPrefer\"}", RoadUse.StronglyPrefer)]
        [InlineData("{\"target\":[10,20,0]}", RoadUse.Unspecified)]
        public void CE3130_TheAuthoredRoadUse_ReachesTheBlackboard(string json, RoadUse expected)
        {
            var authored = JsonSerializer.Deserialize<MoveToLocationParamsJsonDto>(json, BehaviorParams.JsonOptions);   // CE-3184: the params options (fields)
            var block = CgfNodes.ConvertMoveTo(authored);
            Assert.Equal(expected, block.RoadUse);
            Assert.Equal(10f, block.X);
        }

        /// <summary>An order that names no road use is written without the key, so authored files do not churn.</summary>
        [Fact]
        public void CE3130_AnUnspecifiedRoadUse_IsNotWritten_ANamedOneIsWrittenByName()
        {
            Assert.DoesNotContain("roadUse", JsonSerializer.Serialize(new MoveToLocationParamsJsonDto { Location = new System.Numerics.Vector3(1, 2, 0) }, BehaviorParams.JsonOptions));
            Assert.Contains("\"roadUse\":\"Prefer\"",
                JsonSerializer.Serialize(new MoveToLocationParamsJsonDto { Location = new System.Numerics.Vector3(1, 2, 0), RoadUse = RoadUse.Prefer }, BehaviorParams.JsonOptions));
        }

        /// <summary>⭐ CE-3483 — the one JSON factory code orders a move with (the hill attack's baseline, SimHost's click-to-move) is read
        /// back by the resolver as the point it was given, under the CURRENT key and never the retired X / Y.</summary>
        [Fact]
        public void CE3483_TheOrderJson_ResolvesToThePointItWasGiven()
        {
            string json = MoveToLocationParamsJsonDto.Json(447.5f, -120.25f, 15f, 5f);
            Assert.Contains("\"target\"", json);
            Assert.DoesNotContain("\"X\"", json);
            var block = CgfNodes.ConvertMoveTo(JsonSerializer.Deserialize<MoveToLocationParamsJsonDto>(json, BehaviorParams.JsonOptions));
            Assert.Equal(447.5f, block.X, 3);
            Assert.Equal(-120.25f, block.Y, 3);
            Assert.Equal(15f, block.Speed, 3);
            Assert.Equal(5f, block.ArrivalRadius, 3);
        }

        [Fact]
        public unsafe void CE3130_TheMoveToLocationNode_IssuesTheMoveWithTheRoadUse()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<LocomotionChannel>();
            var e = repo.CreateEntity();
            repo.AddComponent(e, new LocomotionChannel());

            var p = new CgfNodes.MoveToLocationParams { X = 50f, Y = 60f, Speed = 2f, ArrivalRadius = 3f, RoadUse = RoadUse.Never };
            CgfNodes.Action_WriteMoveToChannel(ref p, e, repo);

            var ch = repo.GetComponent<LocomotionChannel>(e);
            MoveToParams issued;
            byte* src = ch.Params;
            issued = *(MoveToParams*)src;
            Assert.Equal(NavigationConstants.ActionIdMoveTo, ch.ActiveAction);
            Assert.Equal(RoadUse.Never, issued.RoadUse);
            Assert.Equal(50f, issued.Destination.X);
        }

        // ⭐⭐ CE-3458 (P1, the bug queue) — a move that cannot finish FAILS UP (Navigation v2 §3.1 / §3.3: FailedUnreachable / FailedBlocked ⇒ a BTree
        //   Failure; a retry is the caller's policy). 📌 Measured live: an off-map destination re-issued itself every ~0.1 s for good, the unit stood still.

        private static (EntityRepository Repo, Entity E) MoverWithBehaviour(uint behaviourInstance)
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<LocomotionChannel>();
            repo.RegisterComponent<BehaviorState>();
            var e = repo.CreateEntity();
            repo.AddComponent(e, new LocomotionChannel());
            repo.AddComponent(e, new BehaviorState { InstanceId = behaviourInstance });
            return (repo, e);
        }

        /// <summary>The executor reports the dispatched action failed (what <c>MoveToExecutor</c> does on FailedUnreachable).</summary>
        private static void ExecutorFails(EntityRepository repo, Entity e)
        {
            ref var ch = ref repo.GetComponentRW<LocomotionChannel>(e);
            ch.DispatchedInstanceId = ch.ActionInstanceId;
            ch.Status = Fbt.NodeStatus.Failure;
        }

        /// <summary>
        /// ⭐ <c>CE-3458</c> — the MoveToLocation node whose OWN move failed answers <b>Failure</b> and issues nothing new; a failure an EARLIER behaviour
        /// left on the channel re-activates (that is not this move's); a fallback move ELSEWHERE in the same behaviour still issues.
        /// 🔴 Red-proof: drop the <c>OwnMoveFailed</c> line ⇒ the failed move re-issues (Running, a new action instance) — the measured loop.
        /// </summary>
        [Fact]
        public void CE3458_AMoveThatFailed_FailsUp_NeverReissuesItself()
        {
            var (repo, e) = MoverWithBehaviour(7);
            var p = new CgfNodes.MoveToLocationParams { X = 318f, Y = 40f, Speed = 2f, ArrivalRadius = 2f };
            Assert.Equal(Fbt.NodeStatus.Running, CgfNodes.Action_WriteMoveToChannel(ref p, e, repo));
            uint issued = repo.GetComponent<LocomotionChannel>(e).ActionInstanceId;
            ExecutorFails(repo, e);

            for (int tick = 0; tick < 5; tick++)
                Assert.Equal(Fbt.NodeStatus.Failure, CgfNodes.Action_WriteMoveToChannel(ref p, e, repo));
            Assert.Equal(issued, repo.GetComponent<LocomotionChannel>(e).ActionInstanceId);   // never re-issued

            // a fallback move to another place, same behaviour: issues
            var fallback = new CgfNodes.MoveToLocationParams { X = 200f, Y = 78f, Speed = 2f, ArrivalRadius = 2f };
            Assert.Equal(Fbt.NodeStatus.Running, CgfNodes.Action_WriteMoveToChannel(ref fallback, e, repo));
            Assert.Equal(issued + 1, repo.GetComponent<LocomotionChannel>(e).ActionInstanceId);

            // the same failed move, but left by an EARLIER behaviour: the new behaviour's move re-activates
            ExecutorFails(repo, e);
            repo.GetComponentRW<BehaviorState>(e).InstanceId = 8;
            Assert.Equal(Fbt.NodeStatus.Running, CgfNodes.Action_WriteMoveToChannel(ref fallback, e, repo));
            repo.Dispose();
        }

        /// <summary>⭐ <c>CE-3458</c> / <c>CE-9023</c> — the same for a ROUTE: a route whose drive failed (an immobilised vehicle, a blocked road) answers
        /// <b>Failure</b> instead of "running" it for good. 🔴 Red-proof: drop the <c>OwnRouteFailed</c> line ⇒ Running.</summary>
        [Fact]
        public void CE3458_ARouteThatFailed_FailsUp()
        {
            var (repo, e) = MoverWithBehaviour(3);
            var p = new CgfNodes.FollowRouteParams { TrajectoryId = 11, RouteNetworkId = 4242, Speed = 5f };
            Assert.Equal(Fbt.NodeStatus.Running, CgfNodes.Action_WriteFollowRouteChannel(ref p, e, repo));
            ExecutorFails(repo, e);
            Assert.Equal(Fbt.NodeStatus.Failure, CgfNodes.Action_WriteFollowRouteChannel(ref p, e, repo));
            repo.Dispose();
        }

        /// <summary>⭐ <c>CE-9023</c> (the locomotion half) — a vehicle IMMOBILISED while it drives its route (an IED: the hit clears
        /// <c>CanMove</c>, <c>CombatLife</c>) ends the route through the REAL dispatcher (CE-3091 ⇒ the channel Failure) and the FollowRoute
        /// node answers <b>Failure</b> for as long as the vehicle cannot move — 📌 before, the disabled APC "ran" <c>FollowRoute</c> for good
        /// and satisfied <c>uc-urban-battle</c>'s "the convoy moves on" row. 🔴 Red-proof: drop the <c>OwnRouteFailed</c> line ⇒ Running.</summary>
        [Fact]
        public void CE9023_AnImmobilisedVehicle_StopsRunningItsRoute()
        {
            var (repo, e) = MoverWithBehaviour(5);
            repo.RegisterComponent<ActorCapabilityState>();
            repo.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });
            var dispatcher = new Fdp.Toolkit.Behavior.Systems.LocomotionDispatcherSystem();
            var p = new CgfNodes.FollowRouteParams { TrajectoryId = 11, RouteNetworkId = 4242, Speed = 5f };

            Assert.Equal(Fbt.NodeStatus.Running, CgfNodes.Action_WriteFollowRouteChannel(ref p, e, repo));
            dispatcher.Execute(repo, 0.016f);                                   // the route starts
            uint issued = repo.GetComponent<LocomotionChannel>(e).ActionInstanceId;

            repo.GetComponentRW<ActorCapabilityState>(e).Capabilities &= ~ActorCapabilities.CanMove;   // the IED
            for (int tick = 0; tick < 5; tick++)
            {
                dispatcher.Execute(repo, 0.016f);
                Assert.Equal(Fbt.NodeStatus.Failure, CgfNodes.Action_WriteFollowRouteChannel(ref p, e, repo));
            }
            Assert.Equal(issued, repo.GetComponent<LocomotionChannel>(e).ActionInstanceId);   // never re-issued
            repo.Dispose();
        }
    }
}
