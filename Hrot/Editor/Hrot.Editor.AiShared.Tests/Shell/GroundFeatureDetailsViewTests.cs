using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Fonts;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Terrain;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Common.Components;
using Hrot.UI.Common.AddEntity;
using ImGuiNET;
using NodeEditor.Core.Widgets;
using Raylib_cs;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐⭐ <c>CE-4592</c> / <c>CE-4593</c> (P-5 part 2b, P-6; mockup <c>ground-shaping-and-ditches.svg</c> C, D) — the <b>Ground</b> tab of a placed ground shape: offered for one ground shape only,
/// its icon the orange mound; the Mode row (Level to Z · Raise by dZ · Lower by dZ · Crater greyed) in one row; the value; the derived Skirt; the Effect rows measured on the merge (the area and
/// change, what is kept out, the draw order — "drawn over" an older shape); a ditch's Preset row and its cover / vehicle rows. ONE undo step per write; the draw order is never touched.
/// 🔴 Red-proofs: the Mode button not calling <c>SetMode</c> ⇒ the click rail red; <c>Overlaps</c> counting newer as older ⇒ the "drawn over" assertion red.
/// </summary>
[Collection(Hrot.Editor.AiShared.Tests.PanelSnapshotTestCollection.Name)]
public sealed class GroundFeatureDetailsViewTests
{
    private sealed class Recorder : IEditStepRecorder
    {
        public readonly List<string> Log = new();
        public void Begin(EntityRepository repo, string label) => Log.Add("begin " + label);
        public void End(EntityRepository repo) => Log.Add("end");
        public void Cancel(EntityRepository repo) => Log.Add("cancel");
    }

    private static Vector2[] Square(float h) => new[] { new Vector2(-h, -h), new Vector2(h, -h), new Vector2(h, h), new Vector2(-h, h) };

    private static Entity Shape(EntityRepository w, string mode, float pad, uint order, Vector2 at, string name, params Vector2[] relative)
    {
        var e = w.CreateEntity();
        w.AddComponent(e, new SimTransform { Position = new Vector3(at, 0f), Rotation = Quaternion.Identity });
        w.AddComponent(e, new TerrainFeature { Kind = TerrainFeature.Ground, Template = new FixedString32(mode), Pad = pad, Seed = order });
        w.AddComponent(e, new EntityInfo { Name = new FixedString64(name) });
        w.SetManagedComponent(e, new EditablePolyline { Points = relative, Version = 1 });
        return e;
    }

    private static EntityRepository World()
    {
        var w = new EntityRepository();
        w.RegisterComponent<SimTransform>(); w.RegisterComponent<TerrainFeature>(); w.RegisterComponent<EntityInfo>();
        w.RegisterManagedComponent<EditablePolyline>();
        return w;
    }

    private static TerrainGhostContext Flat()
    {
        int n = 201;
        var flat = new TerrainWorld { Name = "flat", BoundsMin = new Vector2(-100f), BoundsMax = new Vector2(100f), Height = new TerrainHeightGrid(new Vector2(-100f), 1f, n, n, new float[n * n]) };
        return new TerrainGhostContext(flat, Path.Combine(Path.GetTempPath(), "no-terrain-" + Guid.NewGuid().ToString("N")));
    }

    private static DetailsContext Context(Entity e) => new(SelectionOrigin.Unknown, Array.Empty<IAssetSubSelection>(), new[] { e }, null, "Scenario", VariableRunState.Planning);

    /// <summary>The tab: offered for exactly one ground shape (not a wall), titled <i>Ground</i>, its icon the mound in orange; mode and value are one edit step each and leave the draw order.</summary>
    [Fact]
    public void TheGroundTab_IsOfferedForAGroundShapeOnly_AndItsWritesAreOneStepEach()
    {
        var w = World();
        var raise = Shape(w, "raise", 2f, 100, Vector2.Zero, "Raised ground", Square(10f));
        var wall = w.CreateEntity(); w.AddComponent(wall, new TerrainFeature { Kind = TerrainFeature.Wall });
        var d = GroundFeatureDetailsViewDescriptor.For(() => w, () => null);
        Assert.Equal("Ground", d.Title);
        Assert.True(d.AppliesTo(Context(raise)));
        Assert.False(d.AppliesTo(Context(wall)));
        Assert.Equal(Icons.Get("earth/mound"), DetailsWindow.TabIconOf("Ground"));
        Assert.Equal(IconPalette.Orange, DetailsWindow.TabIconOf("Ground")!.Value.Color);

        var rec = new Recorder(); EditSteps.Install(w, rec);
        Assert.True(GroundFeatureOps.SetMode(w, raise, "lower", 1f));
        Assert.Equal("lower", w.GetComponentRO<TerrainFeature>(raise).Template.ToString());
        Assert.True(GroundFeatureOps.SetValue(w, raise, 3.5f));
        Assert.Equal(3.5f, w.GetComponentRO<TerrainFeature>(raise).Pad);
        Assert.False(GroundFeatureOps.SetValue(w, raise, 0f));                 // a lower of 0 m is refused
        Assert.False(GroundFeatureOps.SetValue(w, raise, 3.5f));               // unchanged: no step
        Assert.Throws<ArgumentException>(() => GroundFeatureOps.SetMode(w, raise, "dig", 1f));
        Assert.Equal(4, rec.Log.Count);
        Assert.Equal(100u, w.GetComponentRO<TerrainFeature>(raise).Seed);       // the draw order never moves
    }

    /// <summary>
    /// The form in a REAL frame (Xvfb; a skip is not a pass): the Mode row in ONE row, the value under it, the Skirt read-out; the Effect rows say the area and the raise, what is kept out, that
    /// the shape is drawn over the older one, and the SUMO line — the mound in orange; a click on <i>Lower by dZ</i> lowers by the default 1 m in one step. A ditch shows its Preset row and cover
    /// rows. Shots under <c>HROT_RAIL_SHOTS</c> (never committed).
    /// </summary>
    [SkippableFact]
    public void TheForm_HasTheMockupsRowsAndEffect_AClickSwitchesTheMode_ADitchItsPresetAndCover()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var w = World();
        Shape(w, "raise", 1f, 50, new Vector2(5f, 0f), "Raise 1", Square(6f));            // an OLDER shape under this one
        var e = Shape(w, "raise", 5f, 100, Vector2.Zero, "Raised ground", Square(10f));
        var rec = new Recorder(); EditSteps.Install(w, rec);
        var ctx = Flat();
        var view = new GroundFeatureDetailsView(() => w, () => ctx);
        using var frame = UiFrameHarness.Begin(460, 520);
        new EditorFontService().Initialize(1f, 1f);
        Entity shown = e;
        void Draw()
        {
            ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.SetNextWindowSize(new Vector2(440, 480));
            ImGui.Begin("Details — Ground 1##p5");
            view.Draw(Context(shown), "rail");
            ImGui.End();
        }
        frame.StepN(6, Draw);
        string? shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS");
        if (shots is { Length: > 0 }) { Directory.CreateDirectory(shots); frame.Screenshot(Path.Combine(shots, "p5-ground-details.png")); }

        var r = new Dictionary<string, (Vector2 Min, Vector2 Max)>(view.Rects);
        Assert.Single(new[] { "mode:Level to Z", "mode:Raise by dZ", "mode:Lower by dZ", "mode:Crater" }.Select(k => (int)r[k].Min.Y).Distinct());
        Assert.True(r["mode:Level to Z"].Max.X <= r["mode:Raise by dZ"].Min.X && r["mode:Lower by dZ"].Max.X <= r["mode:Crater"].Min.X, "the four modes in the mockup's order");
        Assert.True(r["value"].Min.Y > r["mode:Raise by dZ"].Max.Y);
        Assert.Equal(r["value"].Min.X, r["mode:Level to Z"].Min.X, 2f);           // one value column
        var texts = view.Effect.Select(x => x.Text).ToList();
        Assert.Equal(new[]
        {
            "no road reached", "area 400 m² · raised 5.0 m", "kept out: 0 houses + skirt · 0 road corridors",
            "drawn over Raise 1 — the newest wins (V-28)", "SUMO net re-elevated at save (our roads follow now)",
        }, texts);
        var shot = Raylib.LoadImageFromScreen();
        try
        {
            var m = r["effect:1"];
            Assert.True(Count(shot, new Vector2(10, m.Min.Y - 2), new Vector2(m.Min.X, m.Max.Y + 2), IconPalette.Orange) >= 6, "the area row carries the mound in orange");
        }
        finally { Raylib.UnloadImage(shot); }

        var lower = r["mode:Lower by dZ"];
        Click(frame, Draw, (lower.Min + lower.Max) * 0.5f);
        Assert.Equal("lower", w.GetComponentRO<TerrainFeature>(e).Template.ToString());
        Assert.Equal(TerrainBirth.DefaultLower, w.GetComponentRO<TerrainFeature>(e).Pad);
        Assert.Equal(2, rec.Log.Count);
        frame.StepN(3, Draw);
        Assert.Equal("area 400 m² · lowered 1.0 m", view.Effect[1].Text);

        // a ditch: the Preset row (the Foxhole greyed for a line), the cover and vehicle rows
        shown = Shape(w, "ditch", 0f, 200, new Vector2(0f, 60f), "Ditch", new Vector2(-30f, 0f), new Vector2(34f, 0f));
        frame.StepN(6, Draw);
        if (shots is { Length: > 0 }) frame.Screenshot(Path.Combine(shots, "p6-ditch-details.png"));
        r = new Dictionary<string, (Vector2 Min, Vector2 Max)>(view.Rects);
        Assert.Single(new[] { "mode:Ditch", "mode:Anti-tank ditch", "mode:Foxhole" }.Select(k => (int)r[k].Min.Y).Distinct());
        Assert.Equal(new[]
        {
            "cover: crouch along both banks", "vehicles: wheeled closed · tracked closed", "infantry passes (a small cost)", "crosses no road", "64 m · 2 vertices · newest on top",
        }, view.Effect.Select(x => x.Text).ToArray());
    }

    private static void Pos(float x, float y) { ImGui.GetIO().AddMousePosEvent(x, y); Raylib.SetMousePosition((int)Math.Round(x), (int)Math.Round(y)); }

    private static void Click(UiFrameSession frame, Action draw, Vector2 at)
    {
        frame.Step(() => { Pos(at.X, at.Y); draw(); });
        frame.Step(() => { Pos(at.X, at.Y); ImGui.GetIO().AddMouseButtonEvent(0, true); draw(); });
        frame.Step(() => { Pos(at.X, at.Y); ImGui.GetIO().AddMouseButtonEvent(0, false); draw(); });
        frame.Step(() => { Pos(5, 5); draw(); });
        frame.Step(draw);
    }

    private static int Count(Image image, Vector2 min, Vector2 max, Vector4 c, int tol = 10)
    {
        int n = 0, r = (int)Math.Round(c.X * 255), g = (int)Math.Round(c.Y * 255), b = (int)Math.Round(c.Z * 255);
        for (int y = Math.Max(0, (int)min.Y); y < (int)max.Y && y < image.Height; y++)
            for (int x = Math.Max(0, (int)min.X); x < (int)max.X && x < image.Width; x++)
            {
                var p = Raylib.GetImageColor(image, x, y);
                if (Math.Abs(p.R - r) <= tol && Math.Abs(p.G - g) <= tol && Math.Abs(p.B - b) <= tol) n++;
            }
        return n;
    }
}
