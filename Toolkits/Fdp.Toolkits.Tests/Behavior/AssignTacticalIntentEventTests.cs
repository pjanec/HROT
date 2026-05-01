using Fdp.Core;
using Fdp.Toolkit.Behavior.Events;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// Unit tests for <see cref="AssignTacticalIntentEvent"/> (TASK-TI001).
    /// </summary>
    public class AssignTacticalIntentEventTests
    {
        // ── SC-1 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// SC-1: Publish an event, swap buffers, then read it back from the managed
        /// bus — verifying that <see cref="AssignTacticalIntentEvent.IntentId"/> is
        /// preserved correctly.
        /// </summary>
        [Fact]
        public void AssignTacticalIntentEvent_PublishAndSwap_ReturnsCorrectIntentId()
        {
            using var bus = new FdpEventBus();

            bus.PublishManaged(new AssignTacticalIntentEvent
            {
                IntentId   = "X",
                JsonParams = "{}",
            });
            bus.SwapBuffers();

            var events = bus.ReadManaged<AssignTacticalIntentEvent>();

            Assert.Single(events);
            Assert.Equal("X", events[0].IntentId);
        }

        // ── SC-2 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// SC-2: A default instance must have non-null empty strings for both
        /// <see cref="AssignTacticalIntentEvent.IntentId"/> and
        /// <see cref="AssignTacticalIntentEvent.JsonParams"/>.
        /// </summary>
        [Fact]
        public void AssignTacticalIntentEvent_DefaultInstance_HasEmptyNotNullStrings()
        {
            var evt = new AssignTacticalIntentEvent();

            Assert.NotNull(evt.IntentId);
            Assert.Equal(string.Empty, evt.IntentId);

            Assert.NotNull(evt.JsonParams);
            Assert.Equal(string.Empty, evt.JsonParams);
        }
    }
}
