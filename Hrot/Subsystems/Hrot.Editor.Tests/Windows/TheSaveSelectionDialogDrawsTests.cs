using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Hrot.Editor.Scenario;
using Hrot.Editor.UiFrameRail;
using Xunit;

namespace Hrot.Editor.Tests.Windows;

/// <summary>
/// ⭐ CE-4210 MA-9 — a REAL ImGui frame of the Save-selection-as-macro dialog at its closure step: every outside reference listed as <c>entity · field · target</c>, <i>Make parameters</i> enabled when each can become a
/// parameter and DISABLED (with the reason beside the line) when one cannot. Under <c>xvfb-run -a</c>; skipped without a display — ⛔ a skip is not a gate row.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class TheSaveSelectionDialogDrawsTests : IDisposable
{
    private readonly EntityRepository _w = new();
    private readonly Entity _a, _b;

    public TheSaveSelectionDialogDrawsTests()
    {
        _w.RegisterComponent<NetworkIdentity>(); _w.RegisterComponent<EntityGuid>(); _w.RegisterComponent<EntityInfo>();
        _w.RegisterComponent<SimTransform>(); _w.RegisterComponent<RefKey>(); _w.RegisterComponent<SelectionMacroTests.Refs>();
        Entity Make(int net, string name)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new NetworkIdentity(net)); _w.AddComponent(e, new EntityGuid(Guid.NewGuid()));
            _w.AddComponent(e, new EntityInfo { Name = new FixedString64(name) }); _w.AddComponent(e, new SimTransform { Position = new Vector3(net, net, 0) });
            return e;
        }
        _a = Make(1, "Rifleman A"); _b = Make(2, "Rifleman B");
        var hill = Make(4, "Hill");
        _w.AddComponent(hill, new RefKey("Hill"));
        _w.AddComponent(_a, new SelectionMacroTests.Refs { Follow = new EntityLink(new EntityRef(4)) });
    }

    public void Dispose() => _w.Dispose();

    private SaveSelectionAsMacroWindow Window(Action<SaveSelectionAsMacroFlow>? tweak = null)
    {
        var flow = new SaveSelectionAsMacroFlow(() => _w, () => new[] { _a, _b }, (_, _, _) => { });
        flow.Begin(); flow.Name = "test/sel-w5"; flow.Confirm();
        tweak?.Invoke(flow);
        return new SaveSelectionAsMacroWindow(flow);
    }

    private static void Frame(SaveSelectionAsMacroWindow w, string? screenshot = null)
    {
        using var frame = UiFrameHarness.Begin();
        frame.StepN(4, () =>
        {
            ImGuiNET.ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiNET.ImGuiCond.Always);
            ImGuiNET.ImGui.SetNextWindowSize(new Vector2(620, 260), ImGuiNET.ImGuiCond.Always);
            ImGuiNET.ImGui.Begin("Save selection as macro (CE-4210)");
            w.DrawBody();
            ImGuiNET.ImGui.End();
        });
        if (screenshot is not null)
        {
            var dir = Environment.GetEnvironmentVariable("HROT_FRAME_SCREENSHOTS") is { Length: > 0 } d ? d : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hrot-frames");
            frame.Screenshot(System.IO.Path.Combine(dir, screenshot));
        }
    }

    [SkippableFact]
    public void TheClosureStep_ListsTheOutsideReference_AndOffersMakeParameters()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var w = Window();
        Assert.Equal(SaveSelectionAsMacroFlow.StepKind.Closure, w.Flow.Step);
        Frame(w, "ce4210-save-selection-closure.png");
        Assert.Equal(1, w.ReferenceLines);
        Assert.True(w.MakeParametersEnabled);
        Assert.Equal(0, w.BlockerLines);
    }

    [SkippableFact]
    public void AReferenceThatCannotBeAParam_DisablesMakeParameters_AndSaysWhy()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        _w.AddComponent(_b, new SelectionMacroTests.Refs { Plain = new EntityRef(4) });
        var w = Window();
        Frame(w, "ce4210-save-selection-blocked.png");
        Assert.Equal(2, w.ReferenceLines);
        Assert.False(w.MakeParametersEnabled);
        Assert.Equal(1, w.BlockerLines);
    }
}
