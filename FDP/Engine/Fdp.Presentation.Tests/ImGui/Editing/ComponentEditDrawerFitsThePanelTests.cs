using System;
using System.Numerics;
using Fdp.Presentation.Editing;
using ImGuiNET;
using StructEdit.Reflection;
using Xunit;

namespace Fdp.Presentation.Tests.ImGui.Editing;

/// <summary>
/// ⭐ CE-4399 (user, 2026-11-10: the details panel's right column is clipped — <i>"Method and Blueprint cut mid-word, Visual ID loses its right side; narrowing the panel hides the right side of every edit box"</i>)
/// — R-4138: a MEASURED rail. The struct table's value controls must END inside the panel's content region at every panel width: the vertices the frame drew (the draw lists) may not pass the window's
/// content right edge. 🔴 Red-proof: see the fix's mutation in the status file.
/// </summary>
public sealed class ComponentEditDrawerFitsThePanelTests
{
    private struct OnlyStr { public string A; }
    private struct OnlyFloat { public float A; }
    private struct OnlyGuid { public Guid A; }
    private struct Facet { public string Method; public string Blueprint; public string VisualId; public float Speed; public int Count; public Guid Id; }

    /// <summary>Draws the drawer in a window of <paramref name="width"/> and returns how far past the content right edge anything drawn reaches (≤ 0 ⇒ fits).</summary>
    private static float Overflow(float width, object? obj = null)
    {
        var service = new ComponentEditServiceBuilder().Build();
        using var session = obj != null ? service.Open(obj, obj.GetType()) : service.Open(new Facet { Method = "Hrot.AI.Behaviors.Brains.PeekAndFireNodes.PeekAndFire", Blueprint = "bp-peek-and-fire-long-name",
            VisualId = "3f0c2d9e-8a41-4b6c-9d57-2e1f7a6b8c90", Speed = 1.5f, Count = 3, Id = Guid.NewGuid() }, typeof(Facet));
        var drawer = new ComponentEditDrawer(session, pickerCtx: null);
        float over = float.NegativeInfinity;
        using var imgui = new ImGuiTestFixture();
        for (int frame = 0; frame < 4; frame++)
        {
            imgui.NewFrame();
            ImGuiNET.ImGui.SetNextWindowPos(new Vector2(10, 10));
            ImGuiNET.ImGui.SetNextWindowSize(new Vector2(width, 400));
            ImGuiNET.ImGui.Begin("Details", ImGuiWindowFlags.NoSavedSettings);
            float right = ImGuiNET.ImGui.GetWindowPos().X + ImGuiNET.ImGui.GetWindowWidth() - ImGuiNET.ImGui.GetStyle().WindowPadding.X;
            if (StructTable.Begin("##t", label: "Property"))
            {
                drawer.DrawRoot(session.Document.Root);
                StructTable.End();
            }
            ImGuiNET.ImGui.End();
            imgui.Render();
            if (frame < 3) continue;
            var dd = ImGuiNET.ImGui.GetDrawData();
            for (int n = 0; n < dd.CmdListsCount; n++)
            {
                var list = dd.CmdLists[n];
                for (int i = 0; i < list.VtxBuffer.Size; i++)
                    over = Math.Max(over, list.VtxBuffer[i].pos.X - right);
            }
        }
        return over;
    }

    [Fact]
    public void TmpProbe()
    {
        var a = Overflow(320, new OnlyStr { A = "hello world long text" });
        var b = Overflow(320, new OnlyFloat { A = 1.5f });
        var c = Overflow(320, new OnlyGuid { A = Guid.NewGuid() });
        System.IO.File.WriteAllText("/tmp/probe_u10.txt", "str=" + a + " float=" + b + " guid=" + c);
    }

    [Theory]
    [InlineData(240f)] [InlineData(320f)] [InlineData(480f)]
    public void TheValueControls_EndInsideThePanelsContent_AtEveryWidth(float width)
    {
        float over = Overflow(width);
        Assert.True(over <= 1f, $"at {width} px the value column reaches {over:F1} px past the content edge");
    }
}
