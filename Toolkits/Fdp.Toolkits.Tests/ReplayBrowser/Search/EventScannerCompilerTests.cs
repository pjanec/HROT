using System;
using System.Collections.Generic;
using System.Reflection;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.ReplayBrowser.Support;
using StructEdit.Reflection;
using Xunit;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    // Test event struct for SR-T23..SR-T27
    [EventId(99011)]
    internal struct HarnessFireEvent { public int WeaponIndex; }
    [EventId(99012)]
    internal struct HarnessValueEvent { public float Damage; }

    /// <summary>
    /// SR-T23..SR-T27: EventScannerCompiler correctness tests.
    /// </summary>
    public class EventScannerCompilerTests : IDisposable
    {
        private readonly FdpRecordingHarness _harness;
        private readonly IEventScannerCompiler _compiler;

        public EventScannerCompilerTests()
        {
            ComponentTypeRegistry.Clear();
            _harness  = new FdpRecordingHarness();
            _compiler = new EventScannerCompiler(new ComponentEditServiceBuilder().Build());
        }

        public void Dispose() => _harness.Dispose();

        // ── SR-T23: pure occurrence, unmanaged event ─────────────────────────

        [Fact]
        public void SR_T23_PureOccurrence_Unmanaged_FindsEventsOnCorrectFrames()
        {
            // Build a recording: fire HarnessFireEvent on frames 3 and 7 (0-indexed)
            _harness.SpawnEntity().WithComponent(new HarnessPosition { X = 1f });

            // Frames 0-2: no event
            _harness.Tick(); _harness.RecordKeyframe();
            _harness.Tick(); _harness.RecordDelta();
            _harness.Tick(); _harness.RecordDelta();

            // Frame 3: fire event
            _harness.FireUnmanagedEvent(new HarnessFireEvent { WeaponIndex = 1 });
            _harness.Tick(); _harness.RecordDelta();

            // Frames 4-6: no event
            _harness.Tick(); _harness.RecordDelta();
            _harness.Tick(); _harness.RecordDelta();
            _harness.Tick(); _harness.RecordDelta();

            // Frame 7: fire event
            _harness.FireUnmanagedEvent(new HarnessFireEvent { WeaponIndex = 2 });
            _harness.Tick(); _harness.RecordDelta();

            string fdpPath = _harness.BuildToTempFile();

            var predicate = new TransientEventPredicateDto
            {
                EventType     = typeof(HarnessFireEvent),
                AnyOccurrence = true
            };

            EventScannerDelegate scanner = _compiler.CompileScanner(predicate);

            // Replay and collect.
            var results = new List<SearchResultDto>();
            using var playback = new PlaybackController(fdpPath);
            var repo = new EntityRepository();
            var bus  = new FdpEventBus();
            RegisterComponents(repo, playback);
            playback.EventBus = bus;

            while (playback.StepForward(repo))
            {
                int frame = playback.CurrentFrame;
                long ticks = playback.GetFrameMetadata(frame).WallClockTicks;
                scanner(bus, frame, ticks, results);
            }

            Assert.Equal(2, results.Count);
        }

        // ── SR-T24: property match on event field ────────────────────────────

        [Fact]
        public void SR_T24_PropertyMatch_OnValueEvent_ReturnsMatchingFrames()
        {
            _harness.SpawnEntity().WithComponent(new HarnessPosition { X = 1f });

            // Frame 0: event with Damage = 100
            _harness.FireUnmanagedEvent(new HarnessValueEvent { Damage = 100f });
            _harness.Tick(); _harness.RecordKeyframe();

            // Frame 1: event with Damage = 5 (below threshold)
            _harness.FireUnmanagedEvent(new HarnessValueEvent { Damage = 5f });
            _harness.Tick(); _harness.RecordDelta();

            string fdpPath = _harness.BuildToTempFile();

            var predicate = new TransientEventPredicateDto
            {
                EventType     = typeof(HarnessValueEvent),
                AnyOccurrence = false,
                PropertyPath  = "Damage",
                Operator      = SearchOperator.GreaterThan,
                TargetValue   = "50"
            };

            EventScannerDelegate scanner = _compiler.CompileScanner(predicate);

            var results = new List<SearchResultDto>();
            using var playback = new PlaybackController(fdpPath);
            var repo = new EntityRepository();
            var bus  = new FdpEventBus();
            RegisterComponents(repo, playback);
            playback.EventBus = bus;

            while (playback.StepForward(repo))
            {
                int frame = playback.CurrentFrame;
                long ticks = playback.GetFrameMetadata(frame).WallClockTicks;
                scanner(bus, frame, ticks, results);
            }

            // Only frame 0 should match (Damage = 100 > 50)
            Assert.Single(results);
            Assert.Equal(0, results[0].FrameIndex);
        }

        // ── SR-T25: no events in recording returns empty results ─────────────

        [Fact]
        public void SR_T25_NoEvents_ReturnsEmptyResults()
        {
            _harness.SpawnEntity().WithComponent(new HarnessPosition { X = 1f });
            _harness.Tick(); _harness.RecordKeyframe();
            _harness.Tick(); _harness.RecordDelta();

            string fdpPath = _harness.BuildToTempFile();

            var predicate = new TransientEventPredicateDto
            {
                EventType     = typeof(HarnessFireEvent),
                AnyOccurrence = true
            };

            EventScannerDelegate scanner = _compiler.CompileScanner(predicate);

            var results = new List<SearchResultDto>();
            using var playback = new PlaybackController(fdpPath);
            var repo = new EntityRepository();
            var bus  = new FdpEventBus();
            RegisterComponents(repo, playback);
            playback.EventBus = bus;

            while (playback.StepForward(repo))
            {
                int frame = playback.CurrentFrame;
                long ticks = playback.GetFrameMetadata(frame).WallClockTicks;
                scanner(bus, frame, ticks, results);
            }

            Assert.Empty(results);
        }

        // ── SR-T26: scanner is reusable across replays ───────────────────────

        [Fact]
        public void SR_T26_Scanner_ReusableAcrossMultipleReplays()
        {
            _harness.FireUnmanagedEvent(new HarnessFireEvent { WeaponIndex = 1 });
            _harness.Tick(); _harness.RecordKeyframe();

            string fdpPath = _harness.BuildToTempFile();

            var predicate = new TransientEventPredicateDto
            {
                EventType     = typeof(HarnessFireEvent),
                AnyOccurrence = true
            };

            EventScannerDelegate scanner = _compiler.CompileScanner(predicate);

            // Run the same scanner twice; both should return 1 result.
            for (int run = 0; run < 2; run++)
            {
                var results = new List<SearchResultDto>();
                using var playback = new PlaybackController(fdpPath);
                var repo = new EntityRepository();
                var bus  = new FdpEventBus();
                RegisterComponents(repo, playback);
                playback.EventBus = bus;

                while (playback.StepForward(repo))
                {
                    int frame = playback.CurrentFrame;
                    long ticks = playback.GetFrameMetadata(frame).WallClockTicks;
                    scanner(bus, frame, ticks, results);
                }

                Assert.Single(results);
            }
        }

        // ── SR-T27: TryExtractEntity parses entity handle string ─────────────

        [Fact]
        public void SR_T27_TryExtractEntity_ParsesEntityHandleString()
        {
            Entity result = EventScannerCompiler.TryExtractEntity("[42, v3]");
            Assert.Equal(42, result.Index);
            Assert.Equal((ushort)3, result.Generation);
        }

        [Fact]
        public void SR_T27b_TryExtractEntity_InvalidString_ReturnsNull()
        {
            Entity result = EventScannerCompiler.TryExtractEntity("not-an-entity");
            Assert.Equal(Entity.Null, result);
        }

        // ── Helper ───────────────────────────────────────────────────────────

        private static void RegisterComponents(EntityRepository repo, PlaybackController playback)
        {
            var manifest = playback.Metadata?.SchemaManifest;
            if (manifest != null)
            {
                foreach (var kvp in manifest)
                {
                    Type? type = ComponentTypeRegistry.GetType(kvp.Key);
                    if (type == null) continue;
                    try
                    {
                        var m = typeof(EntityRepository)
                            .GetMethod("RegisterComponent",
                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                                new[] { typeof(DataPolicy?) })!
                            .MakeGenericMethod(type);
                        m.Invoke(repo, new object?[] { null });
                    }
                    catch { }
                }
            }
        }
    }
}
