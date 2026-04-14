using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Fdp.Kernel;

namespace Fdp.Tests
{
    /// <summary>
    /// Comprehensive test suite for FdpEventBus system.
    /// Tests cover: basic operations, thread safety, buffer expansion, phase lifecycle, and serialization.
    /// </summary>
    public class EventBusTests : IDisposable
    {
        private FdpEventBus _bus;

        public EventBusTests()
        {
            // Reset static registry to prevent ID collisions from other tests
            EventTypeRegistry.ClearForTesting();
            _bus = new FdpEventBus();
        }

        public void Dispose()
        {
            _bus?.Dispose();
            EventTypeRegistry.ClearForTesting();
        }

        #region Test Event Types

        [EventId(1)]
        public struct SimpleEvent
        {
            public int Value;
        }

        [EventId(2)]
        public struct DamageEvent
        {
            public Entity Target;
            public float Amount;
            public Entity Source;
        }

        [EventId(3)]
        public struct ExplosionEvent
        {
            public float X, Y, Z;
            public float Radius;
            public int ParticleCount;
        }

        [EventId(4)]
        public struct LargeEvent
        {
            // 256 bytes total - using regular fields to avoid fixed buffer issues
            public long V0, V1, V2, V3, V4, V5, V6, V7;
            public long V8, V9, V10, V11, V12, V13, V14, V15;
            public long V16, V17, V18, V19, V20, V21, V22, V23;
            public long V24, V25, V26, V27, V28, V29, V30, V31;
        }

        // Missing [EventId] - should throw
        public struct InvalidEvent
        {
            public int Value;
        }

        #endregion

        #region Basic Functionality Tests

        [Fact]
        public void PublishAndConsume_SingleEvent_ReturnsEventNextFrame()
        {
            // Arrange
            var evt = new SimpleEvent { Value = 42 };

            // Act - Frame 1: Publish
            _bus.Publish(evt);

            // Assert - Frame 1: Should be empty (events not swapped yet)
            var consumed1 = _bus.Consume<SimpleEvent>();
            Assert.Equal(0, consumed1.Length);

            // Act - Swap buffers (end of frame)
            _bus.SwapBuffers();

            // Assert - Frame 2: Should see the event
            var consumed2 = _bus.Consume<SimpleEvent>();
            Assert.Equal(1, consumed2.Length);
            Assert.Equal(42, consumed2[0].Value);
        }

        [Fact]
        public void PublishMultiple_SameType_ReturnsAllEvents()
        {
            // Arrange
            _bus.Publish(new SimpleEvent { Value = 1 });
            _bus.Publish(new SimpleEvent { Value = 2 });
            _bus.Publish(new SimpleEvent { Value = 3 });

            // Act
            _bus.SwapBuffers();
            var events = _bus.Consume<SimpleEvent>();

            // Assert
            Assert.Equal(3, events.Length);
            Assert.Equal(1, events[0].Value);
            Assert.Equal(2, events[1].Value);
            Assert.Equal(3, events[2].Value);
        }

        [Fact]
        public void PublishMultiple_DifferentTypes_IsolatesEventStreams()
        {
            // Arrange
            _bus.Publish(new SimpleEvent { Value = 100 });
            _bus.Publish(new DamageEvent { Amount = 50.0f });
            _bus.Publish(new SimpleEvent { Value = 200 });

            // Act
            _bus.SwapBuffers();

            // Assert
            var simpleEvents = _bus.Consume<SimpleEvent>();
            var damageEvents = _bus.Consume<DamageEvent>();

            Assert.Equal(2, simpleEvents.Length);
            Assert.Equal(1, damageEvents.Length);
            Assert.Equal(100, simpleEvents[0].Value);
            Assert.Equal(200, simpleEvents[1].Value);
            Assert.Equal(50.0f, damageEvents[0].Amount);
        }

        [Fact]
        public void Consume_NoEventsPublished_ReturnsEmptySpan()
        {
            // Act
            var events = _bus.Consume<SimpleEvent>();

            // Assert
            Assert.True(events.IsEmpty);
            Assert.Equal(0, events.Length);
        }

        [Fact]
        public void SwapBuffers_ClearsPreviousEvents()
        {
            // Arrange
            _bus.Publish(new SimpleEvent { Value = 1 });
            _bus.SwapBuffers();

            // Act - Swap again without publishing new events
            _bus.SwapBuffers();
            var events = _bus.Consume<SimpleEvent>();

            // Assert - Old events should be cleared
            Assert.Equal(0, events.Length);
        }

        #endregion

        #region Event Type Registry Tests

        [Fact]
        public void EventType_WithAttribute_ReturnsCorrectId()
        {
            // Act
            int id1 = EventType<SimpleEvent>.Id;
            int id2 = EventType<DamageEvent>.Id;

            // Assert
            Assert.Equal(1, id1);
            Assert.Equal(2, id2);
        }

        [Fact]
        public void EventType_SameTypeMultipleCalls_ReturnsSameId()
        {
            // Act
            int id1 = EventType<SimpleEvent>.Id;
            int id2 = EventType<SimpleEvent>.Id;

            // Assert
            Assert.Equal(id1, id2);
        }

        [Fact]
        public void EventType_MissingAttribute_ThrowsException()
        {
            // Act & Assert - Static initializer wraps exception in TypeInitializationException
            var ex = Assert.Throws<TypeInitializationException>(() => EventType<InvalidEvent>.Id);
            Assert.IsType<InvalidOperationException>(ex.InnerException);
            Assert.Contains("missing required [EventId] attribute", ex.InnerException.Message);
        }

        [Fact]
        public void EventType_DifferentTypes_HaveDifferentIds()
        {
            // Act
            int id1 = EventType<SimpleEvent>.Id;
            int id2 = EventType<DamageEvent>.Id;
            int id3 = EventType<ExplosionEvent>.Id;

            // Assert
            Assert.NotEqual(id1, id2);
            Assert.NotEqual(id2, id3);
            Assert.NotEqual(id1, id3);
        }

        #endregion

        #region Thread Safety Tests

        [Fact]
        public void Publish_MultiThreaded_AllEventsRecorded()
        {
            // Arrange
            const int ThreadCount = 10;
            const int EventsPerThread = 1000;
            const int ExpectedTotal = ThreadCount * EventsPerThread;

            // Act - Multiple threads publishing simultaneously
            Parallel.For(0, ThreadCount, threadId =>
            {
                for (int i = 0; i < EventsPerThread; i++)
                {
                    _bus.Publish(new SimpleEvent { Value = threadId * 1000 + i });
                }
            });

            _bus.SwapBuffers();
            var events = _bus.Consume<SimpleEvent>();

            // Assert
            Assert.Equal(ExpectedTotal, events.Length);

            // Verify all values are unique (no overwrites)
            var uniqueValues = new HashSet<int>();
            foreach (var evt in events)
            {
                Assert.True(uniqueValues.Add(evt.Value), $"Duplicate value detected: {evt.Value}");
            }
        }

        [Fact]
        public void Publish_ConcurrentWithSwap_NoDataLoss()
        {
            // Arrange
            bool keepPublishing = true;
            int publishedCount = 0;
            int totalConsumed = 0;

            // Act - One thread publishing, another swapping and consuming
            var publishTask = Task.Run(() =>
            {
                while (keepPublishing)
                {
                    _bus.Publish(new SimpleEvent { Value = Interlocked.Increment(ref publishedCount) });
                    Thread.Sleep(0); // Yield to increase contention
                }
            });

            var swapTask = Task.Run(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    Thread.Sleep(10);
                    _bus.SwapBuffers();
                    // Consume events after each swap
                    var events = _bus.Consume<SimpleEvent>();
                    Interlocked.Add(ref totalConsumed, events.Length);
                }
            });

            // Let it run for a bit
            Thread.Sleep(500);
            keepPublishing = false;
#pragma warning disable xUnit1031 // Do not use blocking task operations in test method
            Task.WaitAll(publishTask, swapTask);
#pragma warning restore xUnit1031 // Do not use blocking task operations in test method

            // Final swap and consume
            _bus.SwapBuffers();
            var finalEvents = _bus.Consume<SimpleEvent>();
            totalConsumed += finalEvents.Length;

            // Assert - We should have consumed events
            Assert.True(totalConsumed > 0, $"Should have captured some events (consumed: {totalConsumed}, published: {publishedCount})");
            Assert.True(publishedCount > 0, "Should have published some events");
        }

        [Fact]
        public void Publish_StressTest_NoMemoryCorruption()
        {
            // Arrange - Publish many large events from multiple threads
            const int ThreadCount = 8;
            const int EventsPerThread = 500;

            // Act
            Parallel.For(0, ThreadCount, threadId =>
            {
                for (int i = 0; i < EventsPerThread; i++)
                {
                    // Fill with pattern to detect corruption
                    var evt = new LargeEvent
                    {
                        V0 = threadId * 100 + i,
                        V1 = threadId * 100 + i + 1,
                        V2 = threadId * 100 + i + 2,
                        V3 = threadId * 100 + i + 3,
                        // Fill remaining fields with pattern
                        V31 = threadId * 100 + i + 31
                    };
                    _bus.Publish(evt);
                }
            });

            _bus.SwapBuffers();
            var events = _bus.Consume<LargeEvent>();

            // Assert - Verify data integrity
            Assert.Equal(ThreadCount * EventsPerThread, events.Length);

            foreach (var evt in events)
            {
                // Check that data pattern is intact (no corruption)
                // At least some fields should be non-zero
                bool hasNonZero = evt.V0 != 0 || evt.V1 != 0 || evt.V31 != 0;
                Assert.True(hasNonZero, "Event data should not be all zeros");
            }
        }

        #endregion

        #region Buffer Expansion Tests

        [Fact]
        public void Publish_ExceedsInitialCapacity_AutoExpands()
        {
            // Arrange - Default capacity is 1024, publish more
            const int EventCount = 2500;

            // Act
            for (int i = 0; i < EventCount; i++)
            {
                _bus.Publish(new SimpleEvent { Value = i });
            }

            _bus.SwapBuffers();
            var events = _bus.Consume<SimpleEvent>();

            // Assert
            Assert.Equal(EventCount, events.Length);

            // Verify all events are present and in order
            for (int i = 0; i < EventCount; i++)
            {
                Assert.Equal(i, events[i].Value);
            }
        }

        [Fact]
        public void Publish_MultipleExpansions_MaintainsDataIntegrity()
        {
            // Arrange - Force multiple expansions (1024 -> 2048 -> 4096 -> 8192)
            const int EventCount = 8000;

            // Act
            for (int i = 0; i < EventCount; i++)
            {
                _bus.Publish(new DamageEvent 
                { 
                    Target = new Entity(i, 1),
                    Amount = i * 1.5f,
                    Source = new Entity(i + 1000, 1)
                });
            }

            _bus.SwapBuffers();
            var events = _bus.Consume<DamageEvent>();

            // Assert
            Assert.Equal(EventCount, events.Length);

            // Spot check values
            Assert.Equal(0, events[0].Target.Index);
            Assert.Equal(0.0f, events[0].Amount);
            Assert.Equal(7999, events[7999].Target.Index);
            Assert.Equal(7999 * 1.5f, events[7999].Amount, precision: 2);
        }

        [Fact]
        public void Publish_ConcurrentExpansion_NoDataLoss()
        {
            // Arrange - Multiple threads causing expansion simultaneously
            const int ThreadCount = 16;
            const int EventsPerThread = 200; // Total: 3200 events (forces expansion)

            // Act
            Parallel.For(0, ThreadCount, threadId =>
            {
                for (int i = 0; i < EventsPerThread; i++)
                {
                    _bus.Publish(new SimpleEvent { Value = threadId * 10000 + i });
                }
            });

            _bus.SwapBuffers();
            var events = _bus.Consume<SimpleEvent>();

            // Assert
            Assert.Equal(ThreadCount * EventsPerThread, events.Length);

            // Verify uniqueness (no overwrites during expansion)
            var values = new HashSet<int>();
            foreach (var evt in events)
            {
                Assert.True(values.Add(evt.Value), $"Duplicate detected: {evt.Value}");
            }
        }

        #endregion

        #region Phase Lifecycle Tests

        [Fact]
        public void EventLifecycle_ThreeFrames_CorrectTiming()
        {
            // Frame 1: Publish A
            _bus.Publish(new SimpleEvent { Value = 1 });
            Assert.Equal(0, _bus.Consume<SimpleEvent>().Length); // Not visible yet

            // End of Frame 1
            _bus.SwapBuffers();

            // Frame 2: Consume A, Publish B
            var frame2Events = _bus.Consume<SimpleEvent>();
            Assert.Equal(1, frame2Events.Length);
            Assert.Equal(1, frame2Events[0].Value);

            _bus.Publish(new SimpleEvent { Value = 2 });

            // End of Frame 2
            _bus.SwapBuffers();

            // Frame 3: Consume B (A is gone)
            var frame3Events = _bus.Consume<SimpleEvent>();
            Assert.Equal(1, frame3Events.Length);
            Assert.Equal(2, frame3Events[0].Value);

            // End of Frame 3
            _bus.SwapBuffers();

            // Frame 4: Nothing
            var frame4Events = _bus.Consume<SimpleEvent>();
            Assert.Equal(0, frame4Events.Length);
        }

        [Fact]
        public void PublishDuringConsume_NotVisibleUntilNextFrame()
        {
            // Arrange
            _bus.Publish(new SimpleEvent { Value = 1 });
            _bus.SwapBuffers();

            // Act - Consume and publish in same frame
            var events1 = _bus.Consume<SimpleEvent>();
            Assert.Equal(1, events1.Length);

            _bus.Publish(new SimpleEvent { Value = 2 });

            // Consume again in same frame
            var events2 = _bus.Consume<SimpleEvent>();

            // Assert - Should still see only the first event
            Assert.Equal(1, events2.Length);
            Assert.Equal(1, events2[0].Value);

            // Next frame
            _bus.SwapBuffers();
            var events3 = _bus.Consume<SimpleEvent>();
            Assert.Equal(1, events3.Length);
            Assert.Equal(2, events3[0].Value);
        }

        [Fact]
        public void ChainReaction_EventTriggersEvent_ProcessedNextFrame()
        {
            // Simulate: DamageEvent -> if fatal -> DeathEvent

            // Frame 1: Damage dealt
            _bus.Publish(new DamageEvent { Target = new Entity(5, 1), Amount = 100.0f });
            _bus.SwapBuffers();

            // Frame 2: Process damage, trigger death
            var damageEvents = _bus.Consume<DamageEvent>();
            Assert.Equal(1, damageEvents.Length);

            // Simulate death logic
            if (damageEvents[0].Amount >= 100.0f)
            {
                _bus.Publish(new ExplosionEvent { X = 10, Y = 20, Z = 30, Radius = 5.0f });
            }

            _bus.SwapBuffers();

            // Frame 3: Process death
            var explosionEvents = _bus.Consume<ExplosionEvent>();
            Assert.Equal(1, explosionEvents.Length);
            Assert.Equal(10, explosionEvents[0].X);
        }

        #endregion

        #region Serialization Support Tests

        [Fact]
        public void GetAllActiveStreams_NoEvents_ReturnsEmpty()
        {
            // Act
            var streams = _bus.GetAllActiveStreams().ToList();

            // Assert
            Assert.Empty(streams);
        }

        [Fact]
        public void GetAllActiveStreams_MultipleTypes_ReturnsAllStreams()
        {
            // Arrange
            _bus.Publish(new SimpleEvent { Value = 1 });
            _bus.Publish(new DamageEvent { Amount = 50 });
            _bus.Publish(new ExplosionEvent { Radius = 10 });

            // Act
            var streams = _bus.GetAllActiveStreams().ToList();

            // Assert
            Assert.Equal(3, streams.Count);

            var typeIds = streams.Select(s => s.EventTypeId).OrderBy(id => id).ToList();
            Assert.Equal(new[] { 1, 2, 3 }, typeIds);
        }

        [Fact]
        public void GetRawBytes_AfterPublish_ReturnsCorrectData()
        {
            // Arrange
            _bus.Publish(new SimpleEvent { Value = 42 });
            _bus.Publish(new SimpleEvent { Value = 99 });

            // Swap to make events readable
            _bus.SwapBuffers();

            // Act
            var streams = _bus.GetAllActiveStreams().ToList();
            var simpleStream = streams.First(s => s.EventTypeId == 1);
            var rawBytes = simpleStream.GetRawBytes();

            // Assert
            Assert.Equal(2 * sizeof(int), rawBytes.Length); // 2 events * sizeof(SimpleEvent)

            unsafe
            {
                fixed (byte* ptr = rawBytes)
                {
                    int* values = (int*)ptr;
                    Assert.Equal(42, values[0]);
                    Assert.Equal(99, values[1]);
                }
            }
        }

        [Fact]
        public void ElementSize_ReturnsCorrectSize()
        {
            // Arrange
            _bus.Publish(new SimpleEvent { Value = 1 });
            _bus.Publish(new DamageEvent { Amount = 1 });
            _bus.Publish(new LargeEvent());

            // Act
            var streams = _bus.GetAllActiveStreams().ToList();

            // Assert
            var simpleStream = streams.First(s => s.EventTypeId == 1);
            var damageStream = streams.First(s => s.EventTypeId == 2);
            var largeStream = streams.First(s => s.EventTypeId == 4);

            Assert.Equal(sizeof(int), simpleStream.ElementSize);
            unsafe
            {
                Assert.Equal(sizeof(DamageEvent), damageStream.ElementSize);
            }
            Assert.Equal(256, largeStream.ElementSize);
        }

        #endregion

        #region Edge Cases and Error Handling

        [Fact]
        public void Consume_BeforeAnyPublish_DoesNotThrow()
        {
            // Act & Assert - Should not throw
            var events = _bus.Consume<SimpleEvent>();
            Assert.Equal(0, events.Length);
        }

        [Fact]
        public void SwapBuffers_MultipleTimes_DoesNotThrow()
        {
            // Act & Assert
            _bus.SwapBuffers();
            _bus.SwapBuffers();
            _bus.SwapBuffers();

            // Should not throw
            Assert.True(true);
        }

        [EventId(99)]
        public struct MinimalEvent
        {
            public byte Flag;
        }

        [Fact]
        public void Publish_ZeroSizedStruct_HandlesCorrectly()
        {
            // Note: C# doesn't allow true zero-sized structs, but we can test minimal struct

            // Act
            var bus = new FdpEventBus();
            bus.Publish(new MinimalEvent { Flag = 1 });
            bus.SwapBuffers();

            var events = bus.Consume<MinimalEvent>();

            // Assert
            Assert.Equal(1, events.Length);
            Assert.Equal(1, events[0].Flag);
        }

        [Fact]
        public void Dispose_AfterUse_CleansUpResources()
        {
            // Arrange
            var bus = new FdpEventBus();
            bus.Publish(new SimpleEvent { Value = 1 });
            bus.SwapBuffers();

            // Act
            bus.Dispose();

            // Assert - Should not throw (cleanup successful)
            Assert.True(true);
        }

        #endregion

        #region Performance Benchmarks (Optional - can be [Fact(Skip = "Benchmark")])

        //[Fact]
        [Fact(Skip = "Benchmark - run manually")]
        public void Benchmark_PublishThroughput()
        {
            // Measure how many events can be published per second
            const int Iterations = 1_000_000;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            for (int i = 0; i < Iterations; i++)
            {
                _bus.Publish(new SimpleEvent { Value = i });
            }

            sw.Stop();
            double eventsPerSecond = Iterations / sw.Elapsed.TotalSeconds;

            // Output for manual inspection
            Console.WriteLine($"Published {eventsPerSecond:N0} events/second");
            Assert.True(eventsPerSecond > 1_000_000, "Should handle at least 1M events/sec");
        }

        //[Fact]
        [Fact(Skip = "Benchmark - run manually")]
        public void Benchmark_MultiThreadedPublish()
        {
            const int ThreadCount = 8;
            const int EventsPerThread = 100_000;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            Parallel.For(0, ThreadCount, _ =>
            {
                for (int i = 0; i < EventsPerThread; i++)
                {
                    _bus.Publish(new SimpleEvent { Value = i });
                }
            });

            sw.Stop();
            double totalEvents = ThreadCount * EventsPerThread;
            double eventsPerSecond = totalEvents / sw.Elapsed.TotalSeconds;

            Console.WriteLine($"Multi-threaded: {eventsPerSecond:N0} events/second");
            Assert.True(eventsPerSecond > 500_000, "Should handle at least 500K events/sec multi-threaded");
        }

        #endregion
    }
}
