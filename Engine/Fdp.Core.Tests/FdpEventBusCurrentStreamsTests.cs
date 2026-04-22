using System;
using System.Collections.Generic;
using Fdp.Core;
using Xunit;

namespace Fdp.Tests
{
    // ── Test event types (IDs 10001/10002 to avoid collisions) ───────────────

    [EventId(10001)]
    internal struct BusCurrentTestNativeEvent
    {
        public int Value;
    }

    internal sealed class BusCurrentTestManagedEvent
    {
        public int Value;
    }

    /// <summary>
    /// Tests for <see cref="FdpEventBus.PopulateCurrentStreams"/> and
    /// <see cref="FdpEventBus.PopulateCurrentManagedStreams"/> (S501).
    /// </summary>
    public sealed class FdpEventBusCurrentStreamsTests : IDisposable
    {
        private readonly FdpEventBus _bus;

        public FdpEventBusCurrentStreamsTests()
        {
            EventTypeRegistry.ClearForTesting();
            _bus = new FdpEventBus();
        }

        public void Dispose()
        {
            _bus.Dispose();
            EventTypeRegistry.ClearForTesting();
        }

        // ── S501-T1: Empty bus returns empty list ─────────────────────────────

        [Fact]
        public void PopulateCurrentStreams_EmptyBus_ReturnsEmptyList()
        {
            var result = new List<INativeEventStream>();
            _bus.PopulateCurrentStreams(result);
            Assert.Empty(result);
        }

        // ── S501-T2: Publish native + SwapBuffers -> stream visible ──────────

        [Fact]
        public void PopulateCurrentStreams_AfterPublishAndSwap_ReturnsStream()
        {
            _bus.Publish(new BusCurrentTestNativeEvent { Value = 42 });
            _bus.SwapBuffers();

            var result = new List<INativeEventStream>();
            _bus.PopulateCurrentStreams(result);

            Assert.Single(result);
            Assert.True(result[0].GetRawBytes().Length > 0);
        }

        // ── S501-T3: Publish native WITHOUT SwapBuffers -> not visible yet ────

        [Fact]
        public void PopulateCurrentStreams_WithoutSwap_ReturnsEmptyList()
        {
            _bus.Publish(new BusCurrentTestNativeEvent { Value = 7 });

            var result = new List<INativeEventStream>();
            _bus.PopulateCurrentStreams(result);

            Assert.Empty(result);
        }

        // ── S501-T4: Publish managed + SwapBuffers -> managed stream visible ──

        [Fact]
        public void PopulateCurrentManagedStreams_AfterPublishAndSwap_ReturnsStream()
        {
            _bus.PublishManaged(new BusCurrentTestManagedEvent { Value = 99 });
            _bus.SwapBuffers();

            var result = new List<IManagedEventStreamInfo>();
            _bus.PopulateCurrentManagedStreams(result);

            Assert.Single(result);
            Assert.True(result[0].CurrentEvents.Count > 0);
        }
    }
}
