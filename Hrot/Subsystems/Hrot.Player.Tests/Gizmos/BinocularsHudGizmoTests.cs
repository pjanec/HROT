using System;
using System.Collections.Generic;
using System.Reflection;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.Replication.Components;
using Hrot.AI.Behaviors.Devices.Binoculars;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.Player.Tests.Gizmos
{
    /// <summary>
    /// ⭐⭐ Simulated devices S1d-3 (docs/designs/devices/DESIGN_Simulated_Devices.md §3.10 D-42 / D-75, §3.6 R6-5 / R7-3, mockup <c>devices-hud.svg</c>
    /// panel 1) — the BINOCULARS' HUD projector through the production path (<see cref="StatelessGizmoRegistry"/> + <see cref="StatelessGizmoSystem"/>, as
    /// <c>RangefinderHudGizmoTests</c>): nothing un-operated, without the brain's authority or for another device type; per zoom step the step lit
    /// (gold, larger) and the reticle's 10-mil tick at the step's field of view (the scale follows the zoom); the eyepiece frame; one HudScope run keyed
    /// by the network id; 0 B per steady frame.
    /// </summary>
    public sealed class BinocularsHudGizmoTests : IDisposable
    {
        private const int NetId = 4343;
        private readonly EntityRepository _w = new();
        private readonly DebugPrimitiveBuffer _buf = new(4096);
        private readonly StatelessGizmoSystem _sys;
        private readonly Entity _d;

        public BinocularsHudGizmoTests()
        {
            _w.RegisterComponent<BehaviorState>();
            _w.RegisterComponent<DeviceInfo>();
            _w.RegisterComponent<PossessedBy>();
            _w.RegisterComponent<NetworkIdentity>();
            BlueprintTierTable.RegisterAll(_w);
            _w.SetSingleton(new GlobalTime());
            var registry = new StatelessGizmoRegistry();
            registry.Register(new BinocularsHudGizmo(), typeof(BinocularsHudGizmo).GetCustomAttribute<GizmoProjectorAttribute>()!.RequiredComponents);
            _sys = new StatelessGizmoSystem(registry, _buf);
            _d = _w.CreateEntity();
            _w.AddComponent(_d, new BehaviorState());
            _w.SetAuthority<BehaviorState>(_d, true);
            _w.AddComponent(_d, new DeviceInfo { DeviceType = 78, Behavior = new FixedString64(BehaviorNames.DeviceBinoculars) });
            _w.AddComponent(_d, new NetworkIdentity { Value = NetId });
            _w.AddComponent(_d, new PossessedBy { StationNodeId = (StationId)5, Mode = PossessionMode.Operated, Epoch = 1 });
        }

        public void Dispose() => _w.Dispose();

        private void Step(byte zoom) => UnitMemory.Ref<BinocularsState>(_w, _d) = new BinocularsState { Zoom = zoom, Ready = 1 };

        private DebugPrimitive[] Frame()
        {
            _buf.EndFrame(0f);
            _sys.Execute(_w, 0f);
            var frame = _buf.GetFrame();
            for (int i = 0; i < frame.Length; i++)
                if (HudScopes.IsScope(in frame[i]))
                {
                    Assert.Equal(NetId, frame[i].HudScopeSource);
                    return frame.Slice(i + 1, frame[i].HudScopeCount).ToArray();
                }
            Assert.Equal(0, frame.Length);
            return Array.Empty<DebugPrimitive>();
        }

        private static DebugPrimitive TextOf(DebugPrimitive[] run, string text)
        {
            foreach (var p in run) if (p.Shape == DebugPrimitiveShape.Text && p.TextContent.ToString() == text) return p;
            throw new Xunit.Sdk.XunitException($"no text '{text}' in the HUD run");
        }

        [Fact]
        public void S1d3_TheRule_IsDeviceInfoAndPossessedBy_AHudOnTheDeviceHudLayer_NothingUnOperatedNorWithoutAuthorityNorAnotherType()
        {
            var attr = typeof(BinocularsHudGizmo).GetCustomAttribute<GizmoProjectorAttribute>()!;
            Assert.Equal(new[] { typeof(DeviceInfo), typeof(PossessedBy) }, attr.RequiredComponents);
            Assert.True(attr.Hud);
            var run = Frame();
            Assert.NotEmpty(run);
            foreach (var p in run) { Assert.Equal(DebugTraceLayers.DeviceHud, p.DebugLayer); Assert.Equal(CoordinateSpace.ViewCanvas, p.Space); }
            _w.SetAuthority<BehaviorState>(_d, false);
            Assert.Empty(Frame());                                                     // a ghost of the device
            _w.SetAuthority<BehaviorState>(_d, true);
            _w.SetComponent(_d, new DeviceInfo { DeviceType = 77, Behavior = new FixedString64(BehaviorNames.DeviceRangefinder) });
            Assert.Empty(Frame());                                                     // the rangefinder: not this HUD
            _w.SetComponent(_d, new DeviceInfo { DeviceType = 78, Behavior = new FixedString64(BehaviorNames.DeviceBinoculars) });
            _w.RemoveComponent<PossessedBy>(_d);
            Assert.Empty(Frame());                                                     // un-operated
        }

        [Theory]
        [InlineData(0, "5×")]
        [InlineData(1, "7×")]
        [InlineData(2, "10×")]
        public void S1d3_EachZoomStep_IsLit_AndTheReticlesTenMilTick_SitsAtThatStepsFieldOfView(byte zoom, string lit)
        {
            Step(zoom);
            var run = Frame();
            TextOf(run, "BINOCULARS");
            foreach (var s in new[] { "5×", "7×", "10×" })
            {
                var t = TextOf(run, s);
                Assert.Equal(s == lit ? BinocularsHudGizmo.Gold : BinocularsHudGizmo.StepOff, t.Color);   // the current step lit, the others dim
                Assert.Equal(ViewCanvasAlign.Right, ViewCanvas.AlignOf(in t));
            }
            // the "10" under the centre sits 10 mil below it at THIS step's field of view (the mockup: 1 mil = 1.48 px at 10°)
            float fov = BinocularsZoom.FovDegrees(zoom);
            var ten = TextOf(run, "10");
            float expectedY = (259f / 2f + 10f * BinocularsHudGizmo.PxPerMil(fov) + 3f) * BinocularsHudGizmo.K;
            Assert.InRange(ten.TextY, expectedY - 0.5f, expectedY + 0.5f);
            Assert.Equal(1.4842f, BinocularsHudGizmo.PxPerMil(10f), 3);
        }

        [Fact]
        public void S1d3_TheFrame_DarkensOutsideTwoEyepieces_AndAFreshStateShowsTheDefaultStep()
        {
            UnitMemory.Ref<BinocularsState>(_w, _d) = default;                         // never ticked: the default step, not 5×
            var run = Frame();
            Assert.Equal(BinocularsHudGizmo.Gold, TextOf(run, "7×").Color);
            int triangles = 0;
            foreach (var p in run) if (p.Shape == DebugPrimitiveShape.FilledTriangle) triangles++;
            Assert.Equal(4 * BinocularsHudGizmo.MaskStrips, triangles);                 // the dark area above and below the eyepieces, per strip
        }

        [Fact]
        public void S1d3_ASteadyFrame_AllocatesNothing()
        {
            Step(1);
            for (int i = 0; i < 20; i++) Frame();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) { _buf.EndFrame(0f); _sys.Execute(_w, 0f); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
