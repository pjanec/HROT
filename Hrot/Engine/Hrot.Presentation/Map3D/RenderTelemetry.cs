using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ CE-1145 (docs/DESIGN_Map_3D_Mode.md §6z) — the map view's frame rate and camera, for the on-screen counter and for the AI debug HTTP
/// route <c>GET /diagnostics/render</c>: frames per second, the frame time's mean / p95 / max over the last ~2 s, the view mode, and the
/// camera's eye, direction, yaw / pitch, distance and height above the ground. 🔒 User, 2026-10-16: <i>"the graphics performance
/// details/telemetry (including current view location/direction/zoom) to be accessible via AI debug HTTP to help diagnosing performance
/// issues."</i> One per process (the window): written by <see cref="MapStatsOverlay"/> each drawn frame, read from any thread.
/// </summary>
public static class RenderTelemetry
{
    private static readonly object Gate = new();
    private static readonly Queue<(double At, float Ms)> Frames = new();
    private static double _clock;
    private static JsonObject _camera = new();
    private static string _mode = "2D";
    private static long _lastAllocated = -1;
    private static readonly Queue<long> Allocations = new();   // ⭐ CE-4385 — bytes allocated (whole process) between consecutive frames, the same window

    /// <summary>The window (s) the frame statistics cover.</summary>
    public const double WindowSeconds = 2.0;

    /// <summary>Records one drawn frame of <paramref name="frameSeconds"/>.</summary>
    public static void Frame(float frameSeconds)
    {
        if (!float.IsFinite(frameSeconds) || frameSeconds <= 0f) return;
        lock (Gate)
        {
            _clock += frameSeconds;
            Frames.Enqueue((_clock, frameSeconds * 1000f));
            while (Frames.Count > 0 && _clock - Frames.Peek().At > WindowSeconds) { Frames.Dequeue(); if (Allocations.Count > Frames.Count - 1) Allocations.Dequeue(); }
            long allocated = GC.GetTotalAllocatedBytes(false);
            if (_lastAllocated >= 0) { Allocations.Enqueue(allocated - _lastAllocated); while (Allocations.Count > Frames.Count) Allocations.Dequeue(); }
            _lastAllocated = allocated;
        }
    }

    /// <summary>The camera as this frame shows it.</summary>
    public static void Camera(string mode, Vector3 eye, Vector3 forward, float yaw, float pitch, float distance, float heightAboveGround, Vector2 viewport)
    {
        static JsonArray V(Vector3 v) => new(MathF.Round(v.X, 2), MathF.Round(v.Y, 2), MathF.Round(v.Z, 2));
        lock (Gate)
        {
            _mode = mode;
            _camera = new JsonObject
            {
                ["eye"] = V(eye), ["forward"] = V(forward), ["yawDeg"] = MathF.Round(yaw * 180f / MathF.PI, 1),
                ["pitchDeg"] = MathF.Round(pitch * 180f / MathF.PI, 1), ["distance"] = MathF.Round(distance, 1),
                ["heightAboveGround"] = MathF.Round(heightAboveGround, 1), ["viewport"] = new JsonArray(viewport.X, viewport.Y),
            };
        }
    }

    /// <summary>(fps, mean ms, p95 ms, max ms) over the window; zeros before the first frame.</summary>
    public static (float Fps, float MeanMs, float P95Ms, float MaxMs) Stats()
    {
        lock (Gate)
        {
            if (Frames.Count == 0) return (0f, 0f, 0f, 0f);
            var ms = new List<float>(Frames.Count);
            foreach (var f in Frames) ms.Add(f.Ms);
            ms.Sort();
            float sum = 0f; foreach (var m in ms) sum += m;
            float mean = sum / ms.Count;
            return (mean > 0f ? 1000f / mean : 0f, mean, ms[Math.Min(ms.Count - 1, (int)(ms.Count * 0.95f))], ms[^1]);
        }
    }

    /// <summary>The route's answer.</summary>
    public static JsonObject ToJson()
    {
        var (fps, mean, p95, max) = Stats();
        lock (Gate)
        {
            return new JsonObject
            {
                ["fps"] = MathF.Round(fps, 1), ["frameMsMean"] = MathF.Round(mean, 2), ["frameMsP95"] = MathF.Round(p95, 2),
                ["frameMsMax"] = MathF.Round(max, 2), ["frames"] = Frames.Count, ["windowSeconds"] = WindowSeconds,
                ["mode"] = _mode, ["camera"] = _camera.DeepClone(),
                // ⭐ CE-4385 (handoff L-3) — what the frame drew and what it cost in memory: the chunked terrain's triangles / meshes (one draw call each) /
                // resident mesh bytes, the process' working set and managed heap, and the bytes allocated per frame (mean over the window, WHOLE process —
                // the sim threads included)
                ["terrain"] = new JsonObject
                {
                    ["triangles"] = Fdp.Toolkit.Vis3D.TerrainDrawStats.Triangles, ["meshes"] = Fdp.Toolkit.Vis3D.TerrainDrawStats.Meshes,
                    ["residentMb"] = Math.Round(Fdp.Toolkit.Vis3D.TerrainDrawStats.ResidentBytes / 1048576.0, 1),
                },
                // ⭐ CE-4555 (§6ak.5 ②) — the body layer: instanced draw calls, instances, bodies by screen-size band (CE-4557), doors, the pick's work (CE-4558)
                ["bodies"] = new JsonObject
                {
                    ["drawCalls"] = Fdp.Toolkit.Vis3D.BodyDrawStats.DrawCalls, ["instances"] = Fdp.Toolkit.Vis3D.BodyDrawStats.Instances,
                    ["drawn"] = Fdp.Toolkit.Vis3D.BodyDrawStats.Bodies, ["doors"] = Fdp.Toolkit.Vis3D.BodyDrawStats.Doors,
                    ["full"] = Fdp.Toolkit.Vis3D.BodyDrawStats.Full, ["far"] = Fdp.Toolkit.Vis3D.BodyDrawStats.Far, ["hidden"] = Fdp.Toolkit.Vis3D.BodyDrawStats.Hidden,
                    ["doorsHidden"] = Fdp.Toolkit.Vis3D.BodyDrawStats.DoorsHidden,
                    ["pickBoxesTested"] = Fdp.Toolkit.Vis3D.BodyDrawStats.PickBoxesTested, ["picks"] = Fdp.Toolkit.Vis3D.BodyDrawStats.Picks,
                },
                ["memory"] = new JsonObject
                {
                    ["workingSetMb"] = Math.Round(Environment.WorkingSet / 1048576.0, 1), ["gcHeapMb"] = Math.Round(GC.GetTotalMemory(false) / 1048576.0, 1),
                    ["allocatedBytesPerFrame"] = AllocatedPerFrame(),
                },
            };
        }
    }

    private static long AllocatedPerFrame()
    {
        if (Allocations.Count == 0) return 0;
        long sum = 0; foreach (var a in Allocations) sum += a;
        return sum / Allocations.Count;
    }

    /// <summary>For tests: forget everything.</summary>
    internal static void Reset() { lock (Gate) { Frames.Clear(); Allocations.Clear(); _lastAllocated = -1; _clock = 0; _camera = new JsonObject(); _mode = "2D"; } }
}
