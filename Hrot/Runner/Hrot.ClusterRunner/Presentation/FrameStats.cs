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

    public static FrameStats? FromEnvironment()
    {
        if (Environment.GetEnvironmentVariable("HROT_FRAME_STATS") != "1") return null;
        Fdp.Presentation.WindowManager.WindowTimings.Enabled = true;   // ⭐ W-P1 — and the per-window CPU timing
        Fdp.Toolkit.Diagnostics.Gizmos.GizmoRuleTimings.Enabled = true;   // ⭐ CE-4385 — and the per-gizmo-rule timing
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

    /// <summary>Call once per frame, after EndDrawing; logs when the window is full.</summary>
    public void EndFrame()
    {
        double now = _clock.Elapsed.TotalMilliseconds;
        if (_lastFrame > 0) _frames.Add(now - _lastFrame);
        _lastFrame = now;
        if (now - _windowStart < WindowSeconds * 1000.0 || _frames.Count == 0) return;
        var sorted = _frames.OrderBy(f => f).ToArray();
        int n = sorted.Length;
        Console.WriteLine(
            $"[FrameStats] {n} frames  mean {sorted.Average():0.0} ms  p50 {sorted[n / 2]:0.0}  p95 {sorted[Math.Min(n - 1, (int)(n * 0.95))]:0.0}" +
            $"  max {sorted[n - 1]:0.0}  | update {_update / n:0.0}  draw {_draw / n:0.0}  ui {_ui / n:0.0} ms/frame  | largest draw list {_maxVtx} vertices ({_maxVtxOwner})");
        var all = Fdp.Presentation.WindowManager.WindowTimings.DrainWithBytes();
        var top = all.Take(6).ToList();
        if (top.Count > 0) Console.WriteLine("[FrameStats]   per window (ms/frame, calls/frame): " + string.Join(" · ", top.Select(w => $"{w.Name} {w.Ms / n:0.0} ({(double)w.Calls / n:0.#})")));
        // ⭐ CE-2401 (R-2084) — the garbage per window: what kills smoothness is the GC, not only the time
        var heavy = all.Where(w => w.Bytes / n >= 1024).OrderByDescending(w => w.Bytes).Take(8).ToList();
        if (heavy.Count > 0) Console.WriteLine("[FrameStats]   per window garbage (KB/frame): " + string.Join(" · ", heavy.Select(w => $"{w.Name} {w.Bytes / n / 1024}")));
        // ⭐ CE-4385 — the stateless gizmo rules (the update's biggest system on a large terrain): time, entities drawn and garbage, per rule
        var rules = Fdp.Toolkit.Diagnostics.Gizmos.GizmoRuleTimings.Drain().Take(6).ToList();
        if (rules.Count > 0) Console.WriteLine("[FrameStats]   gizmo rules (ms · entities · KB per frame): " + string.Join(" · ", rules.Select(r => $"{r.Rule} {r.Ms / n:0.00} · {r.Entities / n} · {r.Bytes / n / 1024}")));
        _frames.Clear();
        Console.WriteLine($"[FrameStats]   garbage (KB/frame): update {_updateBytes / n / 1024}  draw {_drawBytes / n / 1024}  ui {_uiBytes / n / 1024}" +
                          $"  | gc0 {GC.CollectionCount(0)} gc1 {GC.CollectionCount(1)} gc2 {GC.CollectionCount(2)} (since start)");
        _update = _draw = _ui = 0; _maxVtx = 0; _updateBytes = _drawBytes = _uiBytes = 0;
        _windowStart = now;
    }
}
