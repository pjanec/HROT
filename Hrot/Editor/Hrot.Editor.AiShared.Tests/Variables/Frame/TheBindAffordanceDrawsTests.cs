using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Fdp.Presentation.Editing;
using Hrot.Editor.UiFrameRail;
using StructEdit.Core;
using StructEdit.Reflection;
using Xunit;

#pragma warning disable CS0649

namespace Hrot.Editor.AiShared.Tests.Variables.Frame;

/// <summary>
/// ⭐ CE-4210 P2 (MA-1) — a REAL ImGui frame of the component drawer with a bind context: the faint chain button sits on the unbound bindable row, the chip on the bound one, a red chip
/// on a binding that cannot hold; a field no parameter fits (and a drawer with no context) shows neither. Under <c>xvfb-run -a</c>; skipped without a display — ⛔ a skip is not a gate row.
/// </summary>
[Collection(Hrot.Editor.AiShared.Tests.PanelSnapshotTestCollection.Name)]
public sealed class TheBindAffordanceDrawsTests
{
    private struct Unit { public float Speed; public int Count; public string Name; }

    private sealed class Ctx : IParamBindContext
    {
        public Dictionary<string, string> Bound = new();
        public IReadOnlyList<ParamInScope> Params { get; } = new[] { new ParamInScope("speed", "float"), new ParamInScope("count", "int") };
        public string? BoundTo(string path) => Bound.TryGetValue(path, out var n) ? n : null;
        public void Bind(string path, string param) => Bound[path] = param;
        public void Unbind(string path) => Bound.Remove(path);
        public string? Problem(string path) => null;
    }

    private static (int Buttons, int Chips, int Red) Draw(IParamBindContext? ctx, string? screenshot = null)
    {
        var svc = new ComponentEditServiceBuilder().Build();
        using var session = svc.Open(new Unit { Speed = 2f, Count = 3, Name = "x" }, typeof(Unit));
        var drawer = new ComponentEditDrawer(session, null, null, ctx);
        using var frame = UiFrameHarness.Begin();
        frame.StepN(4, () =>
        {
            ImGuiNET.ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiNET.ImGuiCond.Always);
            ImGuiNET.ImGui.SetNextWindowSize(new Vector2(560, 260), ImGuiNET.ImGuiCond.Always);
            ImGuiNET.ImGui.Begin("Bind (CE-4210)");
            if (session.RebuildState == EditRebuildState.RebuildRequired) session.RebuildDocument();
            if (ImGuiNET.ImGui.BeginTable("t", 2, ImGuiNET.ImGuiTableFlags.Borders))
            {
                drawer.DrawRoot(session.Document.Root);
                ImGuiNET.ImGui.EndTable();
            }
            ImGuiNET.ImGui.End();
        });
        if (screenshot is not null)
        {
            var dir = Environment.GetEnvironmentVariable("HROT_FRAME_SCREENSHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-frames");
            frame.Screenshot(Path.Combine(dir, screenshot));
        }
        return drawer.LastBindRows;
    }

    [SkippableFact]
    public void AnUnboundBindableRow_ShowsTheFaintButton_AndAFieldNoParamFitsShowsNone()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (buttons, chips, red) = Draw(new Ctx(), "ce4210-bind-unbound.png");
        Assert.Equal(2, buttons);       // Speed (float) and Count (int); Name (string) has no fitting param
        Assert.Equal(0, chips);
        Assert.Equal(0, red);
    }

    [SkippableFact]
    public void ABoundRow_ShowsTheChip_NotTheButton()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var ctx = new Ctx(); ctx.Bound["Speed"] = "speed";
        var (buttons, chips, red) = Draw(ctx, "ce4210-bind-bound.png");
        Assert.Equal(1, buttons);       // Count still offers it
        Assert.Equal(1, chips);
        Assert.Equal(0, red);
    }

    [SkippableFact]
    public void ABindingToAnUndeclaredParam_IsARedChip()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var ctx = new Ctx(); ctx.Bound["Speed"] = "gone";
        var (_, chips, red) = Draw(ctx);
        Assert.Equal(1, chips);
        Assert.Equal(1, red);
    }

    [SkippableFact]
    public void NoContext_DrawsNothingNew()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        Assert.Equal((0, 0, 0), Draw(null));
    }
}
