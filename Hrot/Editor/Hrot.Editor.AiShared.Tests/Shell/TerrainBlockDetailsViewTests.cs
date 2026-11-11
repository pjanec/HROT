using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Fonts;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Services;
using Hrot.UI.Common.AddEntity;
using ImGuiNET;
using NodeEditor.Core.Widgets;
using Raylib_cs;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐ Terrain authoring U-3 (mockup <c>terrain-authoring-2-blocks.svg</c>, the Details panel) — the <b>Block</b> tab of a placed block: Recipe (the block icon + the dropdown of the shipped recipes), Seed (typed + the dice), Fit to the site (four checkboxes
/// on the <see cref="TerrainBlock.Fit"/> bits; <i>Align to the nearest road</i> live (T-5), <i>Level houses on pads</i> greyed with its reason), Contents. ONE undo step per write. The frame is read with the editor's real fonts (Xvfb; a skip is not a pass).
/// 🔴 Red-proofs (compile): <c>TerrainBlockOps.SetFit</c> ignoring the bit ⇒ the fit rail red; the dice not calling <c>Reroll</c> ⇒ the form rail red.
/// </summary>
[Collection(Hrot.Editor.AiShared.Tests.PanelSnapshotTestCollection.Name)]
public sealed class TerrainBlockDetailsViewTests
{
    private static readonly Guid Key = Guid.Parse("a1000000-0000-4000-8000-000000000009");

    private sealed class Recorder : IEditStepRecorder
    {
        public readonly List<string> Log = new();
        public void Begin(EntityRepository repo, string label) => Log.Add("begin " + label);
        public void End(EntityRepository repo) => Log.Add("end");
        public void Cancel(EntityRepository repo) => Log.Add("cancel");
    }

    private static (EntityRepository World, Entity Block) World(byte fit = TerrainBlock.FitAll)
    {
        var w = new EntityRepository();
        w.RegisterComponent<SimTransform>(); w.RegisterComponent<NetworkIdentity>(); w.RegisterComponent<TerrainBlock>(); w.RegisterComponent<EntityGuid>(); w.RegisterComponent<SelectionState>();
        var e = w.CreateEntity();
        w.AddComponent(e, new SimTransform { Position = new Vector3(100, 100, 0), Rotation = Quaternion.Identity });
        w.AddComponent(e, new TerrainBlock { Recipe = new FixedString32("town"), Seed = 4471, Fit = fit });
        w.AddComponent(e, new EntityGuid(Key)); w.AddComponent(e, new NetworkIdentity(5)); w.AddComponent(e, new SelectionState());
        w.RegisterManagedComponent<LiveTerrainBakeInfo>();
        w.SetSingletonManaged(LiveTerrainBakeInfo.From(1, Enumerable.Range(0, 28).Select(i => new OverlayPart($"{Key}/{i}", 1, Vector2.Zero, Vector2.One))
            .Concat(new[] { new OverlayPart($"{Key}/w0", 2, Vector2.Zero, Vector2.One), new OverlayPart($"{Key}/w1", 2, Vector2.Zero, Vector2.One) }).ToList(), null));
        return (w, e);
    }

    private static IReadOnlyList<TerrainPick> Recipes() => new[]
    {
        new TerrainPick(TerrainPickKind.Block, "farmstead", "Farmstead", TerrainPickCatalog.Blocks, "Rural", "house, barns, yard", Array.Empty<(string, string)>()),
        new TerrainPick(TerrainPickKind.Block, "town", "Town block", TerrainPickCatalog.Blocks, "Urban", "3-6 storeys", Array.Empty<(string, string)>()),
    };

    private static DetailsContext Context(Entity e) => new(SelectionOrigin.Unknown, Array.Empty<IAssetSubSelection>(), new[] { e }, null, "Scenario", VariableRunState.Planning);

    /// <summary>The tab: offered for exactly one block (and no other entity), titled <i>Block</i>, its icon the city in orange.</summary>
    [Fact]
    public void U3_TheBlockTab_IsOfferedForOneBlock_TitledBlock_WithTheCityIconInOrange()
    {
        var (w, e) = World();
        var d = TerrainBlockDetailsViewDescriptor.For(() => w, Recipes);
        Assert.Equal("Block", d.Title);
        Assert.Contains(typeof(TerrainBlock), d.Requires);
        Assert.True(d.AppliesTo(Context(e)));
        Assert.Equal(Icons.Get("terrain/block"), DetailsWindow.TabIconOf("Block"));
        Assert.Equal(IconPalette.Orange, DetailsWindow.TabIconOf("Block")!.Value.Color);
    }

    [Fact]
    public void U3_Contents_ReadsTheLiveBake_AndTheFitBitsAreOneStepEach_AndASeedTypedIsOne()
    {
        var (w, e) = World();
        Assert.Equal("28 houses · 2 woods", TerrainBlockDetailsView.ContentsOf(w, e));
        var rec = new Recorder(); EditSteps.Install(w, rec);
        Assert.True(TerrainBlockOps.SetFit(w, e, TerrainBlock.AvoidWater, false));
        Assert.Equal((byte)(TerrainBlock.FitAll & ~TerrainBlock.AvoidWater), w.GetComponentRO<TerrainBlock>(e).Fit);
        Assert.Equal(2, rec.Log.Count);
        Assert.False(TerrainBlockOps.SetFit(w, e, TerrainBlock.AvoidWater, false));   // already off: no step
        Assert.Equal(2, rec.Log.Count);
        Assert.True(TerrainBlockOps.SetFit(w, e, TerrainBlock.AvoidWater, true));
        Assert.Equal(TerrainBlock.FitAll, w.GetComponentRO<TerrainBlock>(e).Fit);
        Assert.True(TerrainBlockOps.SetSeed(w, e, 777));
        Assert.Equal(777u, w.GetComponentRO<TerrainBlock>(e).Seed);
        Assert.False(TerrainBlockOps.SetSeed(w, e, 777));
        Assert.Equal(6, rec.Log.Count);
        // a block the bake has not held yet says so
        var (w2, e2) = World(); w2.SetSingletonManaged(LiveTerrainBakeInfo.Empty);
        Assert.Equal("not baked yet", TerrainBlockDetailsView.ContentsOf(w2, e2));
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

    /// <summary>
    /// The form in a REAL frame: the Recipe row carries the orange city icon, the Seed row a field and an amber dice (a click on it re-rolls in one step), the four Fit checkboxes sit in ONE column, the pads one is the disabled-frame
    /// (its click changes nothing), and the Contents line reads "28 houses · 2 woods". Shot: <c>u3-block-details.png</c> under <c>HROT_RAIL_SHOTS</c> (never committed).
    /// </summary>
    [SkippableFact]
    public void U3_TheForm_DrawsItsRows_TheIconsInColour_TheChecksInOneColumn_AndTheDiceRerollsInOneStep()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (w, e) = World();
        var rec = new Recorder(); EditSteps.Install(w, rec);
        var view = new TerrainBlockDetailsView(() => w, Recipes);
        using var frame = UiFrameHarness.Begin(420, 480);
        new EditorFontService().Initialize(1f, 1f);
        void Draw()
        {
            ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.SetNextWindowSize(new Vector2(400, 420));
            ImGui.Begin("Details — Town block##u3");
            view.Draw(Context(e), "rail");
            ImGui.End();
        }
        frame.StepN(6, Draw);
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) { Directory.CreateDirectory(dir); frame.Screenshot(Path.Combine(dir, "u3-block-details.png")); }
        var rects = new Dictionary<string, (Vector2 Min, Vector2 Max)>(view.Rects);
        foreach (var k in new[] { "recipe", "seed", "dice", "align", "water", "buildings", "pads" }) Assert.True(rects.ContainsKey(k), $"the form drew '{k}'");
        Assert.Equal("28 houses · 2 woods", view.ContentsText);

        var shot = Raylib.LoadImageFromScreen();
        try
        {
            var r = rects["recipe"];
            Assert.True(Count(shot, new Vector2(10, r.Min.Y - 2), new Vector2(r.Min.X, r.Max.Y + 2), IconPalette.Orange) >= 8, "the Recipe row carries the city icon in orange");
            var d = rects["dice"];
            Assert.True(Count(shot, d.Min, d.Max, IconPalette.Amber) >= 8, "the dice is amber");
            // alignment — recipe and seed fields in one column, the four checkboxes in one column
            Assert.True(Math.Abs(rects["recipe"].Min.X - rects["seed"].Min.X) < 40f, "the Recipe combo and the Seed field start near one value column");
            Assert.Single(new[] { "align", "water", "buildings", "pads" }.Select(k => (int)rects[k].Min.X).Distinct());
        }
        finally { Raylib.UnloadImage(shot); }

        // the dice re-rolls in ONE step; a greyed checkbox changes nothing
        rec.Log.Clear();
        uint before = w.GetComponentRO<TerrainBlock>(e).Seed;
        var dc = (rects["dice"].Min + rects["dice"].Max) * 0.5f;
        Click(frame, Draw, dc);
        Assert.NotEqual(before, w.GetComponentRO<TerrainBlock>(e).Seed);
        Assert.Equal(new[] { "begin Re-roll block", "end" }, rec.Log);
        rec.Log.Clear();
        byte fit = w.GetComponentRO<TerrainBlock>(e).Fit;
        Click(frame, Draw, (rects["pads"].Min + rects["pads"].Max) * 0.5f);
        Assert.Equal(fit, w.GetComponentRO<TerrainBlock>(e).Fit);   // the greyed one (pads, S-5)
        Assert.Empty(rec.Log);
        Click(frame, Draw, (rects["water"].Min + rects["water"].Max) * 0.5f);
        Assert.Equal((byte)(fit & ~TerrainBlock.AvoidWater), w.GetComponentRO<TerrainBlock>(e).Fit);
        // ⭐ T-5 — Align to the nearest road is LIVE: a click turns the AlignRoad bit off in ONE step, another turns it on. 🔴 Red-proof: that row drawn with enabled:false ⇒ the click changes nothing ⇒ red
        rec.Log.Clear();
        fit = w.GetComponentRO<TerrainBlock>(e).Fit;
        Assert.NotEqual(0, fit & TerrainBlock.AlignRoad);
        Click(frame, Draw, (rects["align"].Min + rects["align"].Max) * 0.5f);
        Assert.Equal((byte)(fit & ~TerrainBlock.AlignRoad), w.GetComponentRO<TerrainBlock>(e).Fit);
        Assert.Equal(new[] { "begin Do not fit block to the site", "end" }, rec.Log);
        Click(frame, Draw, (rects["align"].Min + rects["align"].Max) * 0.5f);
        Assert.Equal(fit, w.GetComponentRO<TerrainBlock>(e).Fit);
    }
}
