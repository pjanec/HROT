using System.Collections.Generic;
using System.Text.Json;
using Fdp.Core;
using Fdp.Core.Diagnostics;
using Fdp.Presentation.Panels;
using Moq;
using Xunit;

namespace Fdp.Presentation.Tests
{
    [Collection("ImGui Sequential")]
    public class EventBrowserPanelTests
    {
        private static CapturedEventDto MakeDto(int i) =>
            new CapturedEventDto((uint)i, "World", "TestEvent", false, $"Value:{i}", null);

        [Fact]
        public void Draw_WithMockServiceReturning5Events_Renders5RowsWithoutException()
        {
            using var fixture = new ImGuiTestFixture();

            var mockSvc = new Mock<IDiagnosticEventHistoryService>();
            var events  = new[]
            {
                MakeDto(0), MakeDto(1), MakeDto(2), MakeDto(3), MakeDto(4)
            };
            mockSvc.Setup(s => s.GetHistory(It.IsAny<IReadOnlyList<string>>())).Returns(events);

            var panel = new EventBrowserPanel(mockSvc.Object);

            fixture.NewFrame();
            // No exception expected during Draw.
            panel.Draw("Test");
            fixture.Render();

            // GetHistory was called at least once.
            mockSvc.Verify(s => s.GetHistory(It.IsAny<IReadOnlyList<string>>()), Times.AtLeastOnce());
        }

        [Fact]
        public void Draw_Paused_DoesNotCallGetHistory()
        {
            using var fixture = new ImGuiTestFixture();

            var mockSvc = new Mock<IDiagnosticEventHistoryService>();
            mockSvc.Setup(s => s.GetHistory(It.IsAny<IReadOnlyList<string>>()))
                   .Returns(System.Array.Empty<CapturedEventDto>());

            var panel = new EventBrowserPanel(mockSvc.Object);

            // Access _paused via the public Draw (the Pause checkbox toggles it via ImGui);
            // here we force it via reflection as the simplest approach.
            typeof(EventBrowserPanel)
                .GetField("_paused", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(panel, true);

            fixture.NewFrame();
            panel.Draw("Test");
            fixture.Render();

            // When paused the snapshot is empty without calling the service.
            mockSvc.Verify(s => s.GetHistory(It.IsAny<IReadOnlyList<string>>()), Times.Never());
        }

        [Fact]
        public void Constructor_NullService_ThrowsArgumentNullException()
        {
            Assert.Throws<System.ArgumentNullException>(() => new EventBrowserPanel(null!));
        }

        // ── DD-P3-T01 multi-select tests ──────────────────────────────────

        [Fact]
        public void CtrlClick_TwoRows_SelectsBoth()
        {
            var mockSvc = new Mock<IDiagnosticEventHistoryService>();
            mockSvc.Setup(s => s.GetHistory(It.IsAny<IReadOnlyList<string>>()))
                   .Returns(System.Array.Empty<CapturedEventDto>());
            var panel = new EventBrowserPanel(mockSvc.Object);

            var viewList = new List<CapturedEventDto>
            {
                MakeDto(0), MakeDto(1), MakeDto(2), MakeDto(3),
            };

            // Ctrl+Click row 1 then row 3 — both should be selected.
            panel.HandleRowClick(viewList, 1, ctrl: true, shift: false);
            panel.HandleRowClick(viewList, 3, ctrl: true, shift: false);

            Assert.Equal(2, panel._selectedEvents.Count);
            Assert.Contains(viewList[1], panel._selectedEvents);
            Assert.Contains(viewList[3], panel._selectedEvents);
        }

        [Fact]
        public void ShiftClick_Range_SelectsCorrectItems_AndPreservesLastClickedIndex()
        {
            var mockSvc = new Mock<IDiagnosticEventHistoryService>();
            mockSvc.Setup(s => s.GetHistory(It.IsAny<IReadOnlyList<string>>()))
                   .Returns(System.Array.Empty<CapturedEventDto>());
            var panel = new EventBrowserPanel(mockSvc.Object);

            var viewList = new List<CapturedEventDto>
            {
                MakeDto(10), MakeDto(11), MakeDto(12), MakeDto(13),
                MakeDto(14), MakeDto(15), MakeDto(16), MakeDto(17),
            };

            // Plain-click index 2 first.
            panel.HandleRowClick(viewList, 2, ctrl: false, shift: false);
            Assert.Equal(2, panel._lastClickedIndex);

            // Shift+Click index 5 — should select 2,3,4,5. _lastClickedIndex stays at 2.
            panel.HandleRowClick(viewList, 5, ctrl: false, shift: true);

            Assert.Equal(4, panel._selectedEvents.Count);
            Assert.Contains(viewList[2], panel._selectedEvents);
            Assert.Contains(viewList[3], panel._selectedEvents);
            Assert.Contains(viewList[4], panel._selectedEvents);
            Assert.Contains(viewList[5], panel._selectedEvents);
            Assert.Equal(2, panel._lastClickedIndex);
        }

        [Fact]
        public void BuildCopyJson_TwoEvents_ReturnsJsonArraySortedByFrame()
        {
            var dto1 = new CapturedEventDto(20u, "World", "EventA", false, "sumA", null);
            var dto2 = new CapturedEventDto(10u, "World", "EventB", false, "sumB", null);

            // Pass out-of-order; expect ascending by Frame in output.
            string json = EventBrowserPanel.BuildCopyJson(new[] { dto1, dto2 });

            using var doc = JsonDocument.Parse(json);
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
            Assert.Equal(2, doc.RootElement.GetArrayLength());

            // First element should have lower Frame.
            var first  = doc.RootElement[0];
            var second = doc.RootElement[1];
            Assert.Equal((uint)10, first.GetProperty("Frame").GetUInt32());
            Assert.Equal((uint)20, second.GetProperty("Frame").GetUInt32());
        }

        [Fact]
        public void BuildCopyJson_SingleEvent_ReturnsSingleObject()
        {
            var dto = new CapturedEventDto(5u, "World", "TestEvt", false, "summary", null);
            string json = EventBrowserPanel.BuildCopyJson(new[] { dto });

            using var doc = JsonDocument.Parse(json);
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
            Assert.Equal("TestEvt", doc.RootElement.GetProperty("EventType").GetString());
        }

        [Fact]
        public void BuildCopyJson_FixedString64Payload_SerializesAsString()
        {
            // Simulate a RawEvent that would cause FixedString64 to serialize as string
            // via FdpJsonOptionsRegistry.Indented (which includes FixedString64Converter).
            var payload = new Fdp.Core.FixedString64("HealthDepleted");
            var dto = new CapturedEventDto(1u, "World", "DestructionOrder", false, "summary", payload);

            string json = EventBrowserPanel.BuildCopyJson(new[] { dto });

            // The Payload field should be the string "HealthDepleted", not a struct JSON object.
            using var doc = JsonDocument.Parse(json);
            var payloadElem = doc.RootElement.GetProperty("Payload");
            Assert.Equal(JsonValueKind.String, payloadElem.ValueKind);
            Assert.Equal("HealthDepleted", payloadElem.GetString());
        }
    }
}
