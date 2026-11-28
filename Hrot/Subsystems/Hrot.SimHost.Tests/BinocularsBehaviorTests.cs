using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Hrot.AI.Behaviors.Devices.Binoculars;
using Hrot.CGF.Configuration;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ Simulated devices S1d-3 (docs/designs/devices/DESIGN_Simulated_Devices.md §3.10 D-42 / D-75, §3.1 D-48 / R5-13, §3.5 R-4300) — THE BINOCULARS'
    /// logic: a press on ZOOM IN / OUT steps the zoom (clamped), re-reading a frame never presses twice, a new operator epoch re-bases the counters; its
    /// own <see cref="DevicePose"/> takes the step's field of view and the frame's look; end to end through the production registry and the device tick,
    /// in pause (dt = 0), and 0 B per steady frame. 🔴 Red-proof (compiles): <c>DeviceLooks.Follow</c> given a constant field ⇒ the FOV rail fails.
    /// </summary>
    public sealed class BinocularsBehaviorTests
    {
        private sealed class Node : IDisposable
        {
            public readonly EntityRepository W = new();
            public readonly Entity D;
            public BinocularsBehavior.BinocularsParams P;
            public BinocularsBehavior.BinocularsWork Ws;

            public Node()
            {
                SimHostComponentRegistry.RegisterAll(W);
                CognitiveComponentRegistry.RegisterAll(W);
                BlueprintTierTable.RegisterAll(W);
                D = W.CreateEntity();
                W.AddComponent(D, new BehaviorState());
                W.AddComponent(D, new DeviceInfo { DeviceType = 78, Behavior = new FixedString64(BehaviorNames.DeviceBinoculars) });
            }

            public BinocularsState State => UnitMemory.Get<BinocularsState>(W, D);
            public DevicePose Pose => W.HasComponent<DevicePose>(D) ? W.GetComponentRO<DevicePose>(D) : default;

            public void Input(byte zoomIn = 0, byte zoomOut = 0, Quaternion? look = null)
            {
                var c = new DeviceControl();
                c.Presses[BinocularsChannels.ZoomIn] = zoomIn; c.Presses[BinocularsChannels.ZoomOut] = zoomOut;
                if (look is { } q) { c.Orientation = q; c.Trackers = DeviceControl.OrientationPresent; }
                var input = new DeviceInput { Control = c };
                if (W.HasComponent<DeviceInput>(D)) W.SetComponent(D, input); else W.AddComponent(D, input);
            }

            public void Operated(uint epoch)
            {
                var row = new PossessedBy { StationNodeId = (StationId)5, Mode = PossessionMode.Operated, Epoch = epoch };
                if (W.HasComponent<PossessedBy>(D)) W.SetComponent(D, row); else W.AddComponent(D, row);
            }

            public void Tick() => Assert.Equal(Fbt.NodeStatus.Running, BinocularsBehavior.Tick(ref P, ref Ws, D, W));
            public void Dispose() => W.Dispose();
        }

        [Fact]
        public void S1d3_ZoomPresses_StepTheZoom_Clamped_AndThePoseTakesTheStepsFieldOfView()
        {
            using var n = new Node();
            n.Operated(1);
            n.Input();
            n.Tick();                                                                   // based; the default step
            Assert.Equal(BinocularsZoom.DefaultStep, n.State.Zoom);
            Assert.Equal(DeviceOptics.FovDegrees(7f), n.Pose.FovDegrees, 4);            // 7× ⇒ 50° / 7
            n.Input(zoomIn: 1);
            n.Tick();
            Assert.Equal(2, n.State.Zoom);
            Assert.Equal(5f, n.Pose.FovDegrees, 4);                                     // 10× ⇒ 5°
            n.Input(zoomIn: 3);                                                         // two more presses at the top: clamped
            n.Tick();
            Assert.Equal(2, n.State.Zoom);
            for (int i = 0; i < 3; i++) n.Tick();                                       // the same frame re-read: no press
            Assert.Equal(2, n.State.Zoom);
            n.Input(zoomIn: 3, zoomOut: 2);
            n.Tick();
            Assert.Equal(0, n.State.Zoom);                                              // two steps down
            Assert.Equal(10f, n.Pose.FovDegrees, 4);                                    // 5× ⇒ 10° — CE-1219's old view
            n.Operated(2);                                                              // another operator: the counters re-base
            n.Input(zoomIn: 9, zoomOut: 2);
            n.Tick();
            Assert.Equal(0, n.State.Zoom);
        }

        [Fact]
        public void S1d3_ThePoseFollowsTheFramesLook_RelativeToItsMount_AndIdentityWithoutOne()
        {
            using var n = new Node();
            n.Input();
            n.Tick();
            Assert.Equal(Quaternion.Identity, n.Pose.Orientation);                      // no look: it points as its mount
            var left = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 4f) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.1f);
            n.Input(look: left);
            n.Tick();
            var fwd = Vector3.Transform(Vector3.UnitX, n.Pose.Orientation);
            Assert.Equal(Vector3.Transform(Vector3.UnitX, left).X, fwd.X, 4);
            Assert.True(fwd.Y > 0.6f && fwd.Z > 0.05f, "45° left and a little up");
        }

        // ── end to end: the production registry and the device tick (paused) ────────────────────────────────────────────────────────────

        [Fact]
        public void S1d3_EndToEnd_InPause_TheDeviceTickStartsTheBinoculars_AZoomPressSteps_AndASteadyFrameAllocatesNothing()
        {
            using var w = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(w);
            CognitiveComponentRegistry.RegisterAll(w);
            BlueprintTierTable.RegisterAll(w);
            var registry = new BehaviorRegistry();
            CgfBehaviorSetup.LoadFromAiAssembly(registry);
            var ingress = new BehaviorIngressSystem(registry);
            var devices = new DeviceTickSystem(registry, ingress);
            var d = w.CreateEntity();
            w.AddComponent(d, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            w.AddComponent(d, new BehaviorState());
            w.AddComponent(d, new DeviceInfo { DeviceType = 78, Behavior = new FixedString64(BehaviorNames.DeviceBinoculars) });
            void Press(byte zoomIn)
            {
                var c = new DeviceControl();
                c.Presses[BinocularsChannels.ZoomIn] = zoomIn;
                var input = new DeviceInput { Control = c };
                if (w.HasComponent<DeviceInput>(d)) w.SetComponent(d, input); else w.AddComponent(d, input);
            }
            void Frame() { w.Bus.SwapBuffers(); devices.Execute(w, 0f); }
            Press(0);
            for (int i = 0; i < 3; i++) Frame();
            Assert.Equal(1, devices.Starts);
            Assert.Equal(BinocularsZoom.DefaultStep, UnitMemory.Get<BinocularsState>(w, d).Zoom);
            Press(1);
            Frame();
            Assert.Equal(2, UnitMemory.Get<BinocularsState>(w, d).Zoom);                 // paused: the zoom still steps (its own state, R-4300)
            Assert.Equal(5f, w.GetComponentRO<DevicePose>(d).FovDegrees, 4);
            for (int i = 0; i < 30; i++) Frame();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) Frame();
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
