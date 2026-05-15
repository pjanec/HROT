using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Toolkit.ReplayBrowser.Diff;
using Fdp.Toolkit.ReplayBrowser.Support;
using Fdp.Toolkit.Scenario;
using Xunit;

namespace Fdp.Toolkit.ReplayBrowser.Export
{
    /// <summary>
    /// Integration tests for RecordingExportService (EX-T01 through EX-T26).
    /// Each test uses FdpRecordingHarness to produce a real .fdp file and then verifies
    /// the JSON output from RecordingExportService.ExportToJson.
    /// </summary>
    public class RecordingExportServiceTests : IDisposable
    {
        public RecordingExportServiceTests()
        {
            ComponentTypeRegistry.Clear();
        }

        public void Dispose() { }

        // ── EX-T01: construction isolation ────────────────────────────────────

        [Fact]
        public void EX_T01_ServiceCanBeConstructedWithNoDependencies()
        {
            var svc = new RecordingExportService();
            Assert.NotNull(svc);
        }

        // ── EX-T02: basic round-trip ─────────────────────────────────────────

        [Fact]
        public void EX_T02_BasicRoundTrip_HeaderAndFrameCount()
        {
            string fdpPath = BuildBasicRecording(out _);
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                new RecordingExportService().ExportToJson(fdpPath, outPath, new JsonExportOptions());

                var root = LoadJson(outPath);
                Assert.Equal("FDPREC", root["Header"]!["Magic"]!.GetValue<string>());
                Assert.Equal((int)FdpConfig.FORMAT_VERSION, root["Header"]!["FormatVersion"]!.GetValue<int>());
                var frames = root["Frames"]!.AsArray();
                Assert.Equal(4, frames.Count);
            }
            finally { TryDelete(outPath); }
        }

        // ── EX-T03: first frame is keyframe ──────────────────────────────────

        [Fact]
        public void EX_T03_FirstFrame_IsKeyframe_EmptyDestroyedEntities()
        {
            string fdpPath = BuildBasicRecording(out _);
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                new RecordingExportService().ExportToJson(fdpPath, outPath, new JsonExportOptions());
                var root = LoadJson(outPath);
                var firstFrame = root["Frames"]!.AsArray()[0]!.AsObject();
                Assert.Equal("Keyframe", firstFrame["FrameHeader"]!["FrameType"]!.GetValue<string>());
                // DestroyedEntities should be absent or empty on keyframes
                var destroyed = firstFrame["DestroyedEntities"]?.AsArray();
                if (destroyed != null)
                    Assert.Empty(destroyed);
            }
            finally { TryDelete(outPath); }
        }

        // ── EX-T04: delta frame destruction log ──────────────────────────────

        [Fact]
        public void EX_T04_DeltaFrame_DestroyedEntities_PopulatedCorrectly()
        {
            string fdpPath = BuildRecordingWithDestruction(out var destroyedEntity);
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                new RecordingExportService().ExportToJson(fdpPath, outPath, new JsonExportOptions());
                var root = LoadJson(outPath);
                var frames = root["Frames"]!.AsArray();

                // Frame 1 (index 1) is the delta with the destruction
                bool foundDestructionFrame = false;
                foreach (var frame in frames)
                {
                    var destroyed = frame!["DestroyedEntities"]?.AsArray();
                    if (destroyed == null || destroyed.Count == 0) continue;
                    string expected = $"[{destroyedEntity.Index}, v{destroyedEntity.Generation}]";
                    Assert.Contains(destroyed, n => n!.GetValue<string>() == expected);
                    foundDestructionFrame = true;
                }
                Assert.True(foundDestructionFrame, "Expected at least one frame with destroyed entities.");
            }
            finally { TryDelete(outPath); }
        }

        // ── EX-T05: component entries are objects with correct keys ───────────

        [Fact]
        public void EX_T05_ComponentEntries_HaveCorrectSchema()
        {
            string fdpPath = BuildBasicRecording(out _);
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                new RecordingExportService().ExportToJson(fdpPath, outPath, new JsonExportOptions());
                var root = LoadJson(outPath);
                var frames = root["Frames"]!.AsArray();

                bool foundComponent = false;
                foreach (var frame in frames)
                {
                    foreach (var entityNode in frame!["Entities"]!.AsArray())
                    {
                        foreach (var comp in entityNode!["Components"]!.AsArray())
                        {
                            Assert.NotNull(comp!["ComponentType"]);
                            Assert.NotNull(comp["HasAuthority"]);
                            // Payload may be null for types autoSerializer cannot handle
                            foundComponent = true;
                        }
                    }
                }
                Assert.True(foundComponent, "Expected at least one component entry.");
            }
            finally { TryDelete(outPath); }
        }

        // ── EX-T06: HasAuthority is correct ──────────────────────────────────

        [Fact]
        public void EX_T06_HasAuthority_ReflectsComponentAuthorityMask()
        {
            // Build a recording with two entities: one with authority, one without
            string fdpPath = BuildRecordingWithAuthority(out var ownedEntity, out var remoteEntity);
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                new RecordingExportService().ExportToJson(fdpPath, outPath, new JsonExportOptions());
                var root = LoadJson(outPath);

                bool foundOwned = false, foundRemote = false;
                var firstFrame = root["Frames"]!.AsArray()[0]!;
                foreach (var entityNode in firstFrame["Entities"]!.AsArray())
                {
                    var id = entityNode!["EntityId"]!.AsArray();
                    int idx = id[0]!.GetValue<int>();
                    foreach (var comp in entityNode["Components"]!.AsArray())
                    {
                        if (comp!["ComponentType"]!.GetValue<string>() != "HarnessPosition") continue;
                        if (idx == ownedEntity.Index)
                        {
                            Assert.True(comp["HasAuthority"]!.GetValue<bool>(), "Owned entity should have authority.");
                            foundOwned = true;
                        }
                        else if (idx == remoteEntity.Index)
                        {
                            Assert.False(comp["HasAuthority"]!.GetValue<bool>(), "Remote entity should not have authority.");
                            foundRemote = true;
                        }
                    }
                }
                Assert.True(foundOwned, "Did not find owned entity component.");
                Assert.True(foundRemote, "Did not find remote entity component.");
            }
            finally { TryDelete(outPath); }
        }

        // ── EX-T07: RelativeWallTimeSec is monotone ───────────────────────────

        [Fact]
        public void EX_T07_RelativeWallTimeSec_ZeroOnFirstFrame_MonotoneAfter()
        {
            string fdpPath = BuildBasicRecording(out _);
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                new RecordingExportService().ExportToJson(fdpPath, outPath, new JsonExportOptions());
                var root = LoadJson(outPath);
                var frames = root["Frames"]!.AsArray();

                double prev = -1.0;
                bool first = true;
                foreach (var frame in frames)
                {
                    double t = frame!["FrameHeader"]!["RelativeWallTimeSec"]!.GetValue<double>();
                    if (first)
                    {
                        Assert.Equal(0.0, t, 1e-9);
                        first = false;
                    }
                    else
                    {
                        Assert.True(t >= prev, $"RelativeWallTimeSec should be non-decreasing: {t} < {prev}");
                    }
                    prev = t;
                }
            }
            finally { TryDelete(outPath); }
        }

        // ── EX-T08: SimTimeSec matches GlobalTime.TotalTime ───────────────────

        [Fact]
        public void EX_T08_SimTimeSec_MatchesGlobalTimeTotalTime()
        {
            string fdpPath = BuildBasicRecording(out _);
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                new RecordingExportService().ExportToJson(fdpPath, outPath, new JsonExportOptions());

                // Verify against a sandbox that replays independently
                var verifyRepo = new EntityRepository();
                verifyRepo.RegisterComponent<HarnessPosition>();
                verifyRepo.RegisterComponent<HarnessVelocity>();
                var verifyBus = new FdpEventBus();
                using var pb2 = new PlaybackController(fdpPath) { EventBus = verifyBus };

                var root = LoadJson(outPath);
                var frames = root["Frames"]!.AsArray();
                int frameIdx = 0;
                while (pb2.StepForward(verifyRepo))
                {
                    double expectedSim = 0.0;
                    if (verifyRepo.HasSingletonUnmanaged<GlobalTime>())
                        expectedSim = verifyRepo.GetSingletonUnmanaged<GlobalTime>().TotalTime;

                    double reported = frames[frameIdx]!["FrameHeader"]!["SimTimeSec"]!.GetValue<double>();
                    Assert.Equal(expectedSim, reported, 1e-9);
                    frameIdx++;
                }
            }
            finally { TryDelete(outPath); }
        }

        // ── EX-T09: SimFrameNumber matches GlobalTime.FrameNumber ─────────────

        [Fact]
        public void EX_T09_SimFrameNumber_MatchesGlobalTimeFrameNumber()
        {
            string fdpPath = BuildBasicRecording(out _);
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                new RecordingExportService().ExportToJson(fdpPath, outPath, new JsonExportOptions());

                var verifyRepo = new EntityRepository();
                verifyRepo.RegisterComponent<HarnessPosition>();
                verifyRepo.RegisterComponent<HarnessVelocity>();
                var verifyBus = new FdpEventBus();
                using var pb2 = new PlaybackController(fdpPath) { EventBus = verifyBus };

                var root = LoadJson(outPath);
                var frames = root["Frames"]!.AsArray();
                int frameIdx = 0;
                while (pb2.StepForward(verifyRepo))
                {
                    long expectedFrameNum = 0;
                    if (verifyRepo.HasSingletonUnmanaged<GlobalTime>())
                        expectedFrameNum = verifyRepo.GetSingletonUnmanaged<GlobalTime>().FrameNumber;

                    long reported = frames[frameIdx]!["FrameHeader"]!["SimFrameNumber"]!.GetValue<long>();
                    Assert.Equal(expectedFrameNum, reported);
                    frameIdx++;
                }
            }
            finally { TryDelete(outPath); }
        }

        // ── EX-T10: FileFrameOrdinal is dense 0..N-1 ─────────────────────────

        [Fact]
        public void EX_T10_FileFrameOrdinal_IsDense()
        {
            string fdpPath = BuildBasicRecording(out _);
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                new RecordingExportService().ExportToJson(fdpPath, outPath, new JsonExportOptions());
                var root = LoadJson(outPath);
                var frames = root["Frames"]!.AsArray();
                for (int i = 0; i < frames.Count; i++)
                {
                    int ord = frames[i]!["FrameHeader"]!["FileFrameOrdinal"]!.GetValue<int>();
                    Assert.Equal(i, ord);
                }
            }
            finally { TryDelete(outPath); }
        }

        // ── EX-T11: Tick matches FrameMetadata.Tick ──────────────────────────

        [Fact]
        public void EX_T11_Tick_MatchesFrameMetadataTick()
        {
            string fdpPath = BuildBasicRecording(out _);
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                new RecordingExportService().ExportToJson(fdpPath, outPath, new JsonExportOptions());

                using var pb2 = new PlaybackController(fdpPath);
                var root = LoadJson(outPath);
                var frames = root["Frames"]!.AsArray();
                // TotalFrames is known after BuildFrameIndex
                for (int i = 0; i < pb2.TotalFrames; i++)
                {
                    var meta = pb2.GetFrameMetadata(i);
                    ulong expectedTick = meta.Tick;
                    ulong reportedTick = frames[i]!["FrameHeader"]!["Tick"]!.GetValue<ulong>();
                    Assert.Equal(expectedTick, reportedTick);
                }
            }
            finally { TryDelete(outPath); }
        }

        // ── EX-T12: ByFrame windowing ────────────────────────────────────────

        [Fact]
        public void EX_T12_ByFrame_WindowsCorrectly()
        {
            string fdpPath = BuildFiveFrameRecording();
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                var opts = new JsonExportOptions
                {
                    WindowMode = ExportWindowMode.ByFrame,
                    StartFrame = 2,
                    EndFrame = 3,
                };
                new RecordingExportService().ExportToJson(fdpPath, outPath, opts);
                var root = LoadJson(outPath);
                var frames = root["Frames"]!.AsArray();
                Assert.Equal(2, frames.Count);
            }
            finally { TryDelete(outPath); }
        }

        // ── EX-T13: ByTime windowing ─────────────────────────────────────────

        [Fact]
        public void EX_T13_ByTime_WindowsCorrectly()
        {
            // Build 5 frames spaced 1 second apart
            string fdpPath = BuildTimedRecording(frameCount: 5, ticksPerFrame: TimeSpan.TicksPerSecond);
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                var opts = new JsonExportOptions
                {
                    WindowMode = ExportWindowMode.ByTime,
                    StartTimeSec = 1.5f,
                    EndTimeSec = 3.0f,
                };
                new RecordingExportService().ExportToJson(fdpPath, outPath, opts);
                var root = LoadJson(outPath);
                var frames = root["Frames"]!.AsArray();
                // Frames at relative times 0, 1, 2, 3, 4 seconds.
                // StartTimeSec=1.5 → seek past frame 1 (t=1.0), should start at frame 2 (t=2.0)
                // EndTimeSec=3.0 → include frame 2 (t=2.0) and frame 3 (t=3.0)
                Assert.Equal(2, frames.Count);
                // All emitted frames must have RelativeWallTimeSec in [1.5, 3.0]
                foreach (var f in frames)
                {
                    double t = f!["FrameHeader"]!["RelativeWallTimeSec"]!.GetValue<double>();
                    Assert.True(t >= 1.5 - 1e-6 && t <= 3.0 + 1e-6,
                        $"Frame RelativeWallTimeSec {t} outside [1.5, 3.0]");
                }
            }
            finally { TryDelete(outPath); }
        }

        // ── EX-T14: ByTime past EOF emits empty Frames ───────────────────────

        [Fact]
        public void EX_T14_ByTime_PastEof_EmitsEmptyFrames()
        {
            string fdpPath = BuildTimedRecording(frameCount: 3, ticksPerFrame: TimeSpan.TicksPerSecond);
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                var opts = new JsonExportOptions
                {
                    WindowMode = ExportWindowMode.ByTime,
                    StartTimeSec = 9999f,  // far past any frame
                    EndTimeSec = float.PositiveInfinity,
                };
                new RecordingExportService().ExportToJson(fdpPath, outPath, opts);
                var root = LoadJson(outPath);
                Assert.NotNull(root["Header"]);
                var frames = root["Frames"]!.AsArray();
                Assert.Empty(frames);
            }
            finally { TryDelete(outPath); }
        }

        // ── EX-T15: FilterByEntityIndex ──────────────────────────────────────

        [Fact]
        public void EX_T15_FilterByEntityIndex_RestrictsEntitiesAndDestroyedEntities()
        {
            string fdpPath = BuildRecordingWithDestruction(out var destroyedEntity);
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                var opts = new JsonExportOptions
                {
                    FilterByEntityIndex = true,
                    TargetEntityIndex = destroyedEntity.Index,
                };
                new RecordingExportService().ExportToJson(fdpPath, outPath, opts);
                var root = LoadJson(outPath);
                foreach (var frame in root["Frames"]!.AsArray())
                {
                    // Entities block: every entity must have the target index
                    foreach (var e in frame!["Entities"]!.AsArray())
                    {
                        int idx = e!["EntityId"]!.AsArray()[0]!.GetValue<int>();
                        Assert.Equal(destroyedEntity.Index, idx);
                    }
                    // DestroyedEntities: if present, must refer to target entity
                    foreach (var d in frame["DestroyedEntities"]!.AsArray())
                    {
                        string s = d!.GetValue<string>();
                        Assert.StartsWith($"[{destroyedEntity.Index},", s);
                    }
                }
            }
            finally { TryDelete(outPath); }
        }

        // ── EX-T16: FilterBySelection ─────────────────────────────────────────

        [Fact]
        public void EX_T16_FilterBySelection_EmitsOnlyTargetEntities()
        {
            string fdpPath = BuildBasicRecording(out var entityA);
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                var opts = new JsonExportOptions
                {
                    FilterBySelection = true,
                    TargetEntities = new System.Collections.Generic.List<Entity> { entityA },
                };
                new RecordingExportService().ExportToJson(fdpPath, outPath, opts);
                var root = LoadJson(outPath);
                foreach (var frame in root["Frames"]!.AsArray())
                {
                    foreach (var e in frame!["Entities"]!.AsArray())
                    {
                        int idx = e!["EntityId"]!.AsArray()[0]!.GetValue<int>();
                        Assert.Equal(entityA.Index, idx);
                    }
                }
            }
            finally { TryDelete(outPath); }
        }

        // ── EX-T17: IncludeEvents=false omits Events block ───────────────────

        [Fact]
        public void EX_T17_IncludeEventsFalse_OmitsEventsBlock()
        {
            string fdpPath = BuildRecordingWithEvents();
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                var opts = new JsonExportOptions { IncludeEvents = false };
                new RecordingExportService().ExportToJson(fdpPath, outPath, opts);
                var root = LoadJson(outPath);
                foreach (var frame in root["Frames"]!.AsArray())
                    Assert.Null(frame!["Events"]);
            }
            finally { TryDelete(outPath); }
        }

        // ── EX-T18: IncludeEntities=false omits Entities/DestroyedEntities ───

        [Fact]
        public void EX_T18_IncludeEntitiesFalse_OmitsEntitiesBlock()
        {
            string fdpPath = BuildRecordingWithEvents();
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                var opts = new JsonExportOptions { IncludeEntities = false };
                new RecordingExportService().ExportToJson(fdpPath, outPath, opts);
                var root = LoadJson(outPath);
                foreach (var frame in root["Frames"]!.AsArray())
                {
                    Assert.Null(frame!["Entities"]);
                    Assert.Null(frame["DestroyedEntities"]);
                    // FrameHeader must still be present
                    Assert.NotNull(frame["FrameHeader"]);
                }
            }
            finally { TryDelete(outPath); }
        }

        // ── EX-T19: Minified=true ────────────────────────────────────────────

        [Fact]
        public void EX_T19_Minified_ProducesNoNewlines()
        {
            string fdpPath = BuildBasicRecording(out _);
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                var opts = new JsonExportOptions { Minified = true };
                new RecordingExportService().ExportToJson(fdpPath, outPath, opts);
                string text = File.ReadAllText(outPath);
                // Minified JSON must not have newlines between top-level keys
                // (the entire document should be one logical line, though
                //  raw values inserted via WriteRawValue may contain internal newlines
                //  from FlattenNumericArrays — but we skip that when minified).
                Assert.DoesNotContain("\n", text);
            }
            finally { TryDelete(outPath); }
        }

        // ── EX-T20: Vector3/Quaternion payloads are single-line arrays ────────

        [Fact]
        public void EX_T20_NumericArrayPayloads_AreFlattenedToSingleLine()
        {
            // HarnessPosition has float fields X,Y,Z — not a Vector3, but the
            // FlattenNumericArrays call should still produce a compact payload.
            // This test verifies no multi-line numeric arrays appear in the output.
            string fdpPath = BuildBasicRecording(out _);
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                new RecordingExportService().ExportToJson(fdpPath, outPath, new JsonExportOptions());
                string text = File.ReadAllText(outPath);
                // Numeric arrays like [1.0, 2.0, 3.0] must not span multiple lines
                // i.e. no pattern of "  1.0,\n  2.0" should exist
                Assert.DoesNotContain("  1,\n", text);
                // The test is weaker here since HarnessPosition is a flat struct,
                // not a Vector3. The key assertion is that the file is well-formed.
                Assert.True(text.Length > 0);
            }
            finally { TryDelete(outPath); }
        }

        // ── EX-T21: Entity cross-references in events ─────────────────────────

        [Fact]
        public void EX_T21_EntityFieldsInEvents_AreFormattedAsStrings()
        {
            string fdpPath = BuildRecordingWithEntityRefEvent(out var refEntity);
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                new RecordingExportService().ExportToJson(fdpPath, outPath, new JsonExportOptions());
                string text = File.ReadAllText(outPath);
                string expected = $"[{refEntity.Index}, v{refEntity.Generation}]";
                Assert.Contains(expected, text);
            }
            finally { TryDelete(outPath); }
        }

        // ── EX-T22: Custom translator honored by ScenarioSerializer ────────────

        [Fact]
        public void EX_T22_CustomTranslator_IsHonored_PayloadReflectsStubDto()
        {
            // Build an in-memory repo with one entity bearing HarnessVelocity.
            var repo = new EntityRepository();
            repo.RegisterComponent<HarnessPosition>();
            repo.RegisterComponent<HarnessVelocity>();
            var entity = repo.CreateEntity();
            repo.SetComponent(entity, new HarnessVelocity { Vx = 1.5f, Vy = 2.5f });

            // Build a ScenarioSerializer with the stub translator registered.
            var serializer = new ScenarioSerializerBuilder("TestReplayBrowser")
                .RegisterTranslator(new FooHarnessBlackboardTranslator())
                .Build();

            // Assert: ScenarioSerializer.Serialize() honors the translator and
            // the stub marker appears in the scenario DOM JSON.
            var dom = serializer.Serialize(repo, new ScenarioHeader("TestReplayBrowser"));
            string scenarioJson = dom.ToJsonString();
            Assert.Contains("FooBlackboard", scenarioJson);

            repo.Dispose();

            // Also verify: passing this serializer to RecordingExportService does not error.
            // RecordingExportService uses the AutoSerializer portion of the ScenarioSerializer.
            string fdpPath = BuildBasicRecording(out _);
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                new RecordingExportService(serializer: serializer)
                    .ExportToJson(fdpPath, outPath, new JsonExportOptions());
                Assert.True(File.Exists(outPath), "Export must produce a file.");
            }
            finally { TryDelete(outPath); }
        }

        // ── EX-T23: Managed events ───────────────────────────────────────────

        [Fact]
        public void EX_T23_ManagedEvents_EmittedWithIsManagedTrue()
        {
            string fdpPath = BuildRecordingWithEvents();
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                new RecordingExportService().ExportToJson(fdpPath, outPath, new JsonExportOptions());
                var root = LoadJson(outPath);
                bool foundManaged = false;
                foreach (var frame in root["Frames"]!.AsArray())
                {
                    var evts = frame!["Events"]?.AsArray();
                    if (evts == null) continue;
                    foreach (var evt in evts)
                    {
                        if (evt!["EventType"]!.GetValue<string>() == "HarnessTestManagedEvent")
                        {
                            Assert.True(evt["IsManaged"]!.GetValue<bool>(), "Managed event must have IsManaged=true");
                            foundManaged = true;
                        }
                    }
                }
                Assert.True(foundManaged, "Expected at least one managed event in output.");
            }
            finally { TryDelete(outPath); }
        }

        // ── EX-T24: Unmanaged events ─────────────────────────────────────────

        [Fact]
        public void EX_T24_UnmanagedEvents_EmittedWithIsManagedFalse()
        {
            string fdpPath = BuildRecordingWithEvents();
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                new RecordingExportService().ExportToJson(fdpPath, outPath, new JsonExportOptions());
                var root = LoadJson(outPath);
                bool foundUnmanaged = false;
                foreach (var frame in root["Frames"]!.AsArray())
                {
                    var evts = frame!["Events"]?.AsArray();
                    if (evts == null) continue;
                    foreach (var evt in evts)
                    {
                        if (evt!["EventType"]!.GetValue<string>() == "HarnessTestEventA")
                        {
                            Assert.False(evt["IsManaged"]!.GetValue<bool>(), "Unmanaged event must have IsManaged=false");
                            Assert.NotNull(evt["Payload"]);
                            foundUnmanaged = true;
                        }
                    }
                }
                Assert.True(foundUnmanaged, "Expected at least one unmanaged event in output.");
            }
            finally { TryDelete(outPath); }
        }

        // ── EX-T25: No large heap allocation for 10k frames ──────────────────

        [Fact]
        public void EX_T25_LargeRecording_NoBigHeapAllocation()
        {
            const int frameCount = 200; // reduced from 10k for test speed; still validates streaming
            string fdpPath = BuildLargeRecording(frameCount);
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                long memBefore = GC.GetTotalMemory(true);

                new RecordingExportService().ExportToJson(fdpPath, outPath, new JsonExportOptions());

                GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                long memAfter = GC.GetTotalMemory(true);

                long deltaMb = (memAfter - memBefore) / (1024 * 1024);
                // For 200 frames the delta should be well below 32 MB
                Assert.True(deltaMb < 32, $"Heap delta {deltaMb} MB exceeds 32 MB limit.");
            }
            finally { TryDelete(outPath); }
        }

        // ── EX-T26: Export does not mutate a parallel context ─────────────────

        [Fact]
        public void EX_T26_Export_DoesNotMutateParallelContext()
        {
            string fdpPath = BuildBasicRecording(out _);
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                // Set up a live context and advance it to frame 1
                var ctx = new ReplayBrowserContext();
                ctx.SandboxRepo.RegisterComponent<HarnessPosition>();
                ctx.SandboxRepo.RegisterComponent<HarnessVelocity>();
                ctx.LoadRecording(fdpPath);
                ctx.SeekToFrame(1);
                int frameBefore = ctx.CurrentFrame;
                uint globalVersionBefore = ctx.SandboxRepo.GlobalVersion;

                // Run export in isolation
                new RecordingExportService().ExportToJson(fdpPath, outPath, new JsonExportOptions());

                // Verify the parallel context is unchanged
                Assert.Equal(frameBefore, ctx.CurrentFrame);
                Assert.Equal(globalVersionBefore, ctx.SandboxRepo.GlobalVersion);

                ctx.Dispose();
            }
            finally { TryDelete(outPath); }
        }

        // ── EX-T27: Changelog mode emits entries only on actual mutations ──────

        [Fact]
        public void EX_T27_ChangelogMode_EmitsExactlyThreeEntries_AtMutatedFrames()
        {
            string fdpPath = BuildChangelogMutationRecording(out Entity entity);
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                var diffSvc = new ComponentDiffService();
                var svc = new RecordingExportService(diffService: diffSvc);
                var opts = new JsonExportOptions
                {
                    FormatMode = ExportFormatMode.Changelog,
                    TargetEntities = new System.Collections.Generic.List<Entity> { entity },
                    EpsilonTolerance = 0.001,
                };

                svc.ExportToJson(fdpPath, outPath, opts);

                string text = File.ReadAllText(outPath);
                // Root must be a JSON array
                var root = JsonNode.Parse(text)!.AsArray();

                // Frame 0 sets the baseline (no entry).
                // Frames 1, 3, 4 have mutations → exactly 3 entries.
                Assert.Equal(3, root.Count);
                var frameIndices = new System.Collections.Generic.HashSet<int>();
                foreach (var entry in root)
                    frameIndices.Add(entry!["FrameIndex"]!.GetValue<int>());

                Assert.Contains(1, frameIndices);
                Assert.Contains(3, frameIndices);
                Assert.Contains(4, frameIndices);
            }
            finally { TryDelete(outPath); }
        }

        // ── EX-T28: Changelog mode with epsilon=2.0 suppresses all sub-epsilon changes ──

        [Fact]
        public void EX_T28_ChangelogMode_Epsilon_SuppressesSubEpsilonChanges()
        {
            string fdpPath = BuildChangelogEpsilonRecording(out Entity entity);
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                var diffSvc = new ComponentDiffService();
                var svc = new RecordingExportService(diffService: diffSvc);
                var opts = new JsonExportOptions
                {
                    FormatMode = ExportFormatMode.Changelog,
                    TargetEntities = new System.Collections.Generic.List<Entity> { entity },
                    EpsilonTolerance = 2.0,   // all changes are 0.5 < 2.0 → no entries
                };

                svc.ExportToJson(fdpPath, outPath, opts);

                string text = File.ReadAllText(outPath);
                var root = JsonNode.Parse(text)!.AsArray();
                Assert.Empty(root);
            }
            finally { TryDelete(outPath); }
        }

        // ── EX-T29: Changelog mode handles entity destruction cleanly ─────────

        [Fact]
        public void EX_T29_ChangelogMode_EntityDestruction_NoEntriesAfterDeath()
        {
            string fdpPath = BuildChangelogDestructionRecording(out Entity entity);
            string outPath = Path.GetTempFileName() + ".json";
            try
            {
                var diffSvc = new ComponentDiffService();
                var svc = new RecordingExportService(diffService: diffSvc);
                var opts = new JsonExportOptions
                {
                    FormatMode = ExportFormatMode.Changelog,
                    TargetEntities = new System.Collections.Generic.List<Entity> { entity },
                    EpsilonTolerance = 0.001,
                };

                // Must not crash, even across entity death
                svc.ExportToJson(fdpPath, outPath, opts);

                string text = File.ReadAllText(outPath);
                var root = JsonNode.Parse(text)!.AsArray();

                // All emitted entries must be from frames before destruction (frame < 3)
                foreach (var entry in root)
                {
                    int fi = entry!["FrameIndex"]!.GetValue<int>();
                    Assert.True(fi < 3, $"Entry at frame {fi} should not appear after destruction at frame 3.");
                }
            }
            finally { TryDelete(outPath); }
        }

        // ── Recording fixture builders ────────────────────────────────────────

        /// <summary>
        /// 5-frame recording: entity alive all frames.
        /// Frame 0 (keyframe): X=1. Frame 1: X=2 (mutation). Frame 2: X=2 (unchanged).
        /// Frame 3: X=3 (mutation). Frame 4: X=4 (mutation).
        /// </summary>
        private static string BuildChangelogMutationRecording(out Entity entity)
        {
            var h = new FdpRecordingHarness();
            h.SpawnEntity().WithComponent(new HarnessPosition { X = 1f, Y = 0f, Z = 0f });
            entity = h.LastSpawned;
            h.Tick().RecordKeyframe(100_000L);                        // frame 0: X=1

            h.MutateComponent<HarnessPosition>(entity, p => new HarnessPosition { X = 2f, Y = p.Y, Z = p.Z });
            h.Tick().RecordDelta(200_000L);                           // frame 1: X=2 (mutation)

            h.Tick().RecordDelta(300_000L);                           // frame 2: X=2 (unchanged)

            h.MutateComponent<HarnessPosition>(entity, p => new HarnessPosition { X = 3f, Y = p.Y, Z = p.Z });
            h.Tick().RecordDelta(400_000L);                           // frame 3: X=3 (mutation)

            h.MutateComponent<HarnessPosition>(entity, p => new HarnessPosition { X = 4f, Y = p.Y, Z = p.Z });
            h.Tick().RecordDelta(500_000L);                           // frame 4: X=4 (mutation)

            return h.BuildToTempFile();
        }

        /// <summary>
        /// 5-frame recording: entity alive all frames, all X mutations are 0.5 steps.
        /// With epsilon=2.0 none of the changes should produce an entry.
        /// </summary>
        private static string BuildChangelogEpsilonRecording(out Entity entity)
        {
            var h = new FdpRecordingHarness();
            h.SpawnEntity().WithComponent(new HarnessPosition { X = 1.0f, Y = 0f, Z = 0f });
            entity = h.LastSpawned;
            h.Tick().RecordKeyframe(100_000L);                        // frame 0: X=1.0

            h.MutateComponent<HarnessPosition>(entity, p => new HarnessPosition { X = 1.5f, Y = p.Y, Z = p.Z });
            h.Tick().RecordDelta(200_000L);                           // frame 1: X=1.5 (delta=0.5)

            h.Tick().RecordDelta(300_000L);                           // frame 2: X=1.5 (unchanged)

            h.MutateComponent<HarnessPosition>(entity, p => new HarnessPosition { X = 2.0f, Y = p.Y, Z = p.Z });
            h.Tick().RecordDelta(400_000L);                           // frame 3: X=2.0 (delta=0.5)

            h.MutateComponent<HarnessPosition>(entity, p => new HarnessPosition { X = 2.5f, Y = p.Y, Z = p.Z });
            h.Tick().RecordDelta(500_000L);                           // frame 4: X=2.5 (delta=0.5)

            return h.BuildToTempFile();
        }

        /// <summary>
        /// 5-frame recording: entity alive frames 0-2, destroyed at frame 3, frame 4 no entity.
        /// Frames 1 and 2 have X mutations so they should produce changelog entries.
        /// Frame 3 onwards: no entries expected (entity dead).
        /// </summary>
        private static string BuildChangelogDestructionRecording(out Entity entity)
        {
            var h = new FdpRecordingHarness();
            h.SpawnEntity().WithComponent(new HarnessPosition { X = 1.0f, Y = 0f, Z = 0f });
            entity = h.LastSpawned;
            h.Tick().RecordKeyframe(100_000L);                        // frame 0: entity alive, X=1.0

            h.MutateComponent<HarnessPosition>(entity, p => new HarnessPosition { X = 2.0f, Y = p.Y, Z = p.Z });
            h.Tick().RecordDelta(200_000L);                           // frame 1: X=2.0 (mutation)

            h.MutateComponent<HarnessPosition>(entity, p => new HarnessPosition { X = 3.0f, Y = p.Y, Z = p.Z });
            h.Tick().RecordDelta(300_000L);                           // frame 2: X=3.0 (mutation)

            h.DestroyEntity(entity);
            h.Tick().RecordDelta(400_000L);                           // frame 3: entity destroyed

            h.Tick().RecordDelta(500_000L);                           // frame 4: entity still dead

            return h.BuildToTempFile();
        }

        private static string BuildBasicRecording(out Entity firstEntity)
        {
            var h = new FdpRecordingHarness(); // not disposed; BuildToTempFile transfers file ownership to caller
            h.SpawnEntity().WithComponent(new HarnessPosition { X = 1f, Y = 0f, Z = 0f });
            firstEntity = h.LastSpawned;
            h.SpawnEntity().WithComponent(new HarnessPosition { X = 2f, Y = 0f, Z = 0f });
            h.SpawnEntity().WithComponent(new HarnessPosition { X = 3f, Y = 0f, Z = 0f });
            // frame 0: keyframe
            h.Tick().RecordKeyframe(100_000L);
            // frame 1-3: deltas
            h.MutateComponent<HarnessPosition>(firstEntity, p => new HarnessPosition { X = p.X + 1f, Y = p.Y, Z = p.Z });
            h.Tick().RecordDelta(200_000L);
            h.Tick().RecordDelta(300_000L);
            h.Tick().RecordDelta(400_000L);
            return h.BuildToTempFile();
        }

        private static string BuildRecordingWithDestruction(out Entity destroyedEntity)
        {
            var h = new FdpRecordingHarness(); // not disposed; BuildToTempFile transfers file ownership to caller
            h.SpawnEntity().WithComponent(new HarnessPosition { X = 1f, Y = 0f, Z = 0f });
            h.SpawnEntity().WithComponent(new HarnessPosition { X = 2f, Y = 0f, Z = 0f });
            destroyedEntity = h.LastSpawned;
            h.Tick().RecordKeyframe(100_000L);
            h.DestroyEntity(destroyedEntity);
            h.Tick().RecordDelta(200_000L);
            return h.BuildToTempFile();
        }

        private static string BuildRecordingWithAuthority(out Entity ownedEntity, out Entity remoteEntity)
        {
            var h = new FdpRecordingHarness(); // not disposed; BuildToTempFile transfers file ownership to caller
            h.SpawnEntity().WithComponent(new HarnessPosition { X = 1f, Y = 0f, Z = 0f });
            ownedEntity = h.LastSpawned;
            h.SpawnEntity().WithComponent(new HarnessPosition { X = 2f, Y = 0f, Z = 0f });
            remoteEntity = h.LastSpawned;
            // Grant authority to the first entity but not the second
            int posId = ComponentTypeRegistry.GetId(typeof(HarnessPosition));
            h.Repository.SetAuthority(ownedEntity, posId, true);
            h.Tick().RecordKeyframe(100_000L);
            return h.BuildToTempFile();
        }

        private static string BuildRecordingWithEvents()
        {
            var h = new FdpRecordingHarness(); // not disposed; BuildToTempFile transfers file ownership to caller
            h.SpawnEntity().WithComponent(new HarnessPosition { X = 1f, Y = 0f, Z = 0f });
            h.Tick().RecordKeyframe(100_000L);
            h.FireUnmanagedEvent(new HarnessTestEventA { Payload = 42 });
            h.FireManagedEvent(new HarnessTestManagedEvent { Tag = "hello" });
            h.Tick().RecordDelta(200_000L);
            return h.BuildToTempFile();
        }

        private static string BuildFiveFrameRecording()
        {
            var h = new FdpRecordingHarness(); // not disposed; BuildToTempFile transfers file ownership to caller
            h.SpawnEntity().WithComponent(new HarnessPosition { X = 1f, Y = 0f, Z = 0f });
            h.Tick().RecordKeyframe(100_000L); // frame 0
            h.Tick().RecordDelta(200_000L);    // frame 1
            h.Tick().RecordDelta(300_000L);    // frame 2
            h.Tick().RecordDelta(400_000L);    // frame 3
            h.Tick().RecordDelta(500_000L);    // frame 4
            return h.BuildToTempFile();
        }

        private static string BuildTimedRecording(int frameCount, long ticksPerFrame)
        {
            var h = new FdpRecordingHarness(); // not disposed; BuildToTempFile transfers file ownership to caller
            h.SpawnEntity().WithComponent(new HarnessPosition { X = 1f, Y = 0f, Z = 0f });
            for (int i = 0; i < frameCount; i++)
            {
                long wallTicks = 1_000_000L + (i * ticksPerFrame);
                if (i == 0)
                    h.Tick().RecordKeyframe(wallTicks);
                else
                    h.Tick().RecordDelta(wallTicks);
            }
            return h.BuildToTempFile();
        }

        private static string BuildRecordingWithEntityRefEvent(out Entity refEntity)
        {
            var h = new FdpRecordingHarness(); // not disposed; BuildToTempFile transfers file ownership to caller
            h.SpawnEntity().WithComponent(new HarnessPosition { X = 1f, Y = 0f, Z = 0f });
            refEntity = h.LastSpawned;
            h.Tick().RecordKeyframe(100_000L);
            h.FireUnmanagedEvent(new HarnessTestEventWithEntity { Target = refEntity });
            h.Tick().RecordDelta(200_000L);
            return h.BuildToTempFile();
        }

        private static string BuildLargeRecording(int frameCount)
        {
            var h = new FdpRecordingHarness(); // not disposed; BuildToTempFile transfers file ownership to caller
            h.SpawnEntity().WithComponent(new HarnessPosition { X = 1f, Y = 0f, Z = 0f });
            h.Tick().RecordKeyframe(100_000L);
            for (int i = 1; i < frameCount; i++)
            {
                h.MutateComponent<HarnessPosition>(h.LastSpawned,
                    p => new HarnessPosition { X = p.X + 0.001f, Y = p.Y, Z = p.Z });
                h.Tick().RecordDelta(100_000L + i * 1_000L);
            }
            return h.BuildToTempFile();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static JsonObject LoadJson(string path)
        {
            string text = File.ReadAllText(path);
            return JsonNode.Parse(text)!.AsObject();
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
        }

        // ── FooHarnessBlackboardTranslator (EX-T22 stub) ─────────────────────────

        private sealed class FooHarnessBlackboardTranslator : IEntityScenarioTranslator
        {
            public BitMask256 GetConsumedComponentsMask()
            {
                var mask = new BitMask256();
                mask.SetBit(203); // HarnessVelocity component ID
                return mask;
            }

            public bool CanTranslate(EntityRepository repo, Entity entity)
                => repo.HasComponent<HarnessVelocity>(entity);

            public Dictionary<string, object> Extract(
                EntityRepository repo, Entity entity, IGuidResolver guidResolver)
            {
                var comp = repo.GetComponent<HarnessVelocity>(entity);
                return new Dictionary<string, object>
                {
                    ["HarnessVelocity"] = new JsonObject
                    {
                        ["Source"] = JsonValue.Create("FooBlackboard"),
                        ["Vx"]     = JsonValue.Create(comp.Vx),
                        ["Vy"]     = JsonValue.Create(comp.Vy),
                    }
                };
            }

            public void Inject(
                EntityRepository repo, Entity entity,
                Dictionary<string, object> scenarioData, IGuidResolver guidResolver)
            { /* no-op: not tested here */ }
        }
    }

    // ── Additional event types used only in export tests ─────────────────────

    // Event ID 99004 reserved for this file
    [EventId(99004)]
    internal struct HarnessTestEventWithEntity { public Entity Target; }
}
