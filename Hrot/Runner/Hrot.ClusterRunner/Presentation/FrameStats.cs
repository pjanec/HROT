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
    private int _maxVtx; private string _maxVtxOwner = "";

    public static FrameStats? FromEnvironment()
    {
        if (Environment.GetEnvironmentVariable("HROT_FRAME_STATS") != "1") return null;
        Fdp.Presentation.WindowManager.WindowTimings.Enabled = true;   // ⭐ W-P1 — and the per-window CPU timing
        return new FrameStats();
    }

    /// <summary>Start timing one part of the frame.</summary>
    public void Begin() => _part.Restart();

    public void EndUpdate() => _update += _part.Elapsed.TotalMilliseconds;
    public void EndDraw() => _draw += _part.Elapsed.TotalMilliseconds;
    public void EndUi() => _ui += _part.Elapsed.TotalMilliseconds;

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
        var top = Fdp.Presentation.WindowManager.WindowTimings.Drain().Take(6).ToList();
        if (top.Count > 0) Console.WriteLine("[FrameStats]   per window (ms/frame, calls/frame): " + string.Join(" · ", top.Select(w => $"{w.Name} {w.Ms / n:0.0} ({(double)w.Calls / n:0.#})")));
        _frames.Clear();
        _update = _draw = _ui = 0; _maxVtx = 0;
        _windowStart = now;
    }
}
