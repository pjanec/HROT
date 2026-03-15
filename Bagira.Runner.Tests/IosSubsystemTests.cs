using System;
using Xunit;
using Bagira.Runner.Services;

namespace Bagira.Runner.Tests
{
    /// <summary>
    /// Tests for the embedded <see cref="IosSubsystem"/> implementation.
    ///
    /// All tests run without a Raylib window (headless mode). The IOS
    /// subsystem uses <c>NullDdsWriter</c> internally, so DDS is not required.
    /// </summary>
    public class IosSubsystemTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        private static SubsystemConfig HeadlessConfig() => new()
        {
            DomainId      = 0,
            Headless      = true,
            OwnWindow     = false,
            SubsystemName = "IOS"
        };

        // ── Name ──────────────────────────────────────────────────────────────

        [Fact]
        public void Name_Returns_IOS()
        {
            var subsystem = new IosSubsystem();
            Assert.Equal("IOS", subsystem.Name);
        }

        // ── Initialize ────────────────────────────────────────────────────────

        [Fact]
        public void Initialize_DoesNotThrow()
        {
            var subsystem = new IosSubsystem();
            var ex = Record.Exception(() => subsystem.Initialize(HeadlessConfig()));
            Assert.Null(ex);
            subsystem.Shutdown();
        }

        [Fact]
        public void Initialize_CreatesInternalMock()
        {
            // Verified indirectly: Update and DrawUI run without NullReferenceException
            // after Initialize — proving IosMock was created successfully.
            var subsystem = new IosSubsystem();
            subsystem.Initialize(HeadlessConfig());
            subsystem.Update(0.016f);   // would NPE if _mock is null
            subsystem.Shutdown();
        }

        // ── Update ────────────────────────────────────────────────────────────

        [Fact]
        public void Update_AfterInit_DoesNotThrow()
        {
            var subsystem = new IosSubsystem();
            subsystem.Initialize(HeadlessConfig());
            var ex = Record.Exception(() => subsystem.Update(0.016f));
            Assert.Null(ex);
            subsystem.Shutdown();
        }

        [Fact]
        public void Update_MultipleFrames_Succeeds()
        {
            var subsystem = new IosSubsystem();
            subsystem.Initialize(HeadlessConfig());

            for (int i = 0; i < 10; i++)
                subsystem.Update(0.016f);

            subsystem.Shutdown();
        }

        // ── DrawWorld ─────────────────────────────────────────────────────────

        [Fact]
        public void DrawWorld_IsAlwaysNoOp()
        {
            var subsystem = new IosSubsystem();
            subsystem.Initialize(HeadlessConfig());
            // IOS has no world rendering; DrawWorld must be a no-op in all modes.
            subsystem.DrawWorld();
            subsystem.Shutdown();
        }

        // ── DrawUI ────────────────────────────────────────────────────────────

        [Fact]
        public void DrawUI_Headless_SkipsImGui()
        {
            var subsystem = new IosSubsystem();
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
            var subsystem = new IosSubsystem();
            subsystem.Initialize(HeadlessConfig());
            var ex = Record.Exception(() => subsystem.Shutdown());
            Assert.Null(ex);
        }

        [Fact]
        public void Shutdown_WithoutInit_IsNoOp()
        {
            var subsystem = new IosSubsystem();
            // Must not throw when Shutdown is called before Initialize.
            var ex = Record.Exception(() => subsystem.Shutdown());
            Assert.Null(ex);
        }

        // ── Full lifecycle ────────────────────────────────────────────────────

        [Fact]
        public void FullLifecycle_Headless_CompletesCleanly()
        {
            ISubsystem subsystem = new IosSubsystem();

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
            var subsystem = new IosSubsystem();
            var ex = Record.Exception(() => subsystem.Update(0.016f));
            Assert.Null(ex);
        }
    }
}
