using Fdp.Core;
using System;
using System.IO;
using System.Threading;
using Fdp.Presentation.Raylib;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;

namespace Hrot.Editor.UiFrameRail;

/// <summary>
/// ⭐ CE-1495 — the harness's A/B: with <c>HROT_IMGUI_AB=&lt;dir&gt;</c> EVERY rendered frame of EVERY frame rail is drawn TWICE over the same scene — once by <see cref="ImGuiRenderer"/>, once by rlImGui 3.2.0's own render step — and the two
/// frames are compared pixel by pixel. ⭐ The scene is held identical (the frame is read before ImGui draws and put back between the two), so a difference is the RENDERER's, never a rail's time or mouse noise. Frames that differ are written
/// to <c>&lt;dir&gt;</c> (A = ours, B = rlImGui) and every frame appends to <c>ab.log</c>. ⛔ Test support only; off (one <see cref="ImGuiRenderer.End"/>) without the variable.
/// </summary>
public static class UiFrameAbCompare
{
    private static readonly string? Dir = EnvVars.Read(EnvVars.HrotImguiAb) is { Length: > 0 } d ? d : null;
    private static int _frame, _differing;
    private static readonly object Gate = new();

    public static int FramesCompared => _frame;
    public static int FramesDiffering => _differing;

    public static void EndImGui()
    {
        if (Dir is null || ImGuiRenderer.UseRlImGui) { ImGuiRenderer.End(); return; }
        Compare(Dir);
    }

    private static unsafe void Compare(string dir)
    {
        ImGuiRenderer.AdvertiseVtxOffset();
        ImGui.Render();
        long r0 = System.Diagnostics.Stopwatch.GetTimestamp();
        ImGui.Render();   // ⭐ a SECOND Render, timed: rlImGui's End calls Render again (cheaper — no EndFrame), and that cost is not the render STEP, so it is taken off its time
        long renderTicks = System.Diagnostics.Stopwatch.GetTimestamp() - r0;
        Rlgl.DrawRenderBatchActive();
        var scene = Raylib.LoadImageFromScreen();
        var sceneTex = Raylib.LoadTextureFromImage(scene);

        // ⭐ alternate which renderer goes first, so neither pays for the other's cold caches
        bool oursFirst = (_frame & 1) == 0;
        long oursTicks = 0, oldTicks = 0;
        Image a = default, b = default;
        for (int pass = 0; pass < 2; pass++)
        {
            bool ours = (pass == 0) == oursFirst;
            if (pass == 1) { Rlgl.DisableScissorTest(); Raylib.DrawTexture(sceneTex, 0, 0, Color.White); Rlgl.DrawRenderBatchActive(); }   // the scene back, exactly
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            if (ours) ImGuiRenderer.RenderLastFrame(); else rlImGui.End();   // rlImGui's End renders (again) and draws
            Rlgl.DrawRenderBatchActive();
            long dt = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            if (ours) { oursTicks = dt; a = Raylib.LoadImageFromScreen(); } else { oldTicks = dt - renderTicks; b = Raylib.LoadImageFromScreen(); }
        }

        int w = a.Width, h = a.Height; long diff = 0; int max = 0;
        Color* pa = Raylib.LoadImageColors(a); Color* pb = Raylib.LoadImageColors(b);
        for (int i = 0; i < w * h; i++)
        {
            int dr = Math.Abs(pa[i].R - pb[i].R), dg = Math.Abs(pa[i].G - pb[i].G), db = Math.Abs(pa[i].B - pb[i].B);
            int m = Math.Max(dr, Math.Max(dg, db));
            if (m > 0) { diff++; if (m > max) max = m; }
        }
        Raylib.UnloadImageColors(pa); Raylib.UnloadImageColors(pb);
        int n;
        lock (Gate)
        {
            n = ++_frame;
            if (diff > 0) _differing++;
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "ab.log"), $"frame {n} {w}x{h} differing={diff} max={max} vertices={ImGuiRenderer.LastVertexCount} ours_us={oursTicks * 1e6 / System.Diagnostics.Stopwatch.Frequency:0} rlimgui_us={oldTicks * 1e6 / System.Diagnostics.Stopwatch.Frequency:0}\n");
            if (diff > 0 && _differing <= 40)
            {
                Raylib.ExportImage(a, Path.Combine(dir, $"frame{n}-ours.png"));
                Raylib.ExportImage(b, Path.Combine(dir, $"frame{n}-rlimgui.png"));
            }
        }
        Raylib.UnloadImage(a); Raylib.UnloadImage(b); Raylib.UnloadImage(scene); Raylib.UnloadTexture(sceneTex);
    }
}
