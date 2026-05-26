using System;
using System.Collections.Generic;
using System.IO;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Toolkit.ReplayBrowser.Federation;
using Fdp.Toolkit.ReplayBrowser.Support;
using Xunit;

namespace Fdp.Toolkit.ReplayBrowser.Federation.Tests
{
    /// <summary>
    /// Tests for <see cref="FederatedReplayManager"/> (RBF-P1T4, RBF-P2T1, RBF-P2T2).
    /// </summary>
    public class FederatedReplayManagerTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly List<string> _tempFiles = new();

        public FederatedReplayManagerTests()
        {
            ComponentTypeRegistry.Clear();
            _tempDir = Path.Combine(
                Path.GetTempPath(), $"FedMgrTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a minimal valid .fdp + .meta.json pair using the harness.
        /// The .meta.json sidecar is overwritten with the given exerciseId and nodeId so
        /// that LoadGroup validation passes.
        /// </summary>
        private string MakeRecording(Guid exerciseId, int nodeId)
        {
            var path = Path.Combine(_tempDir, $"node{nodeId}_{Guid.NewGuid():N}.fdp");
            // Write a real .fdp using the harness
            using (var harness = new FdpRecordingHarness())
            {
                harness.SpawnEntity().WithComponent(
                    new HarnessPosition { X = nodeId * 1.0f, Y = 0f, Z = 0f });
                harness.Tick().RecordKeyframe(1_000_000L + nodeId * 100_000L);  // distinct wall ticks
                harness.Tick().RecordDelta(2_000_000L + nodeId * 100_000L);
                var harnessPath = harness.BuildToTempFile();
                // Copy the .fdp to our named path
                File.Copy(harnessPath, path, overwrite: true);
            }
            // Overwrite the .meta.json with federation metadata
            var meta = new RecordingMetadata
            {
                ExerciseId = exerciseId,
                NodeId     = nodeId,
            };
            var json = MetadataSerializer.Serialize(meta);
            File.WriteAllText(path + ".meta.json", json);
            return path;
        }

        /// <summary>Creates a .meta.json at <paramref name="fdpPath"/> + ".meta.json" only (no .fdp).</summary>
        private static void WriteMeta(string fdpPath, Guid exerciseId, int nodeId)
        {
            var meta = new RecordingMetadata { ExerciseId = exerciseId, NodeId = nodeId };
            File.WriteAllText(fdpPath + ".meta.json", MetadataSerializer.Serialize(meta));
        }

        // ── RBF-P1T4: LoadGroup ───────────────────────────────────────────────

        /// <summary>
        /// Happy path: three synthetic recordings with identical ExerciseId and distinct
        /// NodeIds {1,2,3} load successfully.
        /// </summary>
        [Fact]
        public void RBF_P1T4_LoadGroup_HappyPath()
        {
            var exerciseId = Guid.NewGuid();
            var path1 = MakeRecording(exerciseId, 1);
            var path2 = MakeRecording(exerciseId, 2);
            var path3 = MakeRecording(exerciseId, 3);

            using var manager = FederatedReplayManager.LoadGroup(
                new[] { path1, path2, path3 });

            Assert.Equal(3, manager.Contexts.Count);
            Assert.True(manager.Contexts.ContainsKey(1));
            Assert.True(manager.Contexts.ContainsKey(2));
            Assert.True(manager.Contexts.ContainsKey(3));
            Assert.Equal(exerciseId, manager.ExerciseId);
        }

        /// <summary>
        /// Two files with different ExerciseIds should throw LoadGroupException.
        /// No contexts should remain alive after the exception.
        /// </summary>
        [Fact]
        public void RBF_P1T4_LoadGroup_RejectsExerciseMismatch()
        {
            var ex1 = Guid.NewGuid();
            var ex2 = Guid.NewGuid();
            var fdpA = Path.Combine(_tempDir, "a.fdp");
            var fdpB = Path.Combine(_tempDir, "b.fdp");
            // Only write meta.json (no real .fdp needed — rejection happens before LoadRecording)
            WriteMeta(fdpA, ex1, 1);
            WriteMeta(fdpB, ex2, 2);

            var exc = Assert.Throws<LoadGroupException>(
                () => FederatedReplayManager.LoadGroup(new[] { fdpA, fdpB }));
            Assert.Contains("exercise mismatch", exc.Message);
        }

        /// <summary>
        /// Two files with the same NodeId should throw LoadGroupException.
        /// </summary>
        [Fact]
        public void RBF_P1T4_LoadGroup_RejectsDuplicateNodeId()
        {
            var exerciseId = Guid.NewGuid();
            var fdpA = Path.Combine(_tempDir, "dup_a.fdp");
            var fdpB = Path.Combine(_tempDir, "dup_b.fdp");
            WriteMeta(fdpA, exerciseId, 5);
            WriteMeta(fdpB, exerciseId, 5);   // same NodeId as A

            var exc = Assert.Throws<LoadGroupException>(
                () => FederatedReplayManager.LoadGroup(new[] { fdpA, fdpB }));
            Assert.Contains("duplicate NodeId", exc.Message);
            Assert.Contains("5", exc.Message);
        }

        /// <summary>
        /// A file whose .meta.json has ExerciseId == Guid.Empty should throw LoadGroupException.
        /// </summary>
        [Fact]
        public void RBF_P1T4_LoadGroup_RejectsEmptyExerciseId()
        {
            var fdpA = Path.Combine(_tempDir, "empty_ex.fdp");
            WriteMeta(fdpA, Guid.Empty, 1);

            var exc = Assert.Throws<LoadGroupException>(
                () => FederatedReplayManager.LoadGroup(new[] { fdpA }));
            Assert.Contains("unknown exercise", exc.Message);
        }

        /// <summary>
        /// When the third path's meta.json is missing, LoadGroup must throw and dispose
        /// any contexts already created for the first two files (verified by absence of
        /// file-lock on the .fdp files — PlaybackController holds no open stream).
        /// </summary>
        [Fact]
        public void RBF_P1T4_LoadGroup_DisposesAllOnError()
        {
            var exerciseId = Guid.NewGuid();
            var path1 = MakeRecording(exerciseId, 1);
            var path2 = MakeRecording(exerciseId, 2);
            // Third path exists as .fdp but has no .meta.json
            var path3 = Path.Combine(_tempDir, "missing_meta.fdp");
            File.WriteAllText(path3, "dummy");  // write .fdp but NOT .meta.json

            Assert.Throws<FileNotFoundException>(
                () => FederatedReplayManager.LoadGroup(new[] { path1, path2, path3 }));

            // Verify first two .fdp files are not locked (contexts were disposed):
            // On Windows, PlaybackController holds a FileStream; if it's still open,
            // File.Open with FileAccess.Write would fail.
            using var f1 = File.Open(path1, FileMode.Open, FileAccess.Read, FileShare.None);
            f1.Close();
            using var f2 = File.Open(path2, FileMode.Open, FileAccess.Read, FileShare.None);
            f2.Close();
        }

        // ── RBF-P2T1: Time state and SeekAll ─────────────────────────────────

        /// <summary>
        /// SetBaseWallTicks seeks both contexts to the new base tick and fires OnTimeChanged.
        /// </summary>
        [Fact]
        public void RBF_P2T1_SeekAll_SeeksEachContext()
        {
            var exerciseId = Guid.NewGuid();
            var path1 = MakeRecording(exerciseId, 1);
            var path2 = MakeRecording(exerciseId, 2);

            using var manager = FederatedReplayManager.LoadGroup(
                new[] { path1, path2 });

            // Both recordings have frame 0 at wall tick ~1_100_000 and frame 1 at ~2_100_000.
            // Seek to frame 0 (base tick = 0 should land at frame 0 for both).
            manager.SetBaseWallTicks(0);
            Assert.Equal(0, manager.Contexts[1].CurrentFrame);
            Assert.Equal(0, manager.Contexts[2].CurrentFrame);
        }

        /// <summary>
        /// SetBaseWallTicks fires OnTimeChanged exactly once.
        /// </summary>
        [Fact]
        public void RBF_P2T1_SetBaseWallTicks_FiresOnTimeChanged()
        {
            var exerciseId = Guid.NewGuid();
            var path1 = MakeRecording(exerciseId, 1);

            using var manager = FederatedReplayManager.LoadGroup(new[] { path1 });
            int fireCount = 0;
            manager.OnTimeChanged += () => fireCount++;

            manager.SetBaseWallTicks(1_000_000L);

            Assert.Equal(1, fireCount);
        }

        /// <summary>
        /// SetNodeOffset fires OnTimeChanged exactly once.
        /// </summary>
        [Fact]
        public void RBF_P2T1_SetNodeOffset_FiresOnTimeChanged()
        {
            var exerciseId = Guid.NewGuid();
            var path1 = MakeRecording(exerciseId, 1);

            using var manager = FederatedReplayManager.LoadGroup(new[] { path1 });
            int fireCount = 0;
            manager.OnTimeChanged += () => fireCount++;

            manager.SetNodeOffset(1, 500_000L);

            Assert.Equal(1, fireCount);
        }

        /// <summary>
        /// A node with no entry in NodeOffsets is seeked using offset 0 (i.e. BaseWallTicks).
        /// </summary>
        [Fact]
        public void RBF_P2T1_DefaultOffsetIsZero()
        {
            var exerciseId = Guid.NewGuid();
            var path1 = MakeRecording(exerciseId, 1);

            using var manager = FederatedReplayManager.LoadGroup(new[] { path1 });
            // No NodeOffsets set for node 1 — offset must default to 0.
            Assert.Empty(manager.NodeOffsets);

            // Setting base ticks should seek using offset 0 (no entry in NodeOffsets).
            int fireCount = 0;
            manager.OnTimeChanged += () => fireCount++;
            manager.SetBaseWallTicks(0L);
            Assert.Equal(1, fireCount);   // event fired once
            Assert.Equal(0, manager.Contexts[1].CurrentFrame);  // seeked to beginning
        }

        /// <summary>
        /// After LoadGroup with nodes {2,5,1}, LocalEntitiesProviderNodeId defaults to 1 (lowest).
        /// </summary>
        [Fact]
        public void RBF_P2T1_LocalEntitiesProvider_DefaultsToLowestNodeId()
        {
            var exerciseId = Guid.NewGuid();
            var path2 = MakeRecording(exerciseId, 2);
            var path5 = MakeRecording(exerciseId, 5);
            var path1 = MakeRecording(exerciseId, 1);

            using var manager = FederatedReplayManager.LoadGroup(
                new[] { path2, path5, path1 });

            Assert.Equal(1, manager.LocalEntitiesProviderNodeId);
        }

        /// <summary>
        /// SetLocalEntitiesProvider fires OnTimeChanged exactly once and does not seek.
        /// </summary>
        [Fact]
        public void RBF_P2T1_SetLocalEntitiesProvider_FiresOnTimeChanged()
        {
            var exerciseId = Guid.NewGuid();
            var path1 = MakeRecording(exerciseId, 1);
            var path2 = MakeRecording(exerciseId, 2);

            using var manager = FederatedReplayManager.LoadGroup(
                new[] { path1, path2 });

            int fireCount = 0;
            manager.OnTimeChanged += () => fireCount++;

            manager.SetLocalEntitiesProvider(2);

            Assert.Equal(1, fireCount);
            Assert.Equal(2, manager.LocalEntitiesProviderNodeId);
        }

        /// <summary>
        /// SetLocalEntitiesProvider with an unknown NodeId throws ArgumentOutOfRangeException.
        /// </summary>
        [Fact]
        public void RBF_P2T1_SetLocalEntitiesProvider_RejectsUnknownNodeId()
        {
            var exerciseId = Guid.NewGuid();
            var path1 = MakeRecording(exerciseId, 1);

            using var manager = FederatedReplayManager.LoadGroup(new[] { path1 });

            Assert.Throws<ArgumentOutOfRangeException>(
                () => manager.SetLocalEntitiesProvider(999));
        }

        // ── RBF-P2T2: Dispose lifecycle ───────────────────────────────────────

        /// <summary>
        /// After Dispose, all contexts have Playback == null (PlaybackController disposed).
        /// </summary>
        [Fact]
        public void RBF_P2T2_Dispose_DisposesAllContexts()
        {
            var exerciseId = Guid.NewGuid();
            var path1 = MakeRecording(exerciseId, 1);
            var path2 = MakeRecording(exerciseId, 2);

            var manager = FederatedReplayManager.LoadGroup(new[] { path1, path2 });
            var ctx1 = manager.Contexts[1];
            var ctx2 = manager.Contexts[2];

            manager.Dispose();

            Assert.Null(ctx1.Playback);
            Assert.Null(ctx2.Playback);
        }

        /// <summary>
        /// Calling Dispose a second time must not throw.
        /// </summary>
        [Fact]
        public void RBF_P2T2_DoubleDispose_NoThrow()
        {
            var exerciseId = Guid.NewGuid();
            var path1 = MakeRecording(exerciseId, 1);

            var manager = FederatedReplayManager.LoadGroup(new[] { path1 });
            manager.Dispose();

            var ex = Record.Exception(() => manager.Dispose());
            Assert.Null(ex);
        }

        // ── D01 fix — per-node offset displacement ────────────────────────────

        /// <summary>
        /// Creates a two-frame .fdp + .meta.json pair.
        /// Frame 0 keyframe at wallTick 1_000_000 with HarnessPosition.X = 1.0.
        /// Frame 1 delta at wallTick 2_000_000 with HarnessPosition.X = 2.0.
        /// </summary>
        private string MakeTwoFrameRecording(Guid exerciseId, int nodeId)
        {
            var path = Path.Combine(_tempDir, $"node{nodeId}_2f_{Guid.NewGuid():N}.fdp");
            using (var harness = new FdpRecordingHarness())
            {
                harness.SpawnEntity().WithComponent(
                    new HarnessPosition { X = 1.0f, Y = 0f, Z = 0f });
                var e = harness.LastSpawned;
                harness.Tick().RecordKeyframe(1_000_000L);
                harness.MutateComponent<HarnessPosition>(e, p => { p.X = 2.0f; return p; });
                harness.Tick().RecordDelta(2_000_000L);
                var harnessPath = harness.BuildToTempFile();
                File.Copy(harnessPath, path, overwrite: true);
            }
            var meta = new RecordingMetadata { ExerciseId = exerciseId, NodeId = nodeId };
            File.WriteAllText(path + ".meta.json", MetadataSerializer.Serialize(meta));
            return path;
        }

        /// <summary>
        /// After SeekAll with a 1_000_000-tick offset applied to node 2:
        /// node 1 (base 1_000_000, offset 0) lands at wallTick 1_000_000 → frame 0 (X=1.0),
        /// node 2 (base 1_000_000, offset 1_000_000) lands at wallTick 2_000_000 → frame 1 (X=2.0).
        /// The two nodes must therefore report different HarnessPosition.X values (D01 fix).
        /// </summary>
        [Fact]
        public void RBF_P2T1_SeekAll_WithNodeOffset_NodeLandsOnDifferentState()
        {
            var exerciseId = Guid.NewGuid();
            var path1 = MakeTwoFrameRecording(exerciseId, 1);
            var path2 = MakeTwoFrameRecording(exerciseId, 2);

            using var manager = FederatedReplayManager.LoadGroup(new[] { path1, path2 });
            manager.SetBaseWallTicks(1_000_000L);      // node 1 → frame 0
            manager.SetNodeOffset(2, 1_000_000L);      // node 2 → frame 1

            // Read HarnessPosition.X from each context's SandboxRepo
            float GetX(int nodeId)
            {
                var repo = manager.Contexts[nodeId].SandboxRepo;
                for (int i = 0; i <= repo.MaxEntityIndex; i++)
                {
                    var e = new Entity(i, repo.GetMetadata(i).Generation);
                    if (repo.IsAlive(e) && repo.HasComponent<HarnessPosition>(e))
                        return repo.GetComponent<HarnessPosition>(e).X;
                }
                throw new InvalidOperationException($"No HarnessPosition on node {nodeId}.");
            }

            float x1 = GetX(1);
            float x2 = GetX(2);
            Assert.NotEqual(x1, x2);
        }

        /// <summary>
        /// SetNodeOffset for a NodeId that was not loaded must throw ArgumentOutOfRangeException (D02 fix).
        /// </summary>
        [Fact]
        public void RBF_P2T1_SetNodeOffset_UnknownNodeId_Throws()
        {
            var exerciseId = Guid.NewGuid();
            var path1 = MakeRecording(exerciseId, 1);

            using var manager = FederatedReplayManager.LoadGroup(new[] { path1 });

            Assert.Throws<ArgumentOutOfRangeException>(() => manager.SetNodeOffset(999, 0L));
        }
    }
}
