using System;
using System.Threading;
using Xunit;
using Bagira.Runner.Abstractions;
using Bagira.Runner.Models;
using Bagira.Runner.Services;

namespace Bagira.Runner.Tests
{
    /// <summary>
    /// Tests for the embedded <see cref="IgSubsystem"/> implementation.
    ///
    /// All tests run without a Raylib window (headless mode). DDS network
    /// initialisation is attempted but failures are caught internally by
    /// <see cref="Bagira.IG.IgApplication.InitializeEmbedded"/>, so tests
    /// are DDS-environment-independent.
    /// </summary>
    public class IgSubsystemTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        private static SubsystemConfig HeadlessConfig() => new()
        {
            DomainId      = 99,       // isolated test domain
            Headless      = true,
            OwnWindow     = false,
            SubsystemName = "IG"
        };

        // ── Name ──────────────────────────────────────────────────────────────

        [Fact]
        public void Name_Returns_IG()
        {
            var subsystem = new IgSubsystem();
            Assert.Equal("IG", subsystem.Name);
        }

        // ── Initialize ────────────────────────────────────────────────────────

        [Fact]
        public void Initialize_Headless_DoesNotThrow()
        {
            using var subsystem = new ResourceTrackedIgSubsystem();
            var ex = Record.Exception(() =>
                subsystem.Inner.Initialize(HeadlessConfig()));
            Assert.Null(ex);
        }

        // ── Update ────────────────────────────────────────────────────────────

        [Fact]
        public void Update_AfterHeadlessInit_DoesNotThrow()
        {
            var subsystem = new IgSubsystem();
            subsystem.Initialize(HeadlessConfig());
            // In headless mode, Update must NOT call any Raylib/ImGui functions.
            var ex = Record.Exception(() => subsystem.Update(0.016f));
            Assert.Null(ex);
            subsystem.Shutdown();
        }

        [Fact]
        public void Update_MultipleFrames_AccumulatesWithoutError()
        {
            var subsystem = new IgSubsystem();
            subsystem.Initialize(HeadlessConfig());

            for (int i = 0; i < 5; i++)
                subsystem.Update(0.016f);

            subsystem.Shutdown();
        }

        // ── DrawWorld / DrawUI ────────────────────────────────────────────────

        [Fact]
        public void DrawWorld_Headless_IsNoOp()
        {
            var subsystem = new IgSubsystem();
            subsystem.Initialize(HeadlessConfig());
            // Must not throw — headless flag suppresses all Raylib calls.
            subsystem.DrawWorld();
            subsystem.Shutdown();
        }

        [Fact]
        public void DrawUI_Headless_IsNoOp()
        {
            var subsystem = new IgSubsystem();
            subsystem.Initialize(HeadlessConfig());
            // Must not throw — headless flag suppresses all ImGui calls.
            subsystem.DrawUI();
            subsystem.Shutdown();
        }

        // ── Shutdown ─────────────────────────────────────────────────────────

        [Fact]
        public void Shutdown_AfterInit_DoesNotThrow()
        {
            var subsystem = new IgSubsystem();
            subsystem.Initialize(HeadlessConfig());
            var ex = Record.Exception(() => subsystem.Shutdown());
            Assert.Null(ex);
        }

        [Fact]
        public void Shutdown_WithoutInit_DoesNotThrow()
        {
            var subsystem = new IgSubsystem();
            // Shutdown without Initialize — must not crash (null-safe delegation).
            var ex = Record.Exception(() => subsystem.Shutdown());
            Assert.Null(ex);
        }

        // ── ISubsystem contract ───────────────────────────────────────────────

        [Fact]
        public void FullLifecycle_Headless_CompletesCleanly()
        {
            ISubsystem subsystem = new IgSubsystem();

            subsystem.Initialize(HeadlessConfig());
            subsystem.Update(0.016f);
            subsystem.DrawWorld();
            subsystem.DrawUI();
            subsystem.Update(0.016f);
            subsystem.Shutdown();
            // No exception = contract fulfilled.
        }

        // ── Helper: wrapper to ensure cleanup on test failure ─────────────────

        private sealed class ResourceTrackedIgSubsystem : IDisposable
        {
            public IgSubsystem Inner { get; } = new IgSubsystem();
            public void Dispose() => Inner.Shutdown();
        }
    }
}
