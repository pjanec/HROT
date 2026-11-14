using Fdp.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Hrot.ClusterRunner.Presentation;

/// <summary>
/// ⭐ CE-1072 (docs/DESIGN_Map_3D_Mode.md §6l) — an opt-in frame-time log for the windowed loop: <c>HROT_FRAME_STATS=1</c> prints,
/// every two seconds, the frame count and the frame time's mean / p50 / p95 / max, and how much of the mean went to the
/// update (input + simulation), the world draw and the UI. ⭐ It turns "the camera is not smooth" into numbers: a camera that
/// moves per frame can only be as smooth as the frame pacing. Off: no cost (null).
/// </summary>
internal sealed class FrameStats
{
    private const double WindowSeconds = 2.0;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Stopwatch _part = new();
    private readonly List<double> _frames = new();
    private double _update, _draw, _ui, _windowStart, _lastFrame;
    // ⭐ CE-2401 (R-2084) — the garbage of each part, beside its time (the main thread's allocation counter)
    private long _partBytes, _updateBytes, _drawBytes, _uiBytes;
    private int _maxVtx; private string _maxVtxOwner = "";
    private double _renderMs;   // ⭐ CE-1495 — the ImGui draw-data render step (Fdp.Presentation.Raylib.ImGuiRenderer), summed over the window

    public static FrameStats? FromEnvironment()
    {
        if (EnvVars.Read(EnvVars.HrotFrameStats) != "1") return null;
        Fdp.Presentation.WindowManager.WindowTimings.Enabled = true;   // ⭐ W-P1 — and the per-window CPU timing
        Fdp.Toolkit.Diagnostics.Gizmos.GizmoRuleTimings.Enabled = true;   // ⭐ CE-4385 — and the per-gizmo-rule timing
        Fdp.ModuleHost.KernelFrameTimings.Enabled = true;   // ⭐ CE-4433 — and the kernel's frame by part
        return new FrameStats();
    }

    /// <summary>Start timing one part of the frame.</summary>
    public void Begin() { _partBytes = GC.GetAllocatedBytesForCurrentThread(); _part.Restart(); }

    private long PartBytes => GC.GetAllocatedBytesForCurrentThread() - _partBytes;
    public void EndUpdate() { _update += _part.Elapsed.TotalMilliseconds; _updateBytes += PartBytes; }
    public void EndDraw() { _draw += _part.Elapsed.TotalMilliseconds; _drawBytes += PartBytes; }
    public void EndUi() { _ui += _part.Elapsed.TotalMilliseconds; _uiBytes += PartBytes; }

    /// <summary>
    /// ⭐ CE-4223 — after <c>rlImGui.End</c> (the draw data exists): the most vertices ANY draw list holds this frame, and the window that owns it. ImGui asserts at 65 536 (16-bit indices) and the editor dies, so the
    /// log names the window that is getting near it — the live measurement the rails cannot make.
    /// </summary>
    public unsafe void NoteDrawLists()
    {
        _renderMs += Fdp.Presentation.Raylib.ImGuiRenderer.LastRenderTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        var dd = ImGuiNET.ImGui.GetDrawData();
        if (dd.NativePtr == null) return;
        for (int i = 0; i < dd.CmdListsCount; i++)
        {
            var list = dd.CmdLists[i];
            if (list.VtxBuffer.Size <= _maxVtx) continue;
            _maxVtx = list.VtxBuffer.Size;
            _maxVtxOwner = list._OwnerName.ToString() ?? "?";
        }
    }

    // ⭐ R-4221 (F-5, CE-4505) — every number this log prints is culture-INVARIANT by construction (a decimal point on a Czech machine
    //   too), not only because the executable runs InvariantGlobalization: the measuring scripts read these lines.
    private static string I(FormattableString s) => FormattableString.Invariant(s);

    /// <summary>Call once per frame, after EndDrawing; logs when the window is full.</summary>
    public void EndFrame()
    {
        double now = _clock.Elapsed.TotalMilliseconds;
        if (_lastFrame > 0) _frames.Add(now - _lastFrame);
        _lastFrame = now;
        if (now - _windowStart < WindowSeconds * 1000.0 || _frames.Count == 0) return;
        Report(now);
    }

    /// <summary>Tests: one frame of <paramref name="ms"/> ms, then the report now (no two-second window).</summary>
    internal void ReportNow(double ms) { _frames.Add(ms); Report(_clock.Elapsed.TotalMilliseconds); }

    /// <summary>The window's report (the frames gathered so far), then a new window.</summary>
    private void Report(double now)
    {
        var sorted = _frames.OrderBy(f => f).ToArray();
        int n = sorted.Length;
        Console.WriteLine(
            I($"[FrameStats] {n} frames  mean {sorted.Average():0.0} ms  p50 {sorted[n / 2]:0.0}  p95 {sorted[Math.Min(n - 1, (int)(n * 0.95))]:0.0}") +
            I($"  max {sorted[n - 1]:0.0}  | update {_update / n:0.0}  draw {_draw / n:0.0}  ui {_ui / n:0.0} ms/frame (imgui render {_renderMs / n:0.00})  | largest draw list {_maxVtx} vertices ({_maxVtxOwner})"));
        var all = Fdp.Presentation.WindowManager.WindowTimings.DrainWithBytes();
        var top = all.Take(12).ToList();
        if (top.Count > 0) Console.WriteLine("[FrameStats]   per window (ms/frame, calls/frame): " + string.Join(" · ", top.Select(w => I($"{w.Name} {w.Ms / n:0.0} ({(double)w.Calls / n:0.#})"))));
        // ⭐ CE-2401 (R-2084) — the garbage per window: what kills smoothness is the GC, not only the time
        var heavy = all.Where(w => w.Bytes / n >= 1024).OrderByDescending(w => w.Bytes).Take(8).ToList();
        if (heavy.Count > 0) Console.WriteLine("[FrameStats]   per window garbage (KB/frame): " + string.Join(" · ", heavy.Select(w => I($"{w.Name} {w.Bytes / n / 1024}"))));
        Console.WriteLine(I($"[FrameStats]   gizmo 3-D pass: {Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer.LastPrimitives3D} primitives · labels {Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer.LastLabelsDrawn3D} drawn of {Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer.LastLabels3D} · cards drawn {Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer.LastCardsDrawn} · drape samples {Fdp.Toolkit.Vis3D.GizmoRenderer3D.LastDrapeSamples} in {Fdp.Toolkit.Vis3D.GizmoRenderer3D.LastDrapeMs:0.0} ms · 2-D plan strokes {Hrot.UI.Common.Map2D.TerrainLayer2D.LastStrokesDrawn}"));
        {   // ⭐ CE-4388 — the 3-D gizmo renderer by shape
            var parts = new System.Collections.Generic.List<string>();
            var ticks = Fdp.Toolkit.Vis3D.GizmoRenderer3D.ShapeTicks; var counts = Fdp.Toolkit.Vis3D.GizmoRenderer3D.ShapeCounts;
            for (int k = 0; k < ticks.Length; k++)
                if (counts[k] > 0) { parts.Add(I($"{(Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveShape)k} {ticks[k] * 1000.0 / System.Diagnostics.Stopwatch.Frequency / n:0.00} ms × {counts[k] / n}")); ticks[k] = 0; counts[k] = 0; }
            if (parts.Count > 0) Console.WriteLine("[FrameStats]   gizmo 3-D shapes (ms · count per frame): " + string.Join(" · ", parts));
        }
        // ⭐ CE-4385 — the stateless gizmo rules (the update's biggest system on a large terrain): time, entities drawn and garbage, per rule
        var rulesAll = Fdp.Toolkit.Diagnostics.Gizmos.GizmoRuleTimings.Drain();
        var rules = rulesAll.Take(10).ToList();
        if (rules.Count > 0) Console.WriteLine("[FrameStats]   gizmo rules (ms · entities · KB per frame): " + string.Join(" · ", rules.Select(r => I($"{r.Rule} {r.Ms / n:0.00} · {r.Entities / n} · {r.Bytes / n / 1024}"))));
        // ⭐ CE-4388 — who fills the gizmo buffer: primitives per frame, per gizmo (the buffer holds 16 384)
        var byPrims = rulesAll.Where(r => r.Primitives > 0).OrderByDescending(r => r.Primitives).Take(10).ToList();
        if (byPrims.Count > 0) Console.WriteLine("[FrameStats]   gizmo primitives per frame: " + string.Join(" · ", byPrims.Select(r => I($"{r.Rule} {r.Primitives / n}"))));
        {   // ⭐ CE-4433 — the kernel's frame by part (sync points, harvests, synchronous modules, phases): what lies between the systems
            var parts = Fdp.ModuleHost.KernelFrameTimings.Drain();
            var shown = parts.Where(k => k.Ms / n >= 0.05).Take(40).ToList();
            if (shown.Count > 0) Console.WriteLine("[FrameStats]   kernel (ms · calls · KB per frame): " + string.Join(" · ", shown.Select(k => I($"{k.Name} {k.Ms / n:0.00} · {(double)k.Calls / n:0.##} · {k.Bytes / n / 1024}"))));
        }
        _frames.Clear();
        Console.WriteLine(I($"[FrameStats]   garbage (KB/frame): update {_updateBytes / n / 1024}  draw {_drawBytes / n / 1024}  ui {_uiBytes / n / 1024}") +
                          I($"  | gc0 {GC.CollectionCount(0)} gc1 {GC.CollectionCount(1)} gc2 {GC.CollectionCount(2)} (since start)"));
        _update = _draw = _ui = 0; _renderMs = 0; _maxVtx = 0; _updateBytes = _drawBytes = _uiBytes = 0;
        _windowStart = now;
    }
}
