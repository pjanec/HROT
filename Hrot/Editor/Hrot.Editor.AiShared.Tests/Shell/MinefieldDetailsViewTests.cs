using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Fonts;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Common.Components;
using Hrot.UI.Common.AddEntity;
using ImGuiNET;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐ <c>CE-4624</c> (minefields MS-U3; minefields-visuals.svg panel "Details - the Minefield form") — the Minefield Details form: offered for exactly one minefield, titled <i>Minefield</i>; Type / Spacing / Row orientation / Seed (+ dice) / Anti-handling /
/// Known at start / Mines in ONE value column, then the Lanes (one row each, a trash glyph at the right) and Add lane; every edit ONE undo step; Add lane hands the field to the host's line tool; the Mines line is derived. The frame test SKIPS without a display
/// (<c>xvfb-run -a dotnet test … --filter MinefieldDetailsViewTests</c>); it measures the mockup's layout from <see cref="MinefieldDetailsView.Rects"/>, and 🔴 red-proof: a form that never writes the anti-handling byte leaves it as it was.
/// </summary>
public sealed class MinefieldDetailsViewTests
{
    private sealed class Recorder : IEditStepRecorder
    {
        public readonly List<string> Log = new();
        public void Begin(EntityRepository repo, string label) => Log.Add("begin " + label);
        public void End(EntityRepository repo) => Log.Add("end");
        public void Cancel(EntityRepository repo) => Log.Add("cancel");
    }

    private static (EntityRepository World, Entity Field) World(int lanes = 1)
    {
        var w = new EntityRepository();
        w.RegisterComponent<SimTransform>(); w.RegisterComponent<Minefield>(); w.RegisterComponent<MinefieldStrikes>(); w.RegisterComponent<EntityGuid>();
        w.RegisterManagedComponent<EditablePolyline>();
        var e = w.CreateEntity();
        w.AddComponent(e, new SimTransform { Position = new Vector3(1000f, 2000f, 0f), Rotation = Quaternion.Identity });
        var f = MinefieldOps.Default();
        f.SpacingM = 14f; f.OrientationDeg = 85f; f.KnownAtStartSides = 1 << (int)ForceId.Friend;
        for (int i = 0; i < lanes; i++) { f.SetLane(i, new MinefieldLane(new Vector2(1120 + i, 1960), new Vector2(1150 + i, 2145), 4.5f, (byte)(ForceId.Friend + 1), (ushort)(1 << (int)ForceId.Friend))); f.LaneCount++; }
        w.AddComponent(e, f);
        w.AddComponent(e, new EntityGuid(Guid.NewGuid()));
        w.SetManagedComponent(e, new EditablePolyline { Points = new List<Vector2> { new(0, 0), new(230, -20), new(270, 90), new(150, 140), new(20, 120) } });
        return (w, e);
    }

    private static DetailsContext Context(Entity e) => new(SelectionOrigin.Unknown, Array.Empty<IAssetSubSelection>(), new[] { e }, null, "Scenario", VariableRunState.Planning);

    [Fact]
    public void TheTab_IsOfferedForOneMinefield_TitledMinefield_RequiringItsComponent()
    {
        var (w, e) = World();
        var d = MinefieldDetailsViewDescriptor.For(() => w);
        Assert.Equal("Minefield", d.Title);
        Assert.Contains(typeof(Minefield), d.Requires);
        Assert.True(d.AppliesTo(Context(e)));
    }

    [Fact]
    public void TheMinesLine_IsTheMockupsText_InvariantCulture()
    {
        Assert.Equal("130 (66 AP, 64 AT) · 1 gone off · 9 cleared", MinefieldDetailsView.MinesText(new MinefieldOps.MineCounts(130, 66, 64, 1, 9)));
        var old = System.Globalization.CultureInfo.CurrentCulture;
        try { System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("cs-CZ"); Assert.Equal("1234 (1000 AP, 234 AT) · 0 gone off · 0 cleared", MinefieldDetailsView.MinesText(new MinefieldOps.MineCounts(1234, 1000, 234, 0, 0))); }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
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

    [SkippableFact]
    public void TheForm_DrawsTheMockupsRows_InOneColumn_EveryEditOneStep_AddLaneArmsTheLineTool_AndASteadyFrameAllocatesNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (w, e) = World();
        var rec = new Recorder(); EditSteps.Install(w, rec);
        Entity armed = Entity.Null;
        var view = new MinefieldDetailsView(() => w, f => armed = f);
        using var frame = UiFrameHarness.Begin(780, 560);
        new EditorFontService().Initialize(1f, 1f);
        void Draw()
        {
            ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.SetNextWindowSize(new Vector2(750, 500));
            ImGui.Begin("Details — Minefield##mf");
            view.Draw(Context(e), "rail");
            ImGui.End();
        }
        frame.StepN(6, Draw);
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) { Directory.CreateDirectory(dir); frame.Screenshot(Path.Combine(dir, "minefield-details.png")); }
        var r = new Dictionary<string, (Vector2 Min, Vector2 Max)>(view.Rects);
        foreach (var k in new[] { "type", "spacing", "orientation", "seed", "dice", "antihandling", "side0", "side1", "side2", "mines", "lane0", "lane0.delete", "addlane" }) Assert.True(r.ContainsKey(k), $"the form drew '{k}'");
        // ⭐ the mockup's layout, measured: one value column (the first control of every row at one x), one frame height for the framed controls
        foreach (var k in new[] { "spacing", "orientation", "seed", "antihandling", "side0", "mines" }) Assert.True(Math.Abs(r["type"].Min.X - r[k].Min.X) < 4f, $"'{k}' starts the value column ({r[k].Min.X} vs {r["type"].Min.X})");
        float h = r["type"].Max.Y - r["type"].Min.Y;
        foreach (var k in new[] { "spacing", "orientation", "seed" }) Assert.True(Math.Abs((r[k].Max.Y - r[k].Min.Y) - h) < 1.5f, $"'{k}' has the frame height {h}");
        Assert.True(Math.Abs(r["spacing"].Max.X - r["spacing"].Min.X - (r["orientation"].Max.X - r["orientation"].Min.X)) < 1.5f, "the two number fields share one width");
        var order = new[] { "type", "spacing", "orientation", "seed", "antihandling", "side0", "mines", "lane0", "addlane" };
        for (int i = 1; i < order.Length; i++) Assert.True(r[order[i - 1]].Min.Y < r[order[i]].Min.Y, $"{order[i - 1]} above {order[i]} (the mockup's order)");
        Assert.True(r["side0"].Min.X < r["side1"].Min.X && r["side1"].Min.X < r["side2"].Min.X, "Blue, Red, Green");
        Assert.True(r["dice"].Min.X > r["seed"].Max.X, "the dice sits right of the seed");
        Assert.True(r["lane0.delete"].Min.X > r["lane0"].Max.X + 100f, "the trash glyph is at the lane row's right end");
        Assert.True(Math.Abs((r["lane0.delete"].Min.Y + r["lane0.delete"].Max.Y) / 2f - (r["lane0"].Min.Y + r["lane0"].Max.Y) / 2f) < h, "on the lane's own row");

        // 0 B on a steady frame
        frame.StepN(3, Draw);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20; i++) frame.Step(Draw);
        long perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / 20;
        Assert.True(perFrame < 256, $"a steady frame of the form allocates {perFrame} B (the ImGui driver's own floor, not the form's)");

        // Anti-handling: one step
        rec.Log.Clear();
        Click(frame, Draw, (r["antihandling"].Min + r["antihandling"].Max) * 0.5f);
        Assert.Equal(1, w.GetComponentRO<Minefield>(e).AntiHandling);
        Assert.Equal(new[] { "begin Fit anti-handling devices", "end" }, rec.Log);
        // a side box (Red): one step, bit 2
        rec.Log.Clear();
        Click(frame, Draw, (r["side1"].Min + r["side1"].Max) * 0.5f);
        Assert.Equal((ushort)((1 << 1) | (1 << 2)), w.GetComponentRO<Minefield>(e).KnownAtStartSides);
        Assert.Single(rec.Log, l => l.StartsWith("begin"));
        // the dice: one step
        rec.Log.Clear();
        Click(frame, Draw, (r["dice"].Min + r["dice"].Max) * 0.5f);
        Assert.Equal(new[] { "begin Re-roll minefield", "end" }, rec.Log);
        // Add lane hands the field to the host's line tool, and writes nothing
        rec.Log.Clear();
        Click(frame, Draw, (r["addlane"].Min + r["addlane"].Max) * 0.5f);
        Assert.Equal(e, armed);
        Assert.Empty(rec.Log);
        // the trash glyph deletes the lane in one step
        Click(frame, Draw, (r["lane0.delete"].Min + r["lane0.delete"].Max) * 0.5f);
        Assert.Equal(0, w.GetComponentRO<Minefield>(e).LaneCount);
        Assert.Equal(new[] { "begin Delete lane", "end" }, rec.Log);
    }
}
