using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Events;
using Hrot.AI.Behaviors.Brains;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="CommanderNodes"/> (TASK-TI010).
    /// </summary>
    public class CommanderNodesTests
    {
        // ── Tests ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Action_IssueTacticalIntent with a valid (non-zero) SubordinatePacked value
        /// must return Success and publish exactly one <see cref="AssignTacticalIntentEvent"/>
        /// with the correct entity and IntentId.
        /// </summary>
        [Fact]
        public void Action_IssueTacticalIntent_WithValidSubordinate_PublishesEvent()
        {
            using var repo = new EntityRepository();
            var subordinate = repo.CreateEntity();

            var p = new CommanderNodes.IssueTacticalIntentParams
            {
                SubordinatePacked = (long)subordinate.PackedValue,
                IntentTypeOrdinal = 0
            };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { World = repo };

            var result = CommanderNodes.Action_IssueTacticalIntent(ref p, ref state, ref ctx);

            repo.Bus.SwapBuffers();

            Assert.Equal(NodeStatus.Success, result);

            var events = repo.Bus.ReadManaged<AssignTacticalIntentEvent>();
            Assert.Single(events);
            Assert.Equal(subordinate, events[0].Entity);
            Assert.Equal("DefendArea", events[0].IntentId);
        }

        /// <summary>
        /// Action_IssueTacticalIntent with SubordinatePacked == 0 must return Failure
        /// and must not publish any event.
        /// </summary>
        [Fact]
        public void Action_IssueTacticalIntent_WithZeroPacked_ReturnsFailure()
        {
            using var repo = new EntityRepository();

            var p = new CommanderNodes.IssueTacticalIntentParams
            {
                SubordinatePacked = 0,
                IntentTypeOrdinal = 0
            };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { World = repo };

            var result = CommanderNodes.Action_IssueTacticalIntent(ref p, ref state, ref ctx);

            repo.Bus.SwapBuffers();

            Assert.Equal(NodeStatus.Failure, result);

            var events = repo.Bus.ReadManaged<AssignTacticalIntentEvent>();
            Assert.Empty(events);
        }
    }
}
