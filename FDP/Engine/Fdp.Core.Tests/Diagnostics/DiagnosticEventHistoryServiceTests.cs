using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Diagnostics;
using Xunit;

namespace Fdp.Core.Tests.Diagnostics
{
    public class DiagnosticEventHistoryServiceTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────

        [EventId(9901)]
        private struct WorldEvent { public int Value; }

        [EventId(9902)]
        private struct OtherEvent { public int Value; }

        /// <summary>
        /// Pushes <paramref name="count"/> dummy events directly via the internal interface
        /// by publishing to a real <see cref="FdpEventBus"/> and calling Capture.
        /// </summary>
        private static void PushEvents(
            DiagnosticEventHistoryService svc,
            FdpEventBus bus,
            int count,
            string eventName = "World")
        {
            for (int i = 0; i < count; i++)
            {
                if (eventName == "World")
                    bus.Publish(new WorldEvent { Value = i });
                else
                    bus.Publish(new OtherEvent { Value = i });

                bus.SwapBuffers();
                svc.Capture(eventName, bus, (uint)i);
            }
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        private sealed class Said { public string Text = ""; public int Count; }

        /// <summary>
        /// ⭐ CE-4565 (X-10; CLAUDE.md HOT PATH) — a steady tick's capture builds NO text: 0 B with a managed event (kept as is) or none, and for an
        /// unmanaged event only its box; the summary is built when the history is READ, with the same text as before. 🔴 Red-proof: the summary
        /// built in <c>Capture</c> again (<c>SummaryOf(evt)</c> per event) ⇒ hundreds of bytes per tick.
        /// </summary>
        [Fact]
        public void X10_ASteadyCapture_BuildsNoText_TheSummaryIsBuiltOnRead()
        {
            using var bus = new FdpEventBus();
            bus.Register<WorldEvent>();
            bus.RegisterManaged<Said>();
            var svc = new DiagnosticEventHistoryService();
            var said = new Said { Text = "contact", Count = 3 };
            for (int warm = 0; warm < 3; warm++) { bus.PublishManaged(said); bus.Publish(new WorldEvent { Value = 7 }); bus.SwapBuffers(); svc.Capture("World", bus, 1); }

            bus.PublishManaged(said); bus.SwapBuffers();                                     // a managed event only
            long before = GC.GetAllocatedBytesForCurrentThread();
            svc.Capture("World", bus, 2);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

            bus.SwapBuffers();                                                               // no event
            before = GC.GetAllocatedBytesForCurrentThread();
            svc.Capture("World", bus, 3);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

            bus.Publish(new WorldEvent { Value = 7 }); bus.SwapBuffers();                    // an unmanaged event: its box, no text
            before = GC.GetAllocatedBytesForCurrentThread();
            svc.Capture("World", bus, 4);
            Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 1, 32);

            var history = svc.GetHistory();
            Assert.Equal("Value:7", history[^1].Summary);                                     // built on read, the same text
            Assert.Equal("Text:contact  Count:3", history[^2].Summary);
        }


        [Fact]
        public void GetHistory_After600Pushes_Returns500()
        {
            using var bus = new FdpEventBus();
            var svc = new DiagnosticEventHistoryService();

            PushEvents(svc, bus, 600);

            var history = svc.GetHistory();

            Assert.Equal(DiagnosticEventHistoryService.Capacity, history.Length);
        }

        [Fact]
        public void GetHistory_WithProviderFilter_ReturnsOnlyMatchingProvider()
        {
            using var bus = new FdpEventBus();
            var svc = new DiagnosticEventHistoryService();

            // Push WorldEvent and OtherEvent alternately.
            for (int i = 0; i < 10; i++)
            {
                bus.Publish(new WorldEvent { Value = i });
                bus.Publish(new OtherEvent { Value = i });
                bus.SwapBuffers();
                svc.Capture("World", bus, (uint)i);
                svc.Capture("Other", bus, (uint)i);
            }

            var filtered = svc.GetHistory(new[] { "World" });

            Assert.True(filtered.Length > 0);
            Assert.All(filtered, e => Assert.Equal("World", e.ProviderName));
        }

        [Fact]
        public void ConcurrentReadWrite_DoesNotThrow()
        {
            using var bus = new FdpEventBus();
            var svc = new DiagnosticEventHistoryService();

            var cts = new CancellationTokenSource();
            var exception = (Exception?)null;

            // Writer thread continuously pushes events.
            var writer = Task.Run(() =>
            {
                try
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        bus.Publish(new WorldEvent { Value = 1 });
                        bus.SwapBuffers();
                        svc.Capture("World", bus, 0);
                    }
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            }, cts.Token);

            // Reader thread continuously reads.
            var reader = Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < 200; i++)
                        _ = svc.GetHistory();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            });

            reader.Wait();
            cts.Cancel();
            writer.Wait(TimeSpan.FromSeconds(2));

            Assert.Null(exception);
        }

        [Fact]
        public void GetHistory_ReturnedSnapshot_IsStableAfterSubsequentWrites()
        {
            using var bus = new FdpEventBus();
            var svc = new DiagnosticEventHistoryService();

            // Prime the buffer with some events.
            PushEvents(svc, bus, 5);

            // Take a snapshot.
            var snapshot = svc.GetHistory();
            int snapshotLength = snapshot.Length;
            string firstTypeName = snapshot[0].TypeName;

            // Write more events AFTER the snapshot was taken.
            PushEvents(svc, bus, 10);

            // The snapshot must not have been mutated.
            Assert.Equal(snapshotLength, snapshot.Length);
            Assert.Equal(firstTypeName, snapshot[0].TypeName);
        }

        [Fact]
        public void ClearHistory_EmptiesBuffer()
        {
            using var bus = new FdpEventBus();
            var svc = new DiagnosticEventHistoryService();

            PushEvents(svc, bus, 10);

            svc.ClearHistory();

            Assert.Empty(svc.GetHistory());
        }
    }
}
