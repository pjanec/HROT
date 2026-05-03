using System.Collections.Generic;
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
            new CapturedEventDto((uint)i, "TestEvent", false, $"Value:{i}", null);

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
    }
}
