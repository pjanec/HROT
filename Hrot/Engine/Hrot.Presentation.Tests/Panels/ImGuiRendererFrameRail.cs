using System;
using System.Diagnostics;
using System.Numerics;
using Fdp.Presentation.Raylib;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using ImGuiNET;
using Raylib_cs;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐⭐ CE-1495 — <b>the ONE ImGui draw-data renderer</b> (<see cref="ImGuiRenderer"/>, <c>FDP/Engine/Fdp.Presentation/Raylib/ImGuiRenderer.cs</c>): rlImGui 3.2.0's render step ignored <c>ImDrawCmd.VtxOffset</c>, so ImGui asserted
/// "Too many vertices in ImDrawList using 16-bit indices" at 65 536 vertices in ONE window's list (the OSM wizard's map preview, right after the download — the second time, CE-4223 being the first). The renderer advertises
/// <c>RendererHasVtxOffset</c> and honours the offset, so ImGui splits the list itself and ⭐ <b>no panel can crash the editor by drawing a lot</b>. Every host and this harness go through it (<c>UiFrameSession</c>).
/// ⛔ Skipped when <c>HROT_IMGUI_RENDERER=rlimgui</c> (the old path, kept for comparison): it would assert.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class ImGuiRendererFrameRail
{
    private readonly ITestOutputHelper _out;
    public ImGuiRendererFrameRail(ITestOutputHelper o) => _out = o;

    /// <summary>The colour rect <paramref name="i"/> is drawn in: distinct for neighbours, opaque.</summary>
    private static uint ColourOf(int i) => 0xFF000000u | (uint)(((i * 37) & 0xFF) | (((i / 7 * 53) & 0xFF) << 8) | (((i / 49 * 91) & 0xFF) << 16));

    /// <summary>
    /// ⭐ ONE window drawing 200 000 vertices (50 000 rectangles, 4 vertices each) renders — ⛔ on the old renderer ImGui's assert kills the process. ⭐ And it renders RIGHT: rectangles whose vertices lie past 65 536 (index > 16 383) are read back from
    /// the frame in their own colour, which only holds if the renderer adds the command's <c>VtxOffset</c> to every index.
    /// ✅ Red-proofs: the <c>RendererHasVtxOffset</c> flag cleared ⇒ ImGui's assert; the <c>+ cmd.VtxOffset</c> dropped from <c>RenderData</c> ⇒ the far rectangles are drawn from the wrong vertices (colour mismatch).
    /// </summary>
    [SkippableFact]
    public void AWindowDrawingTwoHundredThousandVertices_RendersWithoutTheAssert_AndEveryRectangleIsInItsOwnColour()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        Skip.If(ImGuiRenderer.UseRlImGui, "HROT_IMGUI_RENDERER=rlimgui — the old renderer cannot draw this");
        const int count = 50_000, cols = 250, pitch = 4;
        using var frame = UiFrameHarness.Begin(1400, 900);
        int worst = 0; int frames0 = ImGuiRenderer.FramesRendered;
        for (int f = 0; f < 4; f++)
            frame.Step(() =>
            {
                ImGui.SetNextWindowPos(new Vector2(20, 20)); ImGui.SetNextWindowSize(new Vector2(cols * pitch, 840));
                ImGui.Begin("many", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings);
                var dl = ImGui.GetWindowDrawList(); var o = ImGui.GetWindowPos();
                for (int i = 0; i < count; i++)
                {
                    var p = o + new Vector2(i % cols * pitch, i / cols * pitch);
                    dl.AddRectFilled(p, p + new Vector2(pitch - 1, pitch - 1), ColourOf(i));
                }
                ImGui.End();
            });
        worst = DrawListVertexRail.MaxListVertices();   // the last rendered frame's draw data
        Assert.True(frame.FramesRendered >= 4 && ImGuiRenderer.FramesRendered - frames0 >= 4, "the frames went through ImGuiRenderer");
        _out.WriteLine($"largest list {worst} vertices; render step {ImGuiRenderer.LastRenderTicks * 1000.0 / Stopwatch.Frequency:0.0} ms for {ImGuiRenderer.LastVertexCount} vertices");
        Assert.True(worst >= 200_000, $"the list holds {worst} vertices — the stress is not stressing");

        var img = Raylib.LoadImageFromScreen();
        try
        {
            int bad = 0; string first = "";
            foreach (int i in new[] { 0, 1, 16_000, 16_383, 16_384, 16_385, 20_000, 33_333, 49_998, 49_999 })   // before and after vertex 65 536 (= rectangle 16 384)
            {
                int x = 20 + i % cols * pitch + 1, y = 20 + i / cols * pitch + 1;
                var c = Raylib.GetImageColor(img, x, y); uint want = ColourOf(i);
                int dr = Math.Abs(c.R - (int)(want & 0xFF)), dg = Math.Abs(c.G - (int)((want >> 8) & 0xFF)), db = Math.Abs(c.B - (int)((want >> 16) & 0xFF));
                if (dr > 1 || dg > 1 || db > 1) { bad++; if (first.Length == 0) first = $"rect {i} at ({x},{y}): drew ({c.R},{c.G},{c.B}), wanted ({want & 0xFF},{(want >> 8) & 0xFF},{(want >> 16) & 0xFF})"; }
            }
            Assert.True(bad == 0, $"{bad} sampled rectangles are in the wrong colour — {first}");
        }
        finally { Raylib.UnloadImage(img); }
    }

    /// <summary>⭐ The backend advertises the flag on the live context (ImGui reads it at NewFrame, so a host that skipped <see cref="ImGuiRenderer.Setup"/> still has it from the second frame).</summary>
    [SkippableFact]
    public void TheContext_AdvertisesRendererHasVtxOffset()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        Skip.If(ImGuiRenderer.UseRlImGui, "HROT_IMGUI_RENDERER=rlimgui — the old renderer");
        using var frame = UiFrameHarness.Begin(400, 300);
        bool has = false;
        frame.Step(() => has = ImGui.GetIO().BackendFlags.HasFlag(ImGuiBackendFlags.RendererHasVtxOffset));
        Assert.True(has);
    }

    /// <summary>⭐ R-2084 — the render step allocates NOTHING in a steady frame (text, windows, a plot of lines).</summary>
    [SkippableFact]
    public void TheRenderStep_AllocatesNothing_InASteadyFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        Skip.If(ImGuiRenderer.UseRlImGui, "HROT_IMGUI_RENDERER=rlimgui — the old renderer");
        using var frame = UiFrameHarness.Begin(800, 500);
        long bytes = -1;
        for (int f = 0; f < 6; f++)
        {
            frame.Step(() =>
            {
                ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.SetNextWindowSize(new Vector2(500, 400));
                ImGui.Begin("steady");
                ImGui.TextUnformatted("the quick brown fox jumps over the lazy dog");
                var dl = ImGui.GetWindowDrawList(); var o = ImGui.GetCursorScreenPos();
                for (int i = 0; i < 200; i++) dl.AddLine(o + new Vector2(i * 2, 0), o + new Vector2(i * 2 + 30, 100), 0xFF00FF00, 1.5f);
                ImGui.End();
            });
            bytes = ImGuiRenderer.LastRenderBytes;
        }
        Assert.Equal(0, bytes);
    }
}
