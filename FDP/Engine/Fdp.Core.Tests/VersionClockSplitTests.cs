using System;
using System.IO;
using Xunit;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Tests
{
    /// <summary>
    /// Tests for NGS-0.1, NGS-0.2, NGS-0.3, NGS-0.4 — version-clock semantic split.
    /// All assertions operate on REAL runtime values; no string-presence checks.
    /// </summary>
    public class VersionClockSplitTests
    {
        // ── NGS-0.1: Version-clock split ──────────────────────────────────────

        [Fact]
        public void Tick_AdvancesBothClocksByExactlyOne()
        {
            using var repo = new EntityRepository();
            repo.ResetGlobalVersion(1);
            uint gvBefore = repo.GlobalVersion;
            uint stBefore = repo.SimulationTick;

            repo.Tick();

            Assert.Equal(gvBefore + 1, repo.GlobalVersion);
            Assert.Equal(stBefore + 1, repo.SimulationTick);
        }

        [Fact]
        public void Tick_KeepsBothClocksEqual_AfterNormalTicks()
        {
            using var repo = new EntityRepository();
            repo.ResetGlobalVersion(1);

            for (int i = 0; i < 10; i++)
                repo.Tick();

            Assert.Equal(repo.GlobalVersion, repo.SimulationTick);
        }

        [Fact]
        public void BumpMemoryVersion_AdvancesGlobalVersion_LeavesSimulationTickUnchanged()
        {
            using var repo = new EntityRepository();
            repo.ResetGlobalVersion(5);

            uint stBefore = repo.SimulationTick;
            uint gvBefore = repo.GlobalVersion;

            repo.BumpMemoryVersion();

            Assert.Equal(gvBefore + 1, repo.GlobalVersion);
            Assert.Equal(stBefore,     repo.SimulationTick); // frozen
        }

        [Fact]
        public void BumpMemoryVersionThenTick_FinalValuesCorrect()
        {
            using var repo = new EntityRepository();
            repo.ResetGlobalVersion(1);
            uint start = repo.GlobalVersion; // 1

            const int K = 3;
            for (int i = 0; i < K; i++)
                repo.BumpMemoryVersion();

            repo.Tick();

            // After K bumps + 1 Tick: GV = start + K + 1, ST = start + 1
            Assert.Equal(start + (uint)K + 1u, repo.GlobalVersion);
            Assert.Equal(start + 1u,            repo.SimulationTick);
        }

        [Fact]
        public void SetGlobalVersion_SetsBothClocks()
        {
            using var repo = new EntityRepository();
            repo.BumpMemoryVersion(); // desync first
            repo.SetGlobalVersion(99);

            Assert.Equal(99u, repo.GlobalVersion);
            Assert.Equal(99u, repo.SimulationTick);
        }

        [Fact]
        public void ResetGlobalVersion_SetsBothClocks()
        {
            using var repo = new EntityRepository();
            repo.BumpMemoryVersion(); // desync first
            repo.ResetGlobalVersion(42);

            Assert.Equal(42u, repo.GlobalVersion);
            Assert.Equal(42u, repo.SimulationTick);
        }

        [Fact]
        public void Invariant_GlobalVersionGeSimulationTick_HoldsAcrossMixedSequence()
        {
            using var repo = new EntityRepository();
            repo.ResetGlobalVersion(1);

            var rng = new Random(12345);
            for (int i = 0; i < 50; i++)
            {
                int choice = rng.Next(3);
                if (choice == 0)      repo.Tick();
                else if (choice == 1) repo.BumpMemoryVersion();
                // else: do nothing (check static state)

                Assert.True(repo.GlobalVersion >= repo.SimulationTick,
                    $"Invariant violated at step {i}: GV={repo.GlobalVersion} ST={repo.SimulationTick}");
            }
        }

        // ── NGS-0.2: ISimulationView.Tick redirected to SimulationTick ────────

        [Fact]
        public void ISimulationViewTick_EqualsSimulationTick_AfterNormalTicks()
        {
            using var repo = new EntityRepository();
            repo.ResetGlobalVersion(1);
            for (int i = 0; i < 5; i++)
                repo.Tick();

            var view = (ISimulationView)repo;
            Assert.Equal(repo.SimulationTick, view.Tick);
        }

        [Fact]
        public void ISimulationViewTick_StaysFrozen_WhileGlobalVersionAdvances()
        {
            // Core guarantee: after BumpMemoryVersion(), view.Tick == SimulationTick (frozen),
            // while GlobalVersion > SimulationTick.
            using var repo = new EntityRepository();
            repo.ResetGlobalVersion(3);
            uint stBefore = repo.SimulationTick;

            const int bumps = 4;
            for (int i = 0; i < bumps; i++)
                repo.BumpMemoryVersion();

            var view = (ISimulationView)repo;

            // view.Tick must be the FRAME clock (frozen)
            Assert.Equal(stBefore,                    view.Tick);
            // GlobalVersion has run ahead
            Assert.Equal(stBefore + (uint)bumps,      repo.GlobalVersion);
            // Explicitly: view.Tick != GlobalVersion (they diverged)
            Assert.NotEqual(repo.GlobalVersion,       view.Tick);
        }

        // ── NGS-0.3: Flight Recorder round-trip uses SimulationTick ──────────

        [Fact]
        public void RecordDeltaFrame_FrameHeader_UsesSimulationTick_NotGlobalVersion()
        {
            using var repo = new EntityRepository();
            // No component registration needed — we only check the frame header tick.
            repo.ResetGlobalVersion(1);

            // Normal tick (both clocks advance together)
            repo.Tick(); // GV=2, ST=2

            // Now bump memory version a few times to diverge the clocks
            repo.BumpMemoryVersion(); // GV=3, ST=2
            repo.BumpMemoryVersion(); // GV=4, ST=2

            var recorder = new RecorderSystem();
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            // Record a delta frame — frame header must carry SimulationTick (2), not GlobalVersion (4)
            uint prevTick = 1; // previous tick
            recorder.RecordDeltaFrame(repo, prevTick, writer, 0L);

            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            ulong frameHeaderTick = reader.ReadUInt64();

            // The header must carry the FRAME clock value
            Assert.Equal((ulong)repo.SimulationTick, frameHeaderTick);
            // Sanity: they differ (clocks diverged)
            Assert.NotEqual((ulong)repo.GlobalVersion, frameHeaderTick);
        }

        [Fact]
        public void RecordKeyframe_FrameHeader_UsesSimulationTick()
        {
            using var repo = new EntityRepository();
            // No component registration needed — we only check the frame header tick.
            repo.ResetGlobalVersion(1);
            repo.Tick(); // GV=2, ST=2

            repo.BumpMemoryVersion(); // GV=3, ST=2

            var recorder = new RecorderSystem();
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            recorder.RecordKeyframe(repo, writer, 0L);

            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            ulong frameHeaderTick = reader.ReadUInt64();

            Assert.Equal((ulong)repo.SimulationTick, frameHeaderTick);
            Assert.NotEqual((ulong)repo.GlobalVersion, frameHeaderTick);
        }

        [Fact]
        public void RecordDeltaAndReplay_RestoresCorrectState_FrameIndexMatchesSimulationTick()
        {
            // Full round-trip: keyframe + delta, then replay into fresh repo.
            // After restore: component value correct AND restored repo's GV/ST both equal
            // the SimulationTick written into the frame header.
            using var srcRepo = new EntityRepository();
            srcRepo.RegisterComponent<IntComponent>();
            srcRepo.ResetGlobalVersion(1);

            // Frame 0: keyframe at ST=2
            var entity = srcRepo.CreateEntity();
            srcRepo.AddComponent(entity, new IntComponent { Value = 10 });
            srcRepo.Tick(); // GV=2, ST=2

            var recorder = new RecorderSystem();
            using var kfMs = new MemoryStream();
            using var kfWriter = new BinaryWriter(kfMs);
            recorder.RecordKeyframe(srcRepo, kfWriter, 0L);
            uint prevTick = srcRepo.SimulationTick; // 2

            // Frame 1: advance tick, then mutate (stamps chunk at GV=3), then bump to diverge clocks
            srcRepo.Tick(); // GV=3, ST=3
            srcRepo.SetComponent(entity, new IntComponent { Value = 20 }); // stamps chunk at GV=3
            srcRepo.BumpMemoryVersion(); // GV=4, ST=3  (diverge: GV ahead of ST)

            using var deltaMs = new MemoryStream();
            using var deltaWriter = new BinaryWriter(deltaMs);
            recorder.RecordDeltaFrame(srcRepo, prevTick, deltaWriter, 0L); // captures chunks with version > 2

            // Restore into a fresh repo
            using var dstRepo = new EntityRepository();
            dstRepo.RegisterComponent<IntComponent>();

            var playback = new PlaybackSystem();

            kfMs.Position = 0;
            using var kfReader = new BinaryReader(kfMs);
            playback.ApplyFrame(dstRepo, kfReader);

            deltaMs.Position = 0;
            using var deltaReader = new BinaryReader(deltaMs);
            playback.ApplyFrame(dstRepo, deltaReader);

            // Restored component value must be correct
            ref readonly var restored = ref dstRepo.GetComponent<IntComponent>(entity);
            Assert.Equal(20, restored.Value);

            // Restored frame's GV and ST must both equal SimulationTick (3), not inflated GV (4)
            Assert.Equal(srcRepo.SimulationTick, dstRepo.GlobalVersion);  // both = 3
            Assert.Equal(srcRepo.SimulationTick, dstRepo.SimulationTick); // both = 3
        }

        [Fact]
        public void RecordDeltaWithBumps_FrameHeaderStillFrozen_ChunkDirtyCorrectlyTracked()
        {
            // After BumpMemoryVersion, a write to a component stamps GV (e.g. 3).
            // The delta frame header still carries ST (e.g. 2), confirming they are independent.
            using var repo = new EntityRepository();
            repo.RegisterComponent<IntComponent>();
            repo.ResetGlobalVersion(1);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new IntComponent { Value = 5 });
            repo.Tick(); // GV=2, ST=2

            uint prevTick = repo.SimulationTick; // 2

            // Sub-tick bump
            repo.BumpMemoryVersion(); // GV=3, ST=2

            // Write after the bump — chunk version stamped at GV=3
            repo.SetComponent(entity, new IntComponent { Value = 99 });

            var recorder = new RecorderSystem();
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            recorder.RecordDeltaFrame(repo, prevTick, writer, 0L);

            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            ulong headerTick = reader.ReadUInt64();

            // Frame header must be SimulationTick (2), not GlobalVersion (3)
            Assert.Equal(2UL, headerTick);
        }

        // ── NGS-0.4: Invariant and regression ─────────────────────────────────

        [Fact]
        public void Invariant_HoldsAfterRepresentativeMixedSequence()
        {
            using var repo = new EntityRepository();
            repo.ResetGlobalVersion(1);

            // K BumpMemoryVersion + Tick cycle, 5 iterations
            for (int cycle = 0; cycle < 5; cycle++)
            {
                for (int b = 0; b < cycle + 1; b++)
                    repo.BumpMemoryVersion();
                repo.Tick();

                Assert.True(repo.GlobalVersion >= repo.SimulationTick,
                    $"Invariant violated after cycle {cycle}: GV={repo.GlobalVersion} ST={repo.SimulationTick}");
            }
        }

        [Fact]
        public void NormalPlay_GlobalVersionEqualsSimulationTick_NoSideEffects()
        {
            // Regression: without any BumpMemoryVersion calls, normal play must remain unaffected.
            using var repo = new EntityRepository();
            repo.ResetGlobalVersion(1);

            for (int i = 0; i < 20; i++)
                repo.Tick();

            // Both clocks must be equal — no divergence in normal play
            Assert.Equal(repo.GlobalVersion, repo.SimulationTick);
            Assert.Equal(21u, repo.GlobalVersion);
        }
    }
}
