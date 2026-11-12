using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;

namespace Fdp.Presentation.Raylib;

/// <summary>
/// ⭐⭐⭐ <b>CE-1495 — the ONE ImGui draw-data renderer, for every host and the frame-rail harness.</b> It replaces ONLY <c>rlImGui.End()</c>'s RENDER step
/// (rlImGui 3.2.0 keeps input, fonts and the font texture); <see cref="Setup"/> / <see cref="End"/> are the single pair a host calls instead of
/// <c>rlImGui.Setup</c> / <c>rlImGui.End</c>.
///
/// <para>🔴 <b>Why:</b> rlImGui 3.2.0's <c>RenderTriangles</c> ignores <c>ImDrawCmd.VtxOffset</c>, so ImGui cannot split a draw list at 65 536 vertices and ASSERTS
/// ("Too many vertices in ImDrawList using 16-bit indices") — 📌 the OSM wizard's map preview crashed this way after CE-4223's fix of one panel. ⭐ Here the
/// backend advertises <see cref="ImGuiBackendFlags.RendererHasVtxOffset"/> and honours <c>VtxOffset</c>, so a window draw list of any size is split by ImGui itself.</para>
///
/// <para>⭐ <b>Mirrors rlImGui's RenderData exactly</b> (same rlgl immediate mode, scissor and Y-flip maths, texture id, colour conversion, user callback) — the ONLY
/// change is the vertex base. ⭐ <c>HROT_IMGUI_RENDERER=rlimgui</c> selects the old path (for comparison / fallback). ⛔ 0 B per steady frame (hot path).</para>
/// </summary>
public static unsafe class ImGuiRenderer
{
    public const string EnvVar = "HROT_IMGUI_RENDERER";

    private delegate void UserCallback(ImDrawListPtr list, ImDrawCmdPtr cmd);

    /// <summary>⭐ True when <c>HROT_IMGUI_RENDERER=rlimgui</c> — the old rlImGui render path, no VtxOffset support.</summary>
    public static readonly bool UseRlImGui =
        string.Equals(Environment.GetEnvironmentVariable(EnvVar), "rlimgui", StringComparison.OrdinalIgnoreCase);

    /// <summary>⭐ Counts every <see cref="End"/> — a rail surface (did the frame go through THIS renderer?).</summary>
    public static int FramesRendered { get; private set; }

    /// <summary>⭐ Wall-clock ticks (Stopwatch) spent in the render step of the LAST <see cref="End"/> (excludes <c>ImGui.Render</c>).</summary>
    public static long LastRenderTicks { get; private set; }

    /// <summary>⭐ Bytes allocated by the render step of the last <see cref="End"/> — must be 0 (hot path).</summary>
    public static long LastRenderBytes { get; private set; }

    /// <summary>⭐ Vertices submitted by the last <see cref="End"/>.</summary>
    public static int LastVertexCount { get; private set; }

    /// <summary>⭐ <c>rlImGui.Setup</c> + the backend flag. The one Setup every host calls.</summary>
    public static void Setup(bool darkTheme = true, bool enableDocking = false)
    {
        rlImGui.Setup(darkTheme, enableDocking);
        AdvertiseVtxOffset();
    }

    /// <summary>⭐ Sets <see cref="ImGuiBackendFlags.RendererHasVtxOffset"/> on the CURRENT context (ImGui reads it at NewFrame). Idempotent; a no-op on the old path.</summary>
    public static void AdvertiseVtxOffset()
    {
        if (UseRlImGui) return;
        ImGui.GetIO().BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
    }

    /// <summary>⭐ Replaces <c>rlImGui.End()</c>: <c>ImGui.Render()</c> then draw the data.</summary>
    public static void End()
    {
        if (UseRlImGui)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            rlImGui.End();
            LastRenderTicks = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            FramesRendered++;
            return;
        }

        AdvertiseVtxOffset();   // safety net for a host that built its context outside Setup (takes effect next NewFrame)
        ImGui.Render();
        RenderLastFrame();
    }

    /// <summary>⭐ Draws the draw data of the last <c>ImGui.Render()</c> with THIS renderer (the harness's A/B comparison renders one frame with each).</summary>
    public static void RenderLastFrame()
    {
        long bytes0 = GC.GetAllocatedBytesForCurrentThread();
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        RenderData(ImGui.GetDrawData());
        LastRenderTicks = System.Diagnostics.Stopwatch.GetTimestamp() - start;
        LastRenderBytes = GC.GetAllocatedBytesForCurrentThread() - bytes0;
        FramesRendered++;
    }

    private static void EnableScissor(float x, float y, float width, float height, in ImGuiIOPtr io, System.Numerics.Vector2 fbScale)
    {
        Rlgl.EnableScissorTest();
        Rlgl.Scissor((int)(x * fbScale.X), (int)((io.DisplaySize.Y - (int)(y + height)) * fbScale.Y), (int)(width * fbScale.X), (int)(height * fbScale.Y));
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]   // the per-vertex loop: fully optimised from the first frame (a Debug build would otherwise run it unoptimised)
    private static void RenderData(ImDrawDataPtr drawData)
    {
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableBackfaceCulling();

        var io = ImGui.GetIO();
        var fbScale = new System.Numerics.Vector2(1f, 1f);
        // rlImGui: the framebuffer scale applies only with a HiDPI window (or on macOS).
        if (Raylib_cs.Raylib.IsWindowState(ConfigFlags.HighDpiWindow) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            fbScale = io.DisplayFramebufferScale;

        int vertices = 0;
        System.Numerics.Vector2 displayPos = drawData.DisplayPos;
        for (int i = 0; i < drawData.CmdListsCount; i++)
        {
            ImDrawListPtr list = drawData.CmdLists[i];
            vertices += list.VtxBuffer.Size;
            for (int j = 0; j < list.CmdBuffer.Size; j++)
            {
                ImDrawCmdPtr cmd = list.CmdBuffer[j];
                var clip = cmd.ClipRect;
                EnableScissor(clip.X - displayPos.X, clip.Y - displayPos.Y, clip.Z - (clip.X - displayPos.X), clip.W - (clip.Y - displayPos.Y), io, fbScale);

                if (cmd.UserCallback != IntPtr.Zero)
                {
                    Marshal.GetDelegateForFunctionPointer<UserCallback>(cmd.UserCallback)(list, cmd);
                    continue;
                }

                RenderTriangles(cmd.ElemCount,
                    (ushort*)list.IdxBuffer.Data + cmd.IdxOffset,
                    (ImDrawVert*)list.VtxBuffer.Data + cmd.VtxOffset,   // ⭐⭐ the whole fix: indices are relative to VtxOffset
                    cmd.TextureId);
                Rlgl.DrawRenderBatchActive();
            }
        }
        LastVertexCount = vertices;

        Rlgl.SetTexture(0u);
        Rlgl.DisableScissorTest();
        Rlgl.EnableBackfaceCulling();
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void RenderTriangles(uint count, ushort* indices, ImDrawVert* vertices, IntPtr texturePtr)
    {
        if (count < 3) return;

        uint texture = 0u;
        if (texturePtr != IntPtr.Zero) texture = (uint)texturePtr.ToInt32();

        Rlgl.Begin(DrawMode.Triangles);
        Rlgl.SetTexture(texture);
        // ⭐ rlgl keeps the current colour and texcoord between vertices, so one that equals the last is not set again — most of a draw list is flat colour on the white pixel (2 of the 3 P/Invokes saved).
        //   Colour goes in as bytes: ImGui's U32 → float (×1/255) → rlgl's byte (×255, truncated) is the identity for all 256 values (checked), so Color4ub is bit-identical to rlImGui's Color4f.
        bool have = false; uint lastCol = 0; float lastU = 0, lastV = 0;
        for (int i = 0; i <= (int)count - 3; i += 3)
        {
            if (Rlgl.CheckRenderBatchLimit(3))
            {
                Rlgl.Begin(DrawMode.Triangles);
                Rlgl.SetTexture(texture);
                have = false;
            }
            for (int k = 0; k < 3; k++)
            {
                ImDrawVert* v = vertices + indices[i + k];
                uint c = v->col; float u = v->uv.X, w = v->uv.Y;
                if (!have) { Rlgl.Color4ub((byte)c, (byte)(c >> 8), (byte)(c >> 16), (byte)(c >> 24)); Rlgl.TexCoord2f(u, w); lastCol = c; lastU = u; lastV = w; have = true; }
                else
                {
                    if (c != lastCol) { Rlgl.Color4ub((byte)c, (byte)(c >> 8), (byte)(c >> 16), (byte)(c >> 24)); lastCol = c; }
                    if (u != lastU || w != lastV) { Rlgl.TexCoord2f(u, w); lastU = u; lastV = w; }
                }
                Rlgl.Vertex2f(v->pos.X, v->pos.Y);
            }
        }
        Rlgl.End();
    }
}
