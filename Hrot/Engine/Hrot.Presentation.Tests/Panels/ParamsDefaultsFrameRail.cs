using System;
using System.Linq;
using System.Numerics;
using Fdp.Presentation.Editing;
using Fdp.Presentation.Fonts;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Behavior;
using Hrot.Presentation.Tests.Map3D;
using ImGuiNET;
using NodeEditor.Core.Widgets;
using StructEdit.Core;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐ CE-3384 (presets §5d) — the params-DEFAULTS form draws the struct table's own rows with the DTO's initialisers as defaults: an overridden field is changed (amber bar + ↺), ↺ commits "no override left",
/// and a steady frame allocates nothing. 🔴 Red-proof (compile): <c>ParamsDefaultsModel.TryEdited</c> keeping a value equal to the DTO default ⇒ the ↺ commit is not null ⇒ red.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class ParamsDefaultsFrameRail
{
    public struct Dto { public float Radius = 1.5f; public int Rounds = 4; public Dto() { } }

    [SkippableFact]
    public void AnOverrideIsChanged_ResetCommitsNoOverride_AndASteadyFrameAllocatesNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(700, 300);
        new EditorFontService().Initialize(1f, 1f);
        var model = new ParamsDefaultsModel(ComponentEditServices.CreateBuilder().Build());
        model.Sync(typeof(Dto), "{\"radius\":2.5}");
        var sim = new WidgetKitFrameRail.Sim();
        long bytes = -1; ParamsDefaultsCommit? commit = null;
        void Draw()
        {
            sim.Feed();
            ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.SetNextWindowSize(new Vector2(680, 260));
            ImGui.Begin("##pdf", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings);
            long before = GC.GetAllocatedBytesForCurrentThread();
            var c = ParamsDefaultsForm.Draw("##t", model);
            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            if (c != null) commit = c;
            ImGui.End();
        }
        for (int k = 0; k < 3; k++) { frame.StepN(15, Draw); System.Threading.Thread.Sleep(220); }
        frame.StepN(5, Draw);
        Assert.True(bytes == 0, $"a steady defaults form allocates nothing ({bytes} bytes)");
        var drawer = model.DrawerFor(null, false)!;
        var fields = model.Session!.Document.Root.Kind == EditNodeKind.SelectionRoot ? model.Session.Document.Root.Children[0] : model.Session.Document.Root;
        Assert.True(drawer.IsChanged(fields.Children.Single(c => c.Name == "Radius")), "the overridden field reads changed");
        Assert.False(drawer.IsChanged(fields.Children.Single(c => c.Name == "Rounds")));
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(System.IO.Path.Combine(dir, "paramsdefaults-form.png"));
        Assert.True(KitProbe.TryGet(KitProbe.ResetMark, out var a, out var b));
        sim.Click(frame, Draw, (a + b) / 2f);
        frame.StepN(3, Draw);
        Assert.NotNull(commit);
        Assert.Null(commit!.Json);                                               // no override is left
    }

    /// <summary>⭐ CE-3384 (b) — the behaviour-params form (the host of the rule, order, entity-AI, mission forms) draws through the ONE struct-table chrome with ONE drawer per session (it built a drawer every frame): 0 B steady, and an edit lands in the session.</summary>
    [SkippableFact]
    public void TheBehaviourParamsForm_DrawsThroughTheStructTable_WithOneDrawerPerSession_AndAllocatesNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(700, 300);
        new EditorFontService().Initialize(1f, 1f);
        var session = BehaviorParamsForm.Open(ComponentEditServices.CreateBuilder().Build(), typeof(Dto), null);
        long bytes = -1;
        void Draw()
        {
            ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.SetNextWindowSize(new Vector2(680, 260));
            ImGui.Begin("##bpf", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings);
            long before = GC.GetAllocatedBytesForCurrentThread();
            BehaviorParamsForm.DrawFields(session, null);
            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            ImGui.End();
        }
        for (int k = 0; k < 3; k++) { frame.StepN(15, Draw); System.Threading.Thread.Sleep(220); }
        frame.StepN(5, Draw);
        Assert.True(bytes == 0, $"a steady behaviour-params form allocates nothing ({bytes} bytes)");
        var root = session.Document.Root.Kind == EditNodeKind.SelectionRoot ? session.Document.Root.Children[0] : session.Document.Root;
        root.Children.Single(c => c.Name == "Rounds").Binding!.SetBoxed(9);
        frame.StepN(3, Draw);
        Assert.Equal(9, ((Dto)session.Commit()).Rounds);
    }
}
