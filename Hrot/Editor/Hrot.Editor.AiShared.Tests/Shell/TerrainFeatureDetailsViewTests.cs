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
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Common.Components;
using Hrot.UI.Common.AddEntity;
using ImGuiNET;
using Raylib_cs;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐⭐ CE-5010 (docs/DESIGN_Terrain_Editor_UX.md §5 TE-8; mockup <c>docs/UX/terrain-editor.svg</c> panel E) — the ONE <b>Terrain</b> Details tab for every non-ground <see cref="TerrainFeature"/>: offered for exactly one such feature (not a ground shape, not two selected);
/// a Kind change is ONE undo step and the expander sees it, and a kind never leaves its shape; Height 0 shows the kind's default; each other row writes through <see cref="TerrainFeatureOps"/>; a real frame has the mockup's rows. (Beside <c>GroundFeatureDetailsViewTests</c>.)
/// </summary>
[Collection(Hrot.Editor.AiShared.Tests.PanelSnapshotTestCollection.Name)]
public sealed class TerrainFeatureDetailsViewTests
{
    private sealed class Recorder : IEditStepRecorder
    {
        public readonly List<string> Log = new();
        public void Begin(EntityRepository repo, string label) => Log.Add("begin " + label);
        public void End(EntityRepository repo) => Log.Add("end");
        public void Cancel(EntityRepository repo) => Log.Add("cancel");
    }

    private static EntityRepository World()
    {
        var w = new EntityRepository();
        TerrainLock.SetOpen(w, true);
        w.RegisterComponent<SimTransform>(); w.RegisterComponent<TerrainFeature>(); w.RegisterComponent<EntityInfo>();
        w.RegisterManagedComponent<EditablePolyline>();
        return w;
    }

    private static Entity Feature(EntityRepository w, byte kind, string template, float pad, uint seed, string name, Vector2 at, params Vector2[] relative)
    {
        var e = w.CreateEntity();
        w.AddComponent(e, new SimTransform { Position = new Vector3(at, 0f), Rotation = Quaternion.Identity });
        w.AddComponent(e, new TerrainFeature { Kind = kind, Template = new FixedString32(template), Pad = pad, Seed = seed });
        w.AddComponent(e, new EntityInfo { Name = new FixedString64(name) });
        if (relative.Length > 0) w.SetManagedComponent(e, new EditablePolyline { Points = relative, Version = 1 });
        return e;
    }

    private static Entity Wall(EntityRepository w, byte kind = TerrainFeature.Wall, float pad = 0f)
        => Feature(w, kind, "", pad, 0, TerrainFeatureOps.KindName(kind), new Vector2(100, 100), new Vector2(0, 0), new Vector2(160, 0), new Vector2(160, 160));

    private static DetailsContext Context(params Entity[] es) => new(SelectionOrigin.Unknown, Array.Empty<IAssetSubSelection>(), es, null, "Scenario", VariableRunState.Planning);

    private static IReadOnlyList<(string, string, float)> Recipes() => new[] { ("street-clutter", "Street clutter", 1.5f), ("undergrowth", "Undergrowth", 2f) };

    private static IReadOnlyList<Vector2> WorldPoints(EntityRepository w, Entity e) => TerrainFeatureDetailsView.PointsOf(w, e);

    private static string FirstMaterialAfter(EntityRepository w, Entity e)
    {
        var f = w.GetComponentRO<TerrainFeature>(e);
        var parts = FeatureExpander.OfKind(f.Kind, "k", f.Template.ToString() ?? "", f.HeadingDeg, f.Pad, f.Seed, Vector2.Zero, WorldPoints(w, e),
            new TerrainWorld { Name = "t", BoundsMin = new Vector2(-1000), BoundsMax = new Vector2(1000) }, null!, Array.Empty<string?>());
        return (string)((System.Text.Json.Nodes.JsonObject)parts[0]["properties"]!)["material"]!;
    }

    /// <summary>
    /// The tab is offered for exactly ONE non-ground feature: a wall, a row, a lone tree, yes; a ground shape (its own tab), two selected, nothing, or a non-feature, no. Titled <i>Terrain</i>. 🔴 Red-proof: <c>IsEditable</c> not excluding Ground ⇒ the ground assert fails.
    /// </summary>
    [Fact]
    public void TheTerrainTab_IsOfferedForOneNonGroundFeature_OrSeveralOfOneFamily_NotForGround()
    {
        var w = World();
        var wall = Wall(w); var wall2 = Wall(w, TerrainFeature.Fence);
        var ground = Feature(w, TerrainFeature.Ground, "raise", 2f, 100, "Raised ground", Vector2.Zero, new Vector2(-5, -5), new Vector2(5, -5), new Vector2(5, 5));
        var plain = w.CreateEntity();
        var d = TerrainFeatureDetailsViewDescriptor.For(() => w, () => Recipes());
        Assert.Equal("Terrain", d.Title);
        Assert.True(d.AppliesTo(Context(wall)));
        Assert.False(d.AppliesTo(Context(ground)));
        Assert.True(d.AppliesTo(Context(wall, wall2)));                                // ⭐ CE-5017 — several of ONE family (a wall and a fence) are edited together
        var wood = Feature(w, TerrainFeature.Wood, "", 0f, 3, "Wood", Vector2.Zero, new Vector2(0, 0), new Vector2(50, 0), new Vector2(50, 50));
        Assert.False(d.AppliesTo(Context(wall, wood)));                                 // … never across shapes
        Assert.False(d.AppliesTo(Context(wall, ground)));
        Assert.False(d.AppliesTo(Context()));
        Assert.False(d.AppliesTo(Context(plain)));
        Assert.True(GroundFeatureDetailsViewDescriptor.For(() => w, () => null).AppliesTo(Context(ground)));   // the ground shape keeps its own tab
    }

    /// <summary>
    /// A Kind change is ONE undo step, the expander sees the new kind (a wall → a hedge: the <c>hedge</c> material), the material and height go back to the new kind's defaults, an author's name stays; and a kind NEVER leaves its shape: a wall's list is Wall · Fence · Hedge,
    /// a wood's Wood · Scrub, a tree Row / Group / point has none — and switching to another shape's kind is refused. 🔴 Red-proof: <c>SetKind</c> without the family check ⇒ the "refused" asserts fail.
    /// </summary>
    [Fact]
    public void AKindChange_IsOneStep_TheExpanderSeesIt_AndNeverLeavesTheShape()
    {
        var w = World();
        var wall = Wall(w, TerrainFeature.Wall, pad: 3f);
        var rec = new Recorder(); EditSteps.Install(w, rec);
        Assert.Equal("concrete", FirstMaterialAfter(w, wall));

        Assert.True(TerrainFeatureOps.SetKind(w, wall, TerrainFeature.Hedge));
        Assert.Equal(new[] { "begin Change terrain kind", "end" }, rec.Log);                  // ONE step
        var f = w.GetComponentRO<TerrainFeature>(wall);
        Assert.Equal((TerrainFeature.Hedge, 0f), (f.Kind, f.Pad));                              // the new kind's default height
        Assert.Equal("hedge", FirstMaterialAfter(w, wall));                                     // the expander sees a hedge
        Assert.Equal("Hedge", w.GetComponentRO<EntityInfo>(wall).Name.ToString());               // the default name followed
        Assert.False(TerrainFeatureOps.SetKind(w, wall, TerrainFeature.Hedge));                // unchanged: no step
        Assert.Equal(2, rec.Log.Count);

        // never another shape
        foreach (var other in new[] { TerrainFeature.Wood, TerrainFeature.Tree, TerrainFeature.Row, TerrainFeature.Ground, TerrainFeature.Water }) Assert.False(TerrainFeatureOps.SetKind(w, wall, other), "a line never becomes " + other);
        Assert.Equal(TerrainFeature.Hedge, w.GetComponentRO<TerrainFeature>(wall).Kind);
        Assert.Equal(new byte[] { TerrainFeature.Wall, TerrainFeature.Fence, TerrainFeature.Hedge }, TerrainFeatureOps.FamilyOf(TerrainFeature.Fence));
        Assert.Equal(new byte[] { TerrainFeature.Wood, TerrainFeature.Scrub }, TerrainFeatureOps.FamilyOf(TerrainFeature.Scrub));
        Assert.All(new[] { TerrainFeature.Row, TerrainFeature.Group, TerrainFeature.Tree, TerrainFeature.Clutter, TerrainFeature.Scatter, TerrainFeature.Water, TerrainFeature.Bridge }, k => Assert.Null(TerrainFeatureOps.FamilyOf(k)));
        var wood = Feature(w, TerrainFeature.Wood, "", 0f, 7, "Wood", Vector2.Zero, new Vector2(0, 0), new Vector2(30, 0), new Vector2(30, 30));
        Assert.True(TerrainFeatureOps.SetKind(w, wood, TerrainFeature.Scrub));
        Assert.False(TerrainFeatureOps.SetKind(w, wood, TerrainFeature.Hedge));                // an area never becomes a line
    }

    /// <summary>
    /// Height 0 SHOWS the kind's default with its hint ("the kind's default: 2.0 m" for a wall, 1.6 fence, 1.5 hedge) and a set height shows itself with no hint; the other writes — material (a library name; unknown refused), height range, a wood's density, a row's spacing, a group's count (a whole number),
    /// a scatter's recipe, the seed and the dice, a tree's heading — are one step each and refused out of range / for a kind that has none. 🔴 Red-proof: <c>PadShown</c> returning the stored 0 ⇒ the default assert fails.
    /// </summary>
    [Fact]
    public void HeightZeroShowsTheDefault_AndEachWriteIsOneStep()
    {
        var w = World();
        var wall = Wall(w); var fence = Wall(w, TerrainFeature.Fence); var hedge = Wall(w, TerrainFeature.Hedge); var tall = Wall(w, TerrainFeature.Wall, 3f);
        Assert.Equal(2.0f, TerrainFeatureOps.PadShown(w.GetComponentRO<TerrainFeature>(wall)));
        Assert.Equal(1.6f, TerrainFeatureOps.PadShown(w.GetComponentRO<TerrainFeature>(fence)));
        Assert.Equal(1.5f, TerrainFeatureOps.PadShown(w.GetComponentRO<TerrainFeature>(hedge)));
        Assert.Equal("the kind's default: 2.0 m", TerrainFeatureDetailsView.HeightHintOf(w.GetComponentRO<TerrainFeature>(wall)));
        Assert.Equal("", TerrainFeatureDetailsView.HeightHintOf(w.GetComponentRO<TerrainFeature>(tall)));
        Assert.Equal(3f, TerrainFeatureOps.PadShown(w.GetComponentRO<TerrainFeature>(tall)));

        var rec = new Recorder(); EditSteps.Install(w, rec);
        Assert.True(TerrainFeatureOps.SetMaterial(w, wall, "brick")); Assert.Equal("brick", w.GetComponentRO<TerrainFeature>(wall).Template.ToString());
        Assert.False(TerrainFeatureOps.SetMaterial(w, wall, "no-such-material"));
        Assert.True(TerrainFeatureOps.SetPad(w, wall, 2.5f)); Assert.False(TerrainFeatureOps.SetPad(w, wall, 80f)); Assert.False(TerrainFeatureOps.SetPad(w, wall, 2.5f));
        Assert.Equal(4, rec.Log.Count);

        var line = new[] { new Vector2(0, 0), new Vector2(60, 0) };
        var row = Feature(w, TerrainFeature.Row, "tree", 0f, 0, "Tree row", new Vector2(0, 50), line);
        var group = Feature(w, TerrainFeature.Group, "tree", 0f, 4471, "Tree group", new Vector2(0, 80), new Vector2(0, 0), new Vector2(30, 0), new Vector2(30, 30));
        var scatter = Feature(w, TerrainFeature.Scatter, "street-clutter", 0f, 9, "Scatter", new Vector2(0, 120), new Vector2(0, 0), new Vector2(30, 0), new Vector2(30, 30));
        var wood = Feature(w, TerrainFeature.Wood, "", 0f, 3, "Wood", new Vector2(0, 160), new Vector2(0, 0), new Vector2(30, 0), new Vector2(30, 30));
        var tree = Feature(w, TerrainFeature.Tree, "", 0f, 0, "Tree", new Vector2(5, 5));
        Assert.Equal(8f, TerrainFeatureOps.PadShown(w.GetComponentRO<TerrainFeature>(row))); Assert.Equal(10f, TerrainFeatureOps.PadShown(w.GetComponentRO<TerrainFeature>(group)));
        Assert.True(TerrainFeatureOps.SetPad(w, row, 5f)); Assert.False(TerrainFeatureOps.SetPad(w, row, 0.1f));
        Assert.True(TerrainFeatureOps.SetPad(w, group, 12f)); Assert.False(TerrainFeatureOps.SetPad(w, group, 12.5f));    // a count is a whole number
        Assert.True(TerrainFeatureOps.SetPad(w, wood, 250f)); Assert.False(TerrainFeatureOps.SetPad(w, tree, 3f));        // a lone tree has no Pad
        Assert.True(TerrainFeatureOps.SetRecipe(w, scatter, "undergrowth")); Assert.False(TerrainFeatureOps.SetRecipe(w, wall, "undergrowth"));
        Assert.True(TerrainFeatureOps.SetSeed(w, group, 123u)); Assert.False(TerrainFeatureOps.SetSeed(w, row, 5u));      // a row has no seed
        uint before = w.GetComponentRO<TerrainFeature>(scatter).Seed;
        Assert.True(TerrainFeatureOps.Reroll(w, scatter)); Assert.NotEqual(before, w.GetComponentRO<TerrainFeature>(scatter).Seed);
        Assert.True(TerrainFeatureOps.SetHeading(w, tree, -90f)); Assert.Equal(270f, w.GetComponentRO<TerrainFeature>(tree).HeadingDeg);
        Assert.False(TerrainFeatureOps.SetHeading(w, wall, 10f));                                                          // a wall has no heading
        Assert.Equal(5f, w.GetComponentRO<TerrainFeature>(row).Pad); Assert.Equal(12f, w.GetComponentRO<TerrainFeature>(group).Pad);

        // the Contents line: length / area + what it does
        Assert.Equal("320 m · stops every vehicle; a heavy tank breaks it (B-3); blasts breach it (B-2)", TerrainFeatureDetailsView.ContentsOf(w, wall));
        Assert.Equal("12 lone trees — they fell, burn and block like any lone tree", TerrainFeatureDetailsView.ContentsOf(w, row));   // 60 m at 5 m spacing: a tree at 2.5, 7.5 … 57.5
        Assert.Equal("12 lone trees — they fell, burn and block like any lone tree", TerrainFeatureDetailsView.ContentsOf(w, group));
        Assert.Contains("blocks sight inside", TerrainFeatureDetailsView.ContentsOf(w, wood));
    }

    /// <summary>
    /// ⭐ CE-5009 (TE-7) — a CLOSED wall / fence / hedge (its first point repeated at the end) adds the Contents line "a closed wall — Edit Shape, right-click a side ▸ Add gate here (3 m)"; an open one, and every other kind, adds nothing; shown in a real frame under <c>xvfb-run -a</c>.
    /// 🔴 Red-proof: <c>GateLineOf</c> always empty ⇒ red.
    /// </summary>
    [Fact]
    public void AClosedWall_SaysHowToAddAGate_AnOpenOneAndOtherKindsDoNot()
    {
        var w = World();
        var closed = Feature(w, TerrainFeature.Wall, "", 0f, 0, "Wall", new Vector2(0, 0), new Vector2(0, 0), new Vector2(20, 0), new Vector2(20, 20), new Vector2(0, 20), new Vector2(0, 0));
        var hedge = Feature(w, TerrainFeature.Hedge, "", 0f, 0, "Hedge", new Vector2(0, 0), new Vector2(0, 0), new Vector2(20, 0), new Vector2(20, 20), new Vector2(0, 20), new Vector2(0, 0));
        var open = Wall(w);
        var wood = Feature(w, TerrainFeature.Wood, "", 0f, 3, "Wood", Vector2.Zero, new Vector2(0, 0), new Vector2(30, 0), new Vector2(30, 30), new Vector2(0, 0));
        Assert.Equal("a closed wall — Edit Shape, right-click a side ▸ Add gate here (3 m)", TerrainFeatureDetailsView.GateLineOf(w, closed));
        Assert.Equal("a closed wall — Edit Shape, right-click a side ▸ Add gate here (3 m)", TerrainFeatureDetailsView.GateLineOf(w, hedge));
        Assert.Equal("", TerrainFeatureDetailsView.GateLineOf(w, open));
        Assert.Equal("", TerrainFeatureDetailsView.GateLineOf(w, wood));                         // a closed ring of another kind: no gate
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

    /// <summary>
    /// The form in a REAL frame (Xvfb; a skip is not a pass), as mockup E: <i>Kind</i> (Wall · Fence · Hedge in one row), <i>Material</i>, <i>Height</i> beside "the kind's default: 2.0 m", the <i>Contents</i> section with the wall's sentence; a click on <i>Hedge</i> is one undo step;
    /// a Row shows Spacing, a Group Count + Seed + dice, a lone tree Heading — and none of them a Kind row. 🔴 Mutation: the Kind button not calling <c>SetKind</c> ⇒ the click assert fails. Shot: <c>ce5010-terrain-tab.png</c> under <c>HROT_RAIL_SHOTS</c>.
    /// </summary>
    [SkippableFact]
    public void TheForm_HasTheMockupsRows_AClickOnHedgeIsOneStep_RowGroupAndTreeShowTheirOwnRows()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var w = World();
        var wall = Wall(w);
        var rec = new Recorder(); EditSteps.Install(w, rec);
        var view = new TerrainFeatureDetailsView(() => w, () => Recipes());
        using var frame = UiFrameHarness.Begin(600, 420);
        new EditorFontService().Initialize(1f, 1f);
        Entity shown = wall;
        void Draw()
        {
            ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.SetNextWindowSize(new Vector2(560, 260));
            ImGui.Begin("Details — Wall (3)##ce5010");
            view.Draw(Context(shown), "rail");
            ImGui.End();
        }
        frame.StepN(6, Draw);
        string? shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS");
        if (shots is { Length: > 0 }) { Directory.CreateDirectory(shots); frame.Screenshot(Path.Combine(shots, "ce5010-terrain-tab.png")); }

        var r = new Dictionary<string, (Vector2 Min, Vector2 Max)>(view.Rects);
        Assert.Single(new[] { "kind:Wall", "kind:Fence", "kind:Hedge" }.Select(k => (int)r[k].Min.Y).Distinct());
        Assert.True(r["kind:Wall"].Max.X <= r["kind:Fence"].Min.X && r["kind:Fence"].Max.X <= r["kind:Hedge"].Min.X, "Wall · Fence · Hedge, in the mockup's order");
        Assert.True(r["material"].Min.Y > r["kind:Wall"].Max.Y && r["height"].Min.Y > r["material"].Max.Y && r["contents"].Min.Y > r["height"].Max.Y, "Kind, Material, Height, then Contents");
        Assert.Equal(r["material"].Min.X, r["height"].Min.X, 2f);   // one value column
        Assert.Equal("the kind's default: 2.0 m", view.HeightHint);
        Assert.Equal("320 m · stops every vehicle; a heavy tank breaks it (B-3); blasts breach it (B-2)", view.ContentsText);

        Click(frame, Draw, (r["kind:Hedge"].Min + r["kind:Hedge"].Max) * 0.5f);
        Assert.Equal(TerrainFeature.Hedge, w.GetComponentRO<TerrainFeature>(wall).Kind);
        Assert.Equal(2, rec.Log.Count);

        var closedWall = Feature(w, TerrainFeature.Wall, "", 0f, 0, "Wall", new Vector2(0, 0), new Vector2(0, 0), new Vector2(20, 0), new Vector2(20, 20), new Vector2(0, 20), new Vector2(0, 0));
        shown = closedWall; frame.StepN(5, Draw);
        Assert.Equal("a closed wall — Edit Shape, right-click a side ▸ Add gate here (3 m)", view.GateText);        // ⭐ CE-5009 (TE-7)
        Assert.True(view.Rects["gate"].Min.Y > view.Rects["contents"].Max.Y, "the gate line is under the Contents line");
        shown = wall; frame.StepN(3, Draw);

        var row = Feature(w, TerrainFeature.Row, "tree", 0f, 0, "Tree row", new Vector2(0, 50), new Vector2(0, 0), new Vector2(60, 0));
        shown = row; frame.StepN(5, Draw);
        Assert.True(view.Rects.ContainsKey("spacing")); Assert.DoesNotContain(view.Rects.Keys, k => k.StartsWith("kind:")); Assert.False(view.Rects.ContainsKey("seed"));
        var group = Feature(w, TerrainFeature.Group, "tree", 0f, 4471, "Tree group", new Vector2(0, 80), new Vector2(0, 0), new Vector2(30, 0), new Vector2(30, 30));
        shown = group; frame.StepN(5, Draw);
        Assert.True(view.Rects.ContainsKey("count") && view.Rects.ContainsKey("seed") && view.Rects.ContainsKey("dice"));
        var tree = Feature(w, TerrainFeature.Tree, "", 0f, 0, "Tree", new Vector2(5, 5));
        shown = tree; frame.StepN(5, Draw);
        Assert.True(view.Rects.ContainsKey("heading")); Assert.False(view.Rects.ContainsKey("height"));
    }

    /// <summary>
    /// ⭐⭐ CE-5030 BE-4 part 2 (📄 docs/DESIGN_Terrain_Base_Edits.md §5; mockup <c>docs/UX/terrain-base-edits.svg</c> "Details — Building edit") — a REMOVE edit's tab in a REAL frame: the
    /// <i>Edit</i> section with Action · Building · Ground · Goes with it (the mockup's order, one value column), then <b>Restore building</b> and <b>Show on map</b> on one row; no Kind row.
    /// A click on Restore deletes the edit (the building comes back with the next bake). 🔴 Mutation: Restore not deleting ⇒ the alive assert fails. Shot: <c>ce5030-edit-details.png</c>.
    /// </summary>
    [SkippableFact]
    public void CE5030_ARemoveEdit_ShowsTheMockupsRows_AndRestoreDeletesIt()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var w = World();
        w.RegisterManagedComponent<TerrainWorld>();
        w.SetSingletonManaged(new TerrainWorld { Removed = new[] { new TerrainRemovedBuilding("way/48211211", new[] { new Vector2(0, 0), new Vector2(5, 0), new Vector2(5, 4.4f), new Vector2(0, 4.4f) }, 312.4f, 2) } });
        var edit = Feature(w, TerrainFeature.Edit, "remove", 0f, 0, "Building edit 3", new Vector2(2, 2));
        var f = w.GetComponentRO<TerrainFeature>(edit); f.Target = new FixedString64("way/48211211"); w.SetComponent(edit, f);
        var view = new TerrainFeatureDetailsView(() => w, () => Recipes());
        using var frame = UiFrameHarness.Begin(600, 300);
        new EditorFontService().Initialize(1f, 1f);
        void Draw()
        {
            ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.SetNextWindowSize(new Vector2(460, 220));
            ImGui.Begin("Details — Building edit 3##ce5030");
            if (w.IsAlive(edit)) view.Draw(Context(edit), "rail");
            ImGui.End();
        }
        frame.StepN(6, Draw);
        string? shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS");
        if (shots is { Length: > 0 }) { Directory.CreateDirectory(shots); frame.Screenshot(Path.Combine(shots, "ce5030-edit-details.png")); }

        Assert.Equal(new TerrainFeatureOps.EditFacts("Remove building", "way/48211211  (baked)", "levelled to 312.4 m", "2 doors · its damage"), view.Facts);
        var r = new Dictionary<string, (Vector2 Min, Vector2 Max)>(view.Rects);
        Assert.True(r["action"].Max.Y <= r["building"].Min.Y && r["building"].Max.Y <= r["ground"].Min.Y && r["ground"].Max.Y <= r["goes"].Min.Y && r["goes"].Max.Y <= r["restore"].Min.Y,
            "Action, Building, Ground, Goes with it, then the buttons");
        Assert.Single(new[] { "action", "building", "ground", "goes" }.Select(k => (int)r[k].Min.X).Distinct());   // one value column
        Assert.Equal((int)r["restore"].Min.Y, (int)r["show"].Min.Y);                                                // Restore · Show on map on one row
        Assert.DoesNotContain(r.Keys, k => k.StartsWith("kind:"));

        Click(frame, Draw, (r["restore"].Min + r["restore"].Max) * 0.5f);
        Assert.False(w.IsAlive(edit));
    }

    /// <summary>
    /// ⭐ CE-5017 (docs/DESIGN_Terrain_Editor_UX.md §5 TE-9) — a change over SEVERAL selected features is ONE undo step: three walls set to Hedge, then their height, each as one bracket
    /// (the per-entity steps nest inside); a feature of another family is not taken together. 🔴 Red-proof: <c>Apply</c> without its bracket ⇒ three "begin" per change.
    /// </summary>
    [Fact]
    public void CE5017_SeveralWalls_ChangeKindAndHeightTogether_AsOneUndoStepEach()
    {
        var w = World();
        var log = new Recorder();
        EditSteps.Install(w, log);
        var walls = new[] { Wall(w), Wall(w, TerrainFeature.Fence), Wall(w) };
        Assert.Same(TerrainFeatureOps.WallKinds, TerrainFeatureOps.CommonFamily(w, walls));
        Assert.Equal(3, TerrainFeatureOps.Apply(w, walls, "Change terrain kind", (wr, e) => TerrainFeatureOps.SetKind(wr, e, TerrainFeature.Hedge)));
        Assert.All(walls, e => Assert.Equal(TerrainFeature.Hedge, w.GetComponentRO<TerrainFeature>(e).Kind));
        Assert.Equal(3, TerrainFeatureOps.Apply(w, walls, "Set height", (wr, e) => TerrainFeatureOps.SetPad(wr, e, 2.5f)));
        Assert.All(walls, e => Assert.Equal(2.5f, w.GetComponentRO<TerrainFeature>(e).Pad));
        // the recorder sees the nesting; WorldUndoRecorder makes the OUTER pair the one step
        Assert.Equal("begin Change terrain kind", log.Log[0]);
        Assert.Equal("end", log.Log[7]);
        Assert.Equal("begin Set height", log.Log[8]);
        Assert.Equal(16, log.Log.Count);
        var wood = Feature(w, TerrainFeature.Wood, "", 0f, 3, "Wood", Vector2.Zero, new Vector2(0, 0), new Vector2(50, 0), new Vector2(50, 50));
        Assert.Null(TerrainFeatureOps.CommonFamily(w, new[] { walls[0], wood }));
    }
}
