using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Motion;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Hrot.Editor.UiFrameRail;
using Fdp.Presentation.Fonts;
using Fdp.Toolkit.Replication.Edits;
using System.Collections.Generic;
using System.IO;
using ImGuiNET;
using Hrot.UI.Common.AddEntity;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐ <c>CE-4647</c> (AC-15 step 6; air-combat-arena.svg, "Details - Air Arena") — the Air Arena Details tab: offered for exactly one arena, titled <i>Air Arena</i>, requiring the air lane's <see cref="AirArena"/>, ranked beside the minefield's (39 → 40);
/// registered by the editor next to it (a source check: the descriptor is added in <c>EditorSubsystem</c>). The writes are <see cref="AirArenaOps"/> (rails in <c>AirArenaGizmoRail</c>). ⚠ The form's pixel layout is NOT measured by a frame rail (the Minefield form's was): a follow-up.
/// </summary>
public sealed class AirArenaDetailsViewTests
{
    private static DetailsContext Context(Entity e) => new(SelectionOrigin.Unknown, Array.Empty<IAssetSubSelection>(), new[] { e }, null, "Scenario", VariableRunState.Planning);

    [Fact]
    public void TheTab_IsOfferedForOneArena_TitledAirArena_RequiringItsComponent_NextToTheMinefields()
    {
        var w = new EntityRepository();
        w.RegisterComponent<AirArena>();
        var e = w.CreateEntity(); w.AddComponent(e, AirArenaOps.Default());
        var d = AirArenaDetailsViewDescriptor.For(() => w);
        Assert.Equal("Air Arena", d.Title);
        Assert.Contains(typeof(AirArena), d.Requires);
        Assert.True(d.AppliesTo(Context(e)));
        Assert.Equal(MinefieldDetailsViewDescriptor.Rank + 1, AirArenaDetailsViewDescriptor.Rank);
        string src = System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Subsystems", "Hrot.Editor", "EditorSubsystem.cs"));
        Assert.Contains("AirArenaDetailsViewDescriptor.For(", src);
    }

    private sealed class Recorder : IEditStepRecorder
    {
        public readonly List<string> Log = new();
        public void Begin(EntityRepository repo, string label) => Log.Add("begin " + label);
        public void End(EntityRepository repo) => Log.Add("end");
        public void Cancel(EntityRepository repo) => Log.Add("cancel");
    }

    private static void Pos(float x, float y) { ImGui.GetIO().AddMousePosEvent(x, y); Raylib_cs.Raylib.SetMousePosition((int)Math.Round(x), (int)Math.Round(y)); }

    private static void Click(UiFrameSession frame, Action draw, Vector2 at)
    {
        frame.Step(() => { Pos(at.X, at.Y); draw(); });
        frame.Step(() => { Pos(at.X, at.Y); ImGui.GetIO().AddMouseButtonEvent(0, true); draw(); });
        frame.Step(() => { Pos(at.X, at.Y); ImGui.GetIO().AddMouseButtonEvent(0, false); draw(); });
        frame.Step(() => { Pos(5, 5); draw(); });
        frame.Step(draw);
    }

    /// <summary>
    /// ⭐ R-4138 / R-4146 — the form's LAYOUT measured against the mockup's form panel (<c>air-combat-arena.svg</c>, "Details - Air Arena"): the seven rows in the mockup's order (Centre · Radius · Floor · Ceiling · Max shot range · Stalemate · Lapses off), ONE value column (every
    /// control at one x), ONE frame height and one width for the framed controls, the Show rules group below Geometry, a steady frame under the ImGui floor, and a checkbox click is ONE undo step. SKIPS without a display (<c>xvfb-run -a dotnet test … --filter AirArenaDetailsViewTests</c>).
    /// 🔴 Red-proof: a form that never writes the lapses flag leaves it as it was (the click rail).
    /// </summary>
    [SkippableFact]
    public void TheForm_DrawsTheMockupsRows_InOneColumn_OneFrameHeightAndWidth_TheGroupsInOrder_AndACheckboxIsOneStep()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var w = new EntityRepository();
        w.RegisterComponent<AirArena>();
        var e = w.CreateEntity(); var a0 = AirArenaOps.Default(); a0.Centre = new Vector3(4200f, 3100f, 0f); a0.RadiusM = 6000f; a0.FloorAglM = 150f; a0.CeilingAglM = 4000f; w.AddComponent(e, a0);
        var rec = new Recorder(); EditSteps.Install(w, rec);
        var view = new AirArenaDetailsView(() => w);
        using var frame = UiFrameHarness.Begin(780, 560);
        new EditorFontService().Initialize(1f, 1f);
        void Draw()
        {
            ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.SetNextWindowSize(new Vector2(750, 500));
            ImGui.Begin("Details — Air Arena##aa");
            view.Draw(Context(e), "rail");
            ImGui.End();
        }
        frame.StepN(6, Draw);
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) { Directory.CreateDirectory(dir); frame.Screenshot(Path.Combine(dir, "airarena-details.png")); }
        var r = new Dictionary<string, (Vector2 Min, Vector2 Max)>(view.Rects);
        var order = new[] { "centre", "radius", "floor", "ceiling", "shot", "stalemate", "lapses" };
        foreach (var k in order) Assert.True(r.ContainsKey(k), $"the form drew '{k}'");
        foreach (var k in order) Assert.True(Math.Abs(r["centre"].Min.X - r[k].Min.X) < 4f, $"'{k}' starts the value column ({r[k].Min.X} vs {r["centre"].Min.X})");
        float h = r["centre"].Max.Y - r["centre"].Min.Y, wd = r["centre"].Max.X - r["centre"].Min.X;
        foreach (var k in new[] { "radius", "floor", "ceiling", "shot", "stalemate" })
        {
            Assert.True(Math.Abs((r[k].Max.Y - r[k].Min.Y) - h) < 1.5f, $"'{k}' has the frame height {h}");
            Assert.True(Math.Abs((r[k].Max.X - r[k].Min.X) - wd) < 1.5f, $"'{k}' has the framed width {wd}");
        }
        for (int i = 1; i < order.Length; i++) Assert.True(r[order[i - 1]].Min.Y < r[order[i]].Min.Y, $"{order[i - 1]} above {order[i]} (the mockup's order)");
        Assert.True(r["shot"].Min.Y - r["ceiling"].Max.Y > 10f, "the Show rules group is set apart below Geometry (a header between)");
        frame.StepN(3, Draw);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20; i++) frame.Step(Draw);
        long perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / 20;
        Assert.True(perFrame < 256, $"a steady frame of the form allocates {perFrame} B (the ImGui driver's own floor, not the form's)");
        rec.Log.Clear();
        Click(frame, Draw, (r["lapses"].Min + r["lapses"].Max) * 0.5f);
        Assert.Equal((byte)1, w.GetComponentRO<AirArena>(e).LapsesOff);
        Assert.Equal(new[] { "begin Arena: no lapses", "end" }, rec.Log);
    }
}
