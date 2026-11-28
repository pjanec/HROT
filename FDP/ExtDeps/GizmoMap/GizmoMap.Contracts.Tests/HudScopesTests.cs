using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Xunit;

namespace GizmoMap.Contracts.Tests
{
    /// <summary>
    /// ⭐⭐ Simulated devices S1d-1 (docs/designs/devices/DESIGN_Simulated_Devices.md §3.6 D-41 / R3-4 / R5-4) — THE one HUD-run reader: a view bound
    /// to a source keeps that source's run and nobody else's; a reader bound to nothing keeps no run; a frame with no scope comes back as it is;
    /// a run longer than the frame is clamped; the 3-D triage fed a raw frame skips the whole run; 0 B steady.
    /// </summary>
    public class HudScopesTests
    {
        private const long DeviceA = 1001, DeviceB = 1002;

        private static DebugPrimitive Line(float x) => DebugPrimitive.MakeLine(new Vector3(x, 0f, 0f), new Vector3(x, 1f, 0f), new Rgba32(255, 0, 0, 255));

        /// <summary>world line 0 · A's run (lines 10, 11) · world line 1 · B's run (line 20) · world line 2.</summary>
        private static DebugPrimitive[] Frame() => new[]
        {
            Line(0f), HudScopes.Make(DeviceA, 2), Line(10f), Line(11f), Line(1f), HudScopes.Make(DeviceB, 1), Line(20f), Line(2f),
        };

        private static float[] Xs(ReadOnlySpan<DebugPrimitive> f)
        {
            var xs = new float[f.Length];
            for (int i = 0; i < f.Length; i++) xs[i] = f[i].Shape == DebugPrimitiveShape.HudScope ? -1f : f[i].LineStart.X;
            return xs;
        }

        [Fact]
        public void S1d1_AViewBoundToASource_KeepsItsRunAndNoOtherRun_WithoutTheMarkers()
        {
            var scratch = Array.Empty<DebugPrimitive>();
            Assert.Equal(new[] { 0f, 10f, 11f, 1f, 2f }, Xs(HudScopes.Keep(Frame(), DeviceA, ref scratch)));
            Assert.Equal(new[] { 0f, 1f, 20f, 2f }, Xs(HudScopes.Keep(Frame(), DeviceB, ref scratch)));
        }

        [Fact]
        public void S1d1_AReaderBoundToNothing_KeepsNoRun()
        {
            var scratch = Array.Empty<DebugPrimitive>();
            Assert.Equal(new[] { 0f, 1f, 2f }, Xs(HudScopes.Unscoped(Frame(), ref scratch)));
            Assert.Equal(new[] { 0f, 1f, 2f }, Xs(HudScopes.Keep(Frame(), 9999, ref scratch)));   // bound to a source with no run
        }

        [Fact]
        public void S1d1_AFrameWithNoScope_IsReturnedAsItIs_AndARunPastTheEndIsClamped()
        {
            var plain = new[] { Line(0f), Line(1f) };
            var scratch = Array.Empty<DebugPrimitive>();
            var kept = HudScopes.Keep(plain, DeviceA, ref scratch);
            Assert.Equal(0, scratch.Length);   // no copy made
            Assert.Equal(2, kept.Length);
            var ragged = new[] { Line(0f), HudScopes.Make(DeviceA, 50), Line(10f) };
            Assert.Equal(3, HudScopes.After(ragged, 1));
            Assert.Equal(new[] { 0f }, Xs(HudScopes.Unscoped(ragged, ref scratch)));
        }

        [Fact]
        public void S1d1_TheTriageOfARawFrame_SkipsTheWholeRun()
        {
            var triage = new DebugPrimitiveTriage3D();
            var into = new List<TriagedPrimitive3D>();
            int kept = triage.Triage(Frame(), into);
            Assert.Equal(3, kept);
            Assert.Equal(2, triage.CulledByScope);
            foreach (var t in into) Assert.True(t.Primitive.LineStart.X < 10f, $"a HUD line was triaged: {t.Primitive.LineStart.X}");
        }

        [Fact]
        public void S1d1_TheFilter_AllocatesNothingSteady()
        {
            var frame = Frame();
            var scratch = Array.Empty<DebugPrimitive>();
            for (int i = 0; i < 100; i++) { HudScopes.Keep(frame, DeviceA, ref scratch); HudScopes.Unscoped(frame, ref scratch); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            int n = 0;
            for (int i = 0; i < 1000; i++) n += HudScopes.Keep(frame, DeviceA, ref scratch).Length + HudScopes.Unscoped(frame, ref scratch).Length;
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(8000, n);
        }
    }
}
