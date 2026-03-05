using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;
using Bagira.Runner.Abstractions;
using Bagira.Runner.Configuration;
using Bagira.Runner.Models;
using Bagira.Runner.Services;
using Bagira.Runner.Tests.Mocks;
using FDP.Toolkit.Vis2D.Components;

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

        private static RunnerConfiguration NonHeadlessAllConfig()
        {
            var c = new RunnerConfiguration
            {
                ModeString = "all",
                Headless   = false,
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

        [Fact]
        public void Initialize_DoesNotForceSimHostHeadless_WhenIgIsPresent()
        {
            // Task 15: SimHost is no longer forced headless when IG is present.
            // Both can own their own map view; the active map owner is toggled at runtime.
            var ig = new MockSubsystem("IG");
            var simHost = new MockSubsystem("SimHost");
            var ios = new MockSubsystem("IOS");

            var orchestrator = new SubsystemOrchestrator(
                NonHeadlessAllConfig(),
                new ISubsystem[] { ig, simHost, ios });

            orchestrator.Initialize();

            Assert.NotNull(ig.ReceivedConfig);
            Assert.NotNull(simHost.ReceivedConfig);
            Assert.False(ig.ReceivedConfig!.Headless);
            Assert.False(simHost.ReceivedConfig!.Headless);
        }

        [Fact]
        public void Initialize_DoesNotForceSimHostHeadless_WhenIgIsAbsent()
        {
            var simHost = new MockSubsystem("SimHost");
            var ios = new MockSubsystem("IOS");

            var orchestrator = new SubsystemOrchestrator(
                NonHeadlessAllConfig(),
                new ISubsystem[] { simHost, ios });

            orchestrator.Initialize();

            Assert.NotNull(simHost.ReceivedConfig);
            Assert.False(simHost.ReceivedConfig!.Headless);
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

    /// <summary>
    /// Tests that switching the active map owner synchronises camera state from the
    /// outgoing subsystem's map to the incoming one, preventing entity jumps.
    /// </summary>
    public class MapCameraSyncTests
    {
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

        private static MapCamera MakeCamera(float zoom, float targetX, float targetY)
        {
            var cam = new MapCamera();
            // Use SnapTo from a source to set up all state consistently.
            var src = new MapCamera();
            src.InnerCamera.Zoom   = zoom;
            src.InnerCamera.Target = new Vector2(targetX, targetY);
            cam.SnapTo(src);
            return cam;
        }

        [Fact]
        public void SwitchMapOwner_FromIgToSimHost_SnapsSimHostCameraToIgState()
        {
            var igCamera  = MakeCamera(zoom: 2.5f, targetX: 100f, targetY: 200f);
            var shCamera  = MakeCamera(zoom: 1.0f, targetX: 0f,   targetY: 0f);

            var ig      = new MapCameraSubsystemMock("IG",      igCamera);
            var simHost = new MapCameraSubsystemMock("SimHost", shCamera);

            var orchestrator = new SubsystemOrchestrator(
                HeadlessAllConfig(), new ISubsystem[] { ig, simHost });
            orchestrator.Initialize();

            // IG is the default active owner; switch to SimHost.
            orchestrator.SwitchMapOwner("SimHost");

            Assert.Equal(igCamera.Zoom,   shCamera.Zoom,   precision: 4);
            Assert.Equal(igCamera.Target, shCamera.Target);
        }

        [Fact]
        public void SwitchMapOwner_FromSimHostToIg_SnapsIgCameraToSimHostState()
        {
            var igCamera  = MakeCamera(zoom: 1.0f, targetX: 0f,   targetY: 0f);
            var shCamera  = MakeCamera(zoom: 3.0f, targetX: 500f, targetY: -200f);

            var ig      = new MapCameraSubsystemMock("IG",      igCamera);
            var simHost = new MapCameraSubsystemMock("SimHost", shCamera);

            // Build an orchestrator where SimHost is initial owner by passing SimHost first
            // and only SimHost+IOS — then add IG to get it initialized.  Easier: just switch
            // initial owner explicitly.
            var orchestrator = new SubsystemOrchestrator(
                HeadlessAllConfig(), new ISubsystem[] { ig, simHost });
            orchestrator.Initialize();

            // Switch to SimHost first (camera sync IG → SimHost already tested elsewhere).
            orchestrator.SwitchMapOwner("SimHost");
            // Now switch back: SimHost → IG
            orchestrator.SwitchMapOwner("IG");

            Assert.Equal(shCamera.Zoom,   igCamera.Zoom,   precision: 4);
            Assert.Equal(shCamera.Target, igCamera.Target);
        }

        [Fact]
        public void SwitchMapOwner_SameOwner_DoesNotChangeCameraState()
        {
            var igCamera = MakeCamera(zoom: 2.0f, targetX: 50f, targetY: 75f);
            var ig       = new MapCameraSubsystemMock("IG", igCamera);

            var orchestrator = new SubsystemOrchestrator(
                HeadlessAllConfig(), new ISubsystem[] { ig });
            orchestrator.Initialize();

            // Switch to the same owner — no-op, camera should be untouched.
            orchestrator.SwitchMapOwner("IG");

            Assert.Equal(2.0f, igCamera.Zoom,   precision: 4);
            Assert.Equal(new Vector2(50f, 75f), igCamera.Target);
        }

        [Fact]
        public void SwitchMapOwner_WhenOutgoingCameraIsNull_DoesNotThrow()
        {
            // A subsystem that reports a null camera (e.g. not yet initialised / headless).
            var shCamera = MakeCamera(zoom: 1.5f, targetX: 10f, targetY: 20f);
            var simHost  = new MapCameraSubsystemMock("SimHost", shCamera);

            var orchestrator = new SubsystemOrchestrator(
                HeadlessAllConfig(), new ISubsystem[] { new NullCameraSubsystemMock("IG"), simHost });
            orchestrator.Initialize();

            // Should not throw even though IG camera is null.
            var ex = Record.Exception(() => orchestrator.SwitchMapOwner("SimHost"));
            Assert.Null(ex);
        }

        /// <summary>Helper mock whose <see cref="GetMapCamera"/> always returns null.</summary>
        private class NullCameraSubsystemMock : ISubsystem, IMapCameraProvider
        {
            public string Name { get; }
            public NullCameraSubsystemMock(string name) => Name = name;
            public void Initialize(SubsystemConfig config) { }
            public void Update(float dt) { }
            public void DrawWorld() { }
            public void DrawUI() { }
            public void Shutdown() { }
            public MapCamera? GetMapCamera() => null;
        }
    }
}
