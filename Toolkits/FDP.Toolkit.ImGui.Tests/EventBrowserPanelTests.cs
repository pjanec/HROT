using System.Collections.Generic;
using System.Reflection;
using Fdp.Kernel;
using FDP.Toolkit.ImGui.Panels;
using Xunit;

namespace FDP.Toolkit.ImGui.Tests
{
    [Collection("ImGui Sequential")]
    public class EventBrowserPanelTests
    {
        [EventId(101)]
        public struct TestEvent
        {
            public int Value;
            public override string ToString() => $"Value:{Value}";
        }

        [Fact]
        public void Update_CapturesEvents_FromReadBuffer()
        {
            using var fixture = new ImGuiTestFixture();
            using var bus = new FdpEventBus();
            var panel = new EventBrowserPanel();

            // Publish event
            bus.Publish(new TestEvent { Value = 42 });
            
            // Swap to make it readable
            bus.SwapBuffers();

            // Update panel
            panel.Update(bus, 1);
     
            // Render (smoke test)
            fixture.NewFrame();
            panel.Draw();
            fixture.Render();
            
            // To inspect internal state, we need reflection or we trust the smoke test + accessing private field via reflection for specific assertion
            var historyField = typeof(EventBrowserPanel).GetField("_history", BindingFlags.NonPublic | BindingFlags.Instance);
            var history = (System.Collections.IList)historyField.GetValue(panel);
            
            Assert.Equal(1, history.Count);
        }

        [Fact]
        public void Pause_StopsCapture()
        {
            using var bus = new FdpEventBus();
            var panel = new EventBrowserPanel();
            
            // 1. Enable pause via reflection (or public UI interaction if possible, but reflection is easier for unit test)
            var pauseField = typeof(EventBrowserPanel).GetField("_paused", BindingFlags.NonPublic | BindingFlags.Instance);
            pauseField.SetValue(panel, true);

            // 2. Publish event
            bus.Publish(new TestEvent { Value = 99 });
            bus.SwapBuffers();

            // 3. Update
            panel.Update(bus, 2);

            // 4. Assert empty
            var historyField = typeof(EventBrowserPanel).GetField("_history", BindingFlags.NonPublic | BindingFlags.Instance);
            var history = (System.Collections.IList)historyField.GetValue(panel);
            
            Assert.Equal(0, history.Count);
        }

        [Fact]
        public void CapacityLimit_TrimsOldest()
        {
            using var bus = new FdpEventBus();
            var panel = new EventBrowserPanel();
            
            // Set capacity to 2 via reflection
             var capacityField = typeof(EventBrowserPanel).GetField("_capacity", BindingFlags.NonPublic | BindingFlags.Instance);
            capacityField.SetValue(panel, 2);

            // Add 3 events in separate frames
            for (int i = 0; i < 3; i++)
            {
                bus.Publish(new TestEvent { Value = i });
                bus.SwapBuffers();
                panel.Update(bus, (uint)i);
            }

            // Assert count is 2
            var historyField = typeof(EventBrowserPanel).GetField("_history", BindingFlags.NonPublic | BindingFlags.Instance);
            var history = (System.Collections.IList)historyField.GetValue(panel);
            
            Assert.Equal(2, history.Count);
        }
    }
}
