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
            while (Frames.Count > 0 && _clock - Frames.Peek().At > WindowSeconds) Frames.Dequeue();
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
            };
        }
    }

    /// <summary>For tests: forget everything.</summary>
    internal static void Reset() { lock (Gate) { Frames.Clear(); _clock = 0; _camera = new JsonObject(); _mode = "2D"; } }
}
