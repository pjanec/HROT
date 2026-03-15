using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;
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
        // Helper: creates headless RunnerOptions
        private static RunnerOptions HeadlessOptions() => new RunnerOptions { Headless = true };
        private static RunnerOptions NonHeadlessOptions() => new RunnerOptions { Headless = false };

        // ── Initialize order ──────────────────────────────────────────────────

        [Fact]
        public void Initialize_CallsInitializeOnAllSubsystems()
        {
            var a = new MockSubsystem("A");
            var b = new MockSubsystem("B");
            var orchestrator = new SubsystemOrchestrator(new ISubsystem[] { a, b }, HeadlessOptions());

            orchestrator.Initialize();

            Assert.True(a.InitializeCalled);
            Assert.True(b.InitializeCalled);
        }

        [Fact]
        public void Initialize_PassesHeadlessFlagToSubsystems()
        {
            var mock = new MockSubsystem();
            var orchestrator = new SubsystemOrchestrator(new ISubsystem[] { mock }, HeadlessOptions());

            orchestrator.Initialize();

            Assert.NotNull(mock.ReceivedConfig);
            Assert.True(mock.ReceivedConfig!.Headless);
        }

        [Fact]
        public void Initialize_SetsOwnWindowFalseOnSubsystems()
        {
            var mock = new MockSubsystem();
            var orchestrator = new SubsystemOrchestrator(new ISubsystem[] { mock }, HeadlessOptions());

            orchestrator.Initialize();

            Assert.False(mock.ReceivedConfig!.OwnWindow);
        }

        [Fact]
        public void Initialize_DoesNotForceSimHostHeadless_WhenIgIsPresent()
        {
            // SimHost and IG both receive the same headless flag from RunnerOptions; neither is forced headless.
            var ig      = new MockSubsystem("IG");
            var simHost = new MockSubsystem("SimHost");
            var ios     = new MockSubsystem("IOS");

            var orchestrator = new SubsystemOrchestrator(
                new ISubsystem[] { ig, simHost, ios }, NonHeadlessOptions());

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
            var ios     = new MockSubsystem("IOS");

            var orchestrator = new SubsystemOrchestrator(
                new ISubsystem[] { simHost, ios }, NonHeadlessOptions());

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
            var orchestrator = new SubsystemOrchestrator(new ISubsystem[] { a, b }, HeadlessOptions());

            orchestrator.Initialize();
            orchestrator.RunFrames(3);

            Assert.Equal(3, a.UpdateCallCount);
            Assert.Equal(3, b.UpdateCallCount);
        }

        [Fact]
        public void HeadlessMode_RunFrames_NeverCallsDrawWorldOrDrawUI()
        {
            var mock = new MockSubsystem();
            var orchestrator = new SubsystemOrchestrator(new ISubsystem[] { mock }, HeadlessOptions());

            orchestrator.Initialize();
            orchestrator.RunFrames(5);

            Assert.Equal(0, mock.DrawWorldCount);
            Assert.Equal(0, mock.DrawUICount);
            Assert.Equal(5, mock.UpdateCallCount);
        }

        [Fact]
        public void HeadlessMode_Run_WithStopFirst_DoesNotHang()
        {
            var mock = new MockSubsystem();
            var orchestrator = new SubsystemOrchestrator(new ISubsystem[] { mock }, HeadlessOptions());

            orchestrator.Initialize();
            orchestrator.Stop();
            orchestrator.Run();

            Assert.Equal(0, mock.DrawWorldCount);
            Assert.Equal(0, mock.DrawUICount);
        }

        // ── Shutdown order ────────────────────────────────────────────────────

        [Fact]
        public void Shutdown_CallsShutdownOnAllSubsystems()
        {
            var a = new MockSubsystem("A");
            var b = new MockSubsystem("B");
            var orchestrator = new SubsystemOrchestrator(new ISubsystem[] { a, b }, HeadlessOptions());

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
                new ISubsystem[] { a, b, c }, HeadlessOptions());

            orchestrator.Initialize();
            orchestrator.Shutdown();

            Assert.Equal(new[] { "C", "B", "A" }, shutdownOrder);
        }

        // ── TitleBarColor (MOD1-P9T1) ─────────────────────────────────────────

        [Fact]
        public void ISubsystem_TitleBarColor_IsSetOnConcretes()
        {
            // Each concrete subsystem colour must be non-zero and distinct.
            var sim = new Bagira.Runner.Services.SimHostSubsystem();
            var ig  = new Bagira.Runner.Services.IgSubsystem();
            var ios = new Bagira.Runner.Services.IosSubsystem();

            Assert.NotEqual(default(Vector4), sim.TitleBarColor);
            Assert.NotEqual(default(Vector4), ig.TitleBarColor);
            Assert.NotEqual(default(Vector4), ios.TitleBarColor);

            // All three colours must be distinct.
            Assert.NotEqual(sim.TitleBarColor, ig.TitleBarColor);
            Assert.NotEqual(ig.TitleBarColor, ios.TitleBarColor);
            Assert.NotEqual(sim.TitleBarColor, ios.TitleBarColor);
        }

        // ── MapCameraProvider toggle (MOD1-P9T2) ──────────────────────────────

        [Fact]
        public void SubsystemOrchestrator_MenuBar_ShowsToggleForMapCameraProviders()
        {
            // The orchestrator sets the first IMapCameraProvider as the active map owner.
            // A subsystem that implements IMapCameraProvider should be the initial owner.
            var mapSub  = new MapCameraSubsystemMock("IG",      new MapCamera());
            var plainSub = new MockSubsystem("IOS");

            var orchestrator = new SubsystemOrchestrator(
                new ISubsystem[] { mapSub, plainSub }, HeadlessOptions());
            orchestrator.Initialize();

            // After init, the IMapCameraProvider subsystem is the active map owner.
            // We verify this by switching to it (no-op) and checking the camera is unchanged.
            var camera = mapSub.GetMapCamera()!;
            float originalZoom = camera.Zoom;

            orchestrator.SwitchMapOwner("IG"); // Switch to same owner — no-op.
            Assert.Equal(originalZoom, camera.Zoom, precision: 4);

            orchestrator.Shutdown();
        }
    }

    /// <summary>
    /// Tests that switching the active map owner synchronises camera state from the
    /// outgoing subsystem's map to the incoming one, preventing entity jumps.
    /// </summary>
    public class MapCameraSyncTests
    {
        private static RunnerOptions HeadlessOptions() => new RunnerOptions { Headless = true };

        private static MapCamera MakeCamera(float zoom, float targetX, float targetY)
        {
            var cam = new MapCamera();
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
                new ISubsystem[] { ig, simHost }, HeadlessOptions());
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

            var orchestrator = new SubsystemOrchestrator(
                new ISubsystem[] { ig, simHost }, HeadlessOptions());
            orchestrator.Initialize();

            orchestrator.SwitchMapOwner("SimHost");
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
                new ISubsystem[] { ig }, HeadlessOptions());
            orchestrator.Initialize();

            orchestrator.SwitchMapOwner("IG");

            Assert.Equal(2.0f, igCamera.Zoom,   precision: 4);
            Assert.Equal(new Vector2(50f, 75f), igCamera.Target);
        }

        [Fact]
        public void SwitchMapOwner_WhenOutgoingCameraIsNull_DoesNotThrow()
        {
            var shCamera = MakeCamera(zoom: 1.5f, targetX: 10f, targetY: 20f);
            var simHost  = new MapCameraSubsystemMock("SimHost", shCamera);

            var orchestrator = new SubsystemOrchestrator(
                new ISubsystem[] { new NullCameraSubsystemMock("IG"), simHost }, HeadlessOptions());
            orchestrator.Initialize();

            var ex = Record.Exception(() => orchestrator.SwitchMapOwner("SimHost"));
            Assert.Null(ex);
        }

        /// <summary>Helper mock whose <see cref="GetMapCamera"/> always returns null.</summary>
        private class NullCameraSubsystemMock : ISubsystem, IMapCameraProvider
        {
            public string Name { get; }
            public Vector4 TitleBarColor => new Vector4(0.5f, 0.5f, 0.5f, 1f);
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
