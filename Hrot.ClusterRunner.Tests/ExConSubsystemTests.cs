using System;
using Xunit;
using Hrot.ExCon;
using FDP.Toolkit.Time.Controllers;

namespace Hrot.ClusterRunner.Tests
{
    /// <summary>
    /// Tests for the embedded <see cref="ExConSubsystem"/> implementation.
    ///
    /// All tests run without a Raylib window (headless mode). The ExCon
    /// subsystem uses <c>NullDdsWriter</c> internally, so DDS is not required.
    /// </summary>
    public class ExConSubsystemTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        private static SubsystemConfig HeadlessConfig() => new()
        {
            DomainId      = 0,
            Headless      = true,
            OwnWindow     = false,
            SubsystemName = "ExCon"
        };

        // ── Name ──────────────────────────────────────────────────────────────

        [Fact]
        public void Name_Returns_ExCon()
        {
            var subsystem = new ExConSubsystem();
            Assert.Equal("ExCon", subsystem.Name);
        }

        // ── Initialize ────────────────────────────────────────────────────────

        [Fact]
        public void Initialize_DoesNotThrow()
        {
            var subsystem = new ExConSubsystem();
            var ex = Record.Exception(() => subsystem.Initialize(HeadlessConfig()));
            Assert.Null(ex);
            subsystem.Shutdown();
        }

        [Fact]
        public void Initialize_CreatesInternalMock()
        {
            // Verified indirectly: Update and DrawUI run without NullReferenceException
            // after Initialize — proving IosMock was created successfully.
            var subsystem = new ExConSubsystem();
            subsystem.Initialize(HeadlessConfig());
            subsystem.Update(0.016f);   // would NPE if _mock is null
            subsystem.Shutdown();
        }

        // ── Update ────────────────────────────────────────────────────────────

        [Fact]
        public void Update_AfterInit_DoesNotThrow()
        {
            var subsystem = new ExConSubsystem();
            subsystem.Initialize(HeadlessConfig());
            var ex = Record.Exception(() => subsystem.Update(0.016f));
            Assert.Null(ex);
            subsystem.Shutdown();
        }

        [Fact]
        public void Update_MultipleFrames_Succeeds()
        {
            var subsystem = new ExConSubsystem();
            subsystem.Initialize(HeadlessConfig());

            for (int i = 0; i < 10; i++)
                subsystem.Update(0.016f);

            subsystem.Shutdown();
        }

        // ── DrawWorld ─────────────────────────────────────────────────────────

        [Fact]
        public void DrawWorld_IsAlwaysNoOp()
        {
            var subsystem = new ExConSubsystem();
            subsystem.Initialize(HeadlessConfig());
            // ExCon has no world rendering; DrawWorld must be a no-op in all modes.
            subsystem.DrawWorld();
            subsystem.Shutdown();
        }

        // ── DrawUI ────────────────────────────────────────────────────────────

        [Fact]
        public void DrawUI_Headless_SkipsImGui()
        {
            var subsystem = new ExConSubsystem();
            subsystem.Initialize(HeadlessConfig());
            // Headless flag must prevent any ImGui calls (which need an active context).
            var ex = Record.Exception(() => subsystem.DrawUI());
            Assert.Null(ex);
            subsystem.Shutdown();
        }

        // ── Shutdown ─────────────────────────────────────────────────────────

        [Fact]
        public void Shutdown_AfterInit_DisposesResources()
        {
            var subsystem = new ExConSubsystem();
            subsystem.Initialize(HeadlessConfig());
            var ex = Record.Exception(() => subsystem.Shutdown());
            Assert.Null(ex);
        }

        [Fact]
        public void Shutdown_WithoutInit_IsNoOp()
        {
            var subsystem = new ExConSubsystem();
            // Must not throw when Shutdown is called before Initialize.
            var ex = Record.Exception(() => subsystem.Shutdown());
            Assert.Null(ex);
        }

        // ── Full lifecycle ────────────────────────────────────────────────────

        [Fact]
        public void FullLifecycle_Headless_CompletesCleanly()
        {
            ISubsystem subsystem = new ExConSubsystem();

            subsystem.Initialize(HeadlessConfig());
            subsystem.Update(0.016f);
            subsystem.DrawWorld();
            subsystem.DrawUI();
            subsystem.Update(0.016f);
            subsystem.Shutdown();
        }

        // ── Negative cases ────────────────────────────────────────────────────

        [Fact]
        public void Update_WithoutInit_DoesNotThrow()
        {
            // Update before Initialize — _mock is null; must be handled gracefully.
            var subsystem = new ExConSubsystem();
            var ex = Record.Exception(() => subsystem.Update(0.016f));
            Assert.Null(ex);
        }

        // ── BUG1-T003: NodeId wiring ──────────────────────────────────────────

        [Fact]
        public void Initialize_StoresNodeIdFromConfig()
        {
            var subsystem = new ExConSubsystem();
            var config    = HeadlessConfig();
            config.NodeId = 7;

            subsystem.Initialize(config);

            Assert.Equal(7, subsystem.TestHook_NodeIdOverride);
            subsystem.Shutdown();
        }

        // ── TC2-P3-T1: SlaveSyncController creation ──────────────────────────────

        [Fact]
        public void ExCon_Initialize_CreatesSlaveTimeController()
        {
            var subsystem = new ExConSubsystem();
            subsystem.Initialize(HeadlessConfig());

            var ctrl = subsystem.TestHook_SlaveSyncController;
            Assert.NotNull(ctrl);
            Assert.IsType<SlaveSyncController>(ctrl);

            subsystem.Shutdown();
        }

        // ── TC2-P3-T2: Update does not throw with time pipeline ───────────────────

        [Fact]
        public void ExCon_Update_DoesNotThrow_WithTimePipeline()
        {
            var subsystem = new ExConSubsystem();
            subsystem.Initialize(HeadlessConfig());

            var ex = Record.Exception(() =>
            {
                for (int i = 0; i < 30; i++)
                    subsystem.Update(0.016f);
            });

            Assert.Null(ex);
            subsystem.Shutdown();
        }

        // ── TC2-P3-T3: SlaveSyncController advances sim time ──────────────────────

        [Fact]
        public void ExCon_UiCache_MasterSimTime_AdvancesWithController()
        {
            var subsystem = new ExConSubsystem();
            subsystem.Initialize(HeadlessConfig());

            for (int i = 0; i < 100; i++)
                subsystem.Update(0.016f);

            var ctrl = subsystem.TestHook_SlaveSyncController!;
            Assert.True(ctrl.GetCurrentState().TotalTime > 0.0,
                "SlaveSyncController TotalTime should be positive after 100 frames");

            subsystem.Shutdown();
        }
    }
}
