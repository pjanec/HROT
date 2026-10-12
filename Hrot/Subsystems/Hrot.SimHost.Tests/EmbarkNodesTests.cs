using System.Numerics;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Navigation;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>CE-1056</c> I-2d (📄 docs/DESIGN_Entity_Interactions.md §7.3) — the AI asks to board and to leave: the two shared nodes walk on
    /// the locomotion channel, put ONE action on the interaction channel, and forward its answer; the vehicle's owner
    /// (<see cref="EmbarkationSystem"/>) is still the only applier. One world; the interaction dispatcher's calls are made by hand.
    /// </summary>
    public class EmbarkNodesTests
    {
        private readonly EntityRepository _w = new();
        private readonly EmbarkationSystem _owner = new();

        public EmbarkNodesTests()
        {
            SimHostComponentRegistry.RegisterAll(_w);
            CognitiveComponentRegistry.RegisterAll(_w);
        }

        private Entity Vehicle(Vector3 at)
        {
            var v = _w.CreateEntity();
            _w.AddComponent(v, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(v, new PassengerBuffer());
            return v;
        }

        private Entity Soldier(Vector3 at)
        {
            var s = _w.CreateEntity();
            _w.AddComponent(s, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(s, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot | ActorCapabilities.CanInteract });
            _w.AddComponent(s, new LocomotionChannel());
            _w.AddComponent(s, new InteractionChannel());
            return s;
        }

        /// <summary>What the interaction dispatcher does for the action the node just put on the channel: enter, execute, let the
        /// owner apply the raised command next frame, execute again.</summary>
        private void Dispatch(Entity soldier, IActionExecutor<InteractionChannel> executor)
        {
            ref var ch = ref _w.GetComponentRW<InteractionChannel>(soldier);
            executor.OnEnter(soldier, ref ch, _w);
            executor.Execute(soldier, ref ch, _w, 0f);
            _w.Bus.SwapBuffers();
            _owner.Execute(_w, 0f);
            ref var ch2 = ref _w.GetComponentRW<InteractionChannel>(soldier);
            if (ch2.Status == NodeStatus.Running) executor.Execute(soldier, ref ch2, _w, 0f);
        }

        [Fact]
        public void CE1056_Embark_WalksToTheBoardingPoint_ThenBoardsThroughTheOwner()
        {
            var vehicle = Vehicle(new Vector3(50f, 0f, 0f));
            var soldier = Soldier(Vector3.Zero);
            var p = new EmbarkNodeParams { Vehicle = vehicle };

            Assert.Equal(NodeStatus.Running, EmbarkNodes.Embark(ref p, soldier, _w));
            var walk = _w.GetComponentRO<LocomotionChannel>(soldier);
            Assert.Equal(NavigationConstants.ActionIdMoveTo, walk.ActiveAction);
            Assert.Equal(0, _w.GetComponentRO<InteractionChannel>(soldier).ActiveAction);   // not asking yet: out of reach
            Assert.True(Vector3.Distance(Embarkation.BoardingPoint(_w, vehicle), new Vector3(50f, 0f, 0f)) > Embarkation.DefaultHullRadius);

            _w.GetComponentRW<SimTransform>(soldier).Position = Embarkation.BoardingPoint(_w, vehicle);   // the walk arrives
            Assert.Equal(NodeStatus.Running, EmbarkNodes.Embark(ref p, soldier, _w));
            Assert.Equal(0, _w.GetComponentRO<LocomotionChannel>(soldier).ActiveAction);                   // the walk handed back
            Assert.Equal(BehaviorConstants.ActionIdEmbark, _w.GetComponentRO<InteractionChannel>(soldier).ActiveAction);
            Assert.Equal(0, _w.GetComponentRO<PassengerBuffer>(vehicle).Count);                            // asked, not written

            Dispatch(soldier, new EmbarkExecutor());
            Assert.Equal(NodeStatus.Success, EmbarkNodes.Embark(ref p, soldier, _w));
            Assert.Equal(1, _w.GetComponentRO<PassengerBuffer>(vehicle).Count);
            Assert.True(_w.HasComponent<IsEmbarkedTag>(soldier));
            Assert.Equal(0, _w.GetComponentRO<InteractionChannel>(soldier).ActiveAction);                  // the channel handed back
            Assert.Equal(NodeStatus.Success, EmbarkNodes.Embark(ref p, soldier, _w));                      // already aboard: at once
        }

        [Fact]
        public void CE1056_Disembark_LeavesBesideTheHull_ThroughTheOwner_AndNotAboardIsSuccess()
        {
            var vehicle = Vehicle(new Vector3(20f, 0f, 0f));
            var soldier = Soldier(new Vector3(20f, 0f, 0f));
            Embarkation.TryEmbark(_w, soldier, vehicle);
            var p = new DisembarkNodeParams();

            Assert.Equal(NodeStatus.Running, EmbarkNodes.Disembark(ref p, soldier, _w));
            Assert.Equal(BehaviorConstants.ActionIdDisembark, _w.GetComponentRO<InteractionChannel>(soldier).ActiveAction);
            Assert.True(_w.HasComponent<IsEmbarkedTag>(soldier));                                           // asked, not written
            Dispatch(soldier, new DisembarkExecutor());
            Assert.Equal(NodeStatus.Success, EmbarkNodes.Disembark(ref p, soldier, _w));

            Assert.False(_w.HasComponent<IsEmbarkedTag>(soldier));
            Assert.Equal(0, _w.GetComponentRO<PassengerBuffer>(vehicle).Count);
            Assert.True(_w.GetComponentRO<ActorCapabilityState>(soldier).Capabilities.HasFlag(ActorCapabilities.CanMove));
            Assert.True(Vector3.Distance(_w.GetComponentRO<SimTransform>(soldier).Position, new Vector3(20f, 0f, 0f)) > Embarkation.DefaultHullRadius);

            var p2 = new DisembarkNodeParams();
            Assert.Equal(NodeStatus.Success, EmbarkNodes.Disembark(ref p2, soldier, _w));                   // not aboard: nothing to do
            Assert.Equal(0, _w.GetComponentRO<InteractionChannel>(soldier).ActiveAction);
        }

        [Fact]
        public void CE1056_Embark_AGoneVehicle_Fails_AndTheBoardingPointLiesWithinTheBoardingRange()
        {
            var soldier = Soldier(Vector3.Zero);
            var gone = new EmbarkNodeParams { Vehicle = Entity.Null };
            Assert.Equal(NodeStatus.Failure, EmbarkNodes.Embark(ref gone, soldier, _w));

            // the executor's range test agrees with where the walk stops: the boarding point lies within BoardingRange
            var vehicle = Vehicle(new Vector3(5f, 5f, 0f));
            Assert.True(Vector3.Distance(Embarkation.BoardingPoint(_w, vehicle), new Vector3(5f, 5f, 0f)) < Embarkation.BoardingRange(_w, vehicle));
        }
    }
}
