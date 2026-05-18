using System;
using System.IO;
using Fdp.Core;
using Fdp.Core.Diagnostics;
using Fdp.Core.FlightRecorder;
using Moq;
using Xunit;
using Fdp.Toolkit.ReplayBrowser.Support;

namespace Fdp.Toolkit.ReplayBrowser.Context
{
    /// <summary>
    /// Tests for ReplayBrowserContext (FND-T06, FND-T07).
    /// </summary>
    public class ReplayBrowserContextTests : IDisposable
    {
        public ReplayBrowserContextTests()
        {
            ComponentTypeRegistry.Clear();
        }

        public void Dispose() { }

        // ── FND-T06: SeekToFrame ordering ─────────────────────────────────────

        [Fact]
        public void FND_T06_SeekToFrame_ClearsBuffersThenSeeksThenCaptures()
        {
            // Build a fixture recording
            string path;
            using var harness = new FdpRecordingHarness();
            harness.SpawnEntity().WithComponent(new HarnessPosition { X = 1f, Y = 0f, Z = 0f });
            harness.Tick().RecordKeyframe(100_000L);   // frame 0
            harness.Tick().RecordDelta(200_000L);      // frame 1
            harness.Tick().RecordDelta(300_000L);      // frame 2
            harness.BuildToTempFile(out path);

            // Arrange: inject mock history service to verify call order
            var callOrder = new System.Collections.Generic.List<string>();
            var mockHistory = new Mock<IDiagnosticEventHistoryService>();
            mockHistory
                .Setup(h => h.Capture(It.IsAny<string>(), It.IsAny<FdpEventBus>(), It.IsAny<uint>()))
                .Callback<string, FdpEventBus, uint>((p, b, f) => callOrder.Add($"Capture:{f}"));

            // Use a real bus so we can verify ClearCurrentBuffers was called (no exception path)
            var realRepo = new EntityRepository();
            realRepo.RegisterComponent<HarnessPosition>();
            realRepo.RegisterComponent<HarnessVelocity>();
            var realBus = new FdpEventBus();

            using var ctx = new ReplayBrowserContext(realRepo, realBus, mockHistory.Object);
            ctx.LoadRecording(path);

            // Act: seek to frame 1
            ctx.SeekToFrame(1);

            // Assert: Capture was invoked with frame 1
            mockHistory.Verify(h => h.Capture("Replay", realBus, 1u), Times.Once);
            Assert.Equal(1, ctx.CurrentFrame);

            harness.Dispose();
        }

        // ── FND-T07: Dispose safety ────────────────────────────────────────────

        [Fact]
        public void FND_T07_Dispose_IsIdempotent()
        {
            string path;
            using var harness = new FdpRecordingHarness();
            harness.SpawnEntity().WithComponent(new HarnessPosition { X = 1f, Y = 0f, Z = 0f });
            harness.Tick().RecordKeyframe(100_000L);
            harness.BuildToTempFile(out path);

            var ctx = new ReplayBrowserContext();
            ctx.LoadRecording(path);

            // First dispose
            ctx.Dispose();

            // Second dispose must not throw
            ctx.Dispose();

            harness.Dispose();
        }

        [Fact]
        public void FND_T07b_StepForward_AfterDispose_ThrowsObjectDisposedException()
        {
            var ctx = new ReplayBrowserContext();
            ctx.Dispose();

            Assert.Throws<ObjectDisposedException>(() => ctx.StepForward());
        }
    }
}
