using System;
using System.Collections.Generic;
using Xunit;
using Bagira.Runner.Abstractions;
using Bagira.Runner.Configuration;
using Bagira.Runner.Services;
using Bagira.Runner.Tests.Mocks;

namespace Bagira.Runner.Tests
{
    /// <summary>
    /// Unit tests for <see cref="SubsystemOrchestrator"/> lifecycle management.
    /// All tests run in headless mode to avoid Raylib window creation.
    /// </summary>
    public class SubsystemOrchestratorTests
    {
        // Helper: creates a headless RunnerConfiguration (already validated)
        private static RunnerConfiguration HeadlessAllConfig()
        {
            var c = new RunnerConfiguration
            {
                ModeString = "all",
                Headless   = true,
                NoWait     = true
            };
            c.Validate();
            return c;
        }

        // ── Initialize order ──────────────────────────────────────────────────

        [Fact]
        public void Initialize_CallsInitializeOnAllSubsystems()
        {
            var a = new MockSubsystem("A");
            var b = new MockSubsystem("B");
            var orchestrator = new SubsystemOrchestrator(HeadlessAllConfig(), new ISubsystem[] { a, b });

            orchestrator.Initialize();

            Assert.True(a.InitializeCalled);
            Assert.True(b.InitializeCalled);
        }

        [Fact]
        public void Initialize_PassesHeadlessFlagToSubsystems()
        {
            var mock = new MockSubsystem();
            var orchestrator = new SubsystemOrchestrator(HeadlessAllConfig(), new ISubsystem[] { mock });

            orchestrator.Initialize();

            Assert.NotNull(mock.ReceivedConfig);
            Assert.True(mock.ReceivedConfig!.Headless);
        }

        [Fact]
        public void Initialize_SetsOwnWindowFalseOnSubsystems()
        {
            var mock = new MockSubsystem();
            var orchestrator = new SubsystemOrchestrator(HeadlessAllConfig(), new ISubsystem[] { mock });

            orchestrator.Initialize();

            Assert.False(mock.ReceivedConfig!.OwnWindow);
        }

        // ── Update loop ───────────────────────────────────────────────────────

        [Fact]
        public void RunFrames_CallsUpdateOnAllSubsystems()
        {
            var a = new MockSubsystem("A");
            var b = new MockSubsystem("B");
            var orchestrator = new SubsystemOrchestrator(HeadlessAllConfig(), new ISubsystem[] { a, b });

            orchestrator.Initialize();
            orchestrator.RunFrames(3);

            Assert.Equal(3, a.UpdateCallCount);
            Assert.Equal(3, b.UpdateCallCount);
        }

        [Fact]
        public void HeadlessMode_RunFrames_NeverCallsDrawWorldOrDrawUI()
        {
            var mock = new MockSubsystem();
            var orchestrator = new SubsystemOrchestrator(HeadlessAllConfig(), new ISubsystem[] { mock });

            orchestrator.Initialize();
            orchestrator.RunFrames(5); // 5 update iterations, no render

            // DrawWorld / DrawUI must NOT be called via RunFrames (headless render skip)
            Assert.Equal(0, mock.DrawWorldCount);
            Assert.Equal(0, mock.DrawUICount);
            Assert.Equal(5, mock.UpdateCallCount);
        }

        [Fact]
        public void HeadlessMode_Run_WithStopFirst_DoesNotHang()
        {
            var mock = new MockSubsystem();
            var orchestrator = new SubsystemOrchestrator(HeadlessAllConfig(), new ISubsystem[] { mock });

            orchestrator.Initialize();
            orchestrator.Stop(); // Pre-stop before Run
            orchestrator.Run();  // Should exit immediately without looping forever

            Assert.Equal(0, mock.DrawWorldCount);
            Assert.Equal(0, mock.DrawUICount);
        }

        // ── Shutdown order ────────────────────────────────────────────────────

        [Fact]
        public void Shutdown_CallsShutdownOnAllSubsystems()
        {
            var a = new MockSubsystem("A");
            var b = new MockSubsystem("B");
            var orchestrator = new SubsystemOrchestrator(HeadlessAllConfig(), new ISubsystem[] { a, b });

            orchestrator.Initialize();
            orchestrator.Shutdown();

            Assert.True(a.ShutdownCalled);
            Assert.True(b.ShutdownCalled);
        }

        [Fact]
        public void Shutdown_CallsSubsystemsInReverseOrder()
        {
            var shutdownOrder = new List<string>();
            var a = new MockSubsystem("A", m => shutdownOrder.Add(m.Name));
            var b = new MockSubsystem("B", m => shutdownOrder.Add(m.Name));
            var c = new MockSubsystem("C", m => shutdownOrder.Add(m.Name));
            var orchestrator = new SubsystemOrchestrator(
                HeadlessAllConfig(), new ISubsystem[] { a, b, c });

            orchestrator.Initialize();
            orchestrator.Shutdown();

            Assert.Equal(new[] { "C", "B", "A" }, shutdownOrder);
        }
    }
}
