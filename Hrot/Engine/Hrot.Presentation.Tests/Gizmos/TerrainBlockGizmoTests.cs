using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Hrot.Common.Actions;
using Hrot.Common.Constants;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Services;
using Hrot.ScenarioEditor.Gizmos;
using Hrot.UI.Common.AddEntity;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos;

/// <summary>
/// ⭐ Terrain authoring U-3 (docs/DESIGN_Terrain_Authoring.md §15a; mockup 2) — a placed block on the map: the outline FOLLOWS what the live bake held (dashed, solid blue when selected), its chip, clickable on the outline; the block menu's
/// writes (Re-roll · Rotate · Change recipe) are ONE edit step each and deterministic; the menu is the block's own (Unpack live, T-4). ✅ Red-proofs (compiling mutations, one command): <c>TerrainBlockOps.Reroll</c> not bracketing ⇒ the
/// one-step rail red; the gizmo ignoring the bake info's bounds ⇒ the follow rail red.
/// </summary>
public sealed class TerrainBlockGizmoTests : IDisposable
{
    private readonly EntityRepository _repo = new();
    private static readonly Guid GuidA = Guid.Parse("a1000000-0000-4000-8000-000000000001");

    public TerrainBlockGizmoTests()
    {
        _repo.RegisterComponent<SimTransform>(); _repo.RegisterComponent<NetworkIdentity>(); _repo.RegisterComponent<TerrainBlock>();
        _repo.RegisterComponent<EntityGuid>(); _repo.RegisterComponent<SelectionState>();
    }

    public void Dispose() => _repo.Dispose();

    private Entity Block(string recipe = "hamlet", uint seed = 4471, int net = 77, bool selected = false)
    {
        var e = _repo.CreateEntity();
        _repo.AddComponent(e, new SimTransform { Position = new Vector3(100, 100, 0), Rotation = Quaternion.Identity });
        _repo.AddComponent(e, new TerrainBlock { Recipe = new Fdp.Core.FixedString32(recipe), Seed = seed, HeadingDeg = 0, Fit = TerrainBlock.FitAll });
        _repo.AddComponent(e, new EntityGuid(GuidA));
        _repo.AddComponent(e, new NetworkIdentity(net));
        _repo.AddComponent(e, new SelectionState { IsSelected = selected });
        return e;
    }

    private void Publish(float x0, float y0, float x1, float y1, int bakes = 1)
    {
        _repo.RegisterManagedComponent<LiveTerrainBakeInfo>();
        _repo.SetSingletonManaged(LiveTerrainBakeInfo.From(bakes, new[] { new OverlayPart(GuidA + "/0", 1, new Vector2(x0, y0), new Vector2(x1, y1)), new OverlayPart(GuidA + "/w0", 2, new Vector2(x0 + 1, y0 + 1), new Vector2(x1 - 1, y1 - 1)) }, null));
    }

    private static DebugPrimitive[] Lines(DebugPrimitiveBuffer b) => b.GetFrame().ToArray().Where(p => p.Shape == DebugPrimitiveShape.Line && !DebugPrimitiveBuffer.IsPickTarget(p)).ToArray();

    [Fact]
    public void U3_TheOutlineIsTheBakedExtent_DashedWhenIdle_SolidWhenSelected_WithItsChipAndAPickOutline()
    {
        var e = Block();
        var gizmo = new TerrainBlockGizmo();
        var none = new DebugPrimitiveBuffer();
        gizmo.Draw(_repo, e, none);
        Assert.Empty(none.GetFrame().ToArray());   // the bake has not held it: no outline yet

        Publish(80, 90, 140, 130);
        var buffer = new DebugPrimitiveBuffer();
        gizmo.Draw(_repo, e, buffer);
        var lines = Lines(buffer);
        Assert.Equal(4, lines.Length);
        Assert.All(lines, l => Assert.Equal(LineStyle.Dashed, l.LineStyle));
        Assert.Contains(lines, l => Math.Abs(l.LineStart.X - (80 - TerrainBlockGizmo.Margin)) < 0.01f || Math.Abs(l.LineEnd.X - (80 - TerrainBlockGizmo.Margin)) < 0.01f);
        Assert.Single(buffer.GetFrame().ToArray(), p => p.Shape == DebugPrimitiveShape.Text);   // the chip
        Assert.True(buffer.GetFrame().ToArray().Count(p => DebugPrimitiveBuffer.IsPickTarget(p)) >= 4, "the outline is clickable");

        _repo.SetComponent(e, new SelectionState { IsSelected = true });
        var sel = new DebugPrimitiveBuffer();
        gizmo.Draw(_repo, e, sel);
        Assert.All(Lines(sel), l => Assert.Equal(LineStyle.Solid, l.LineStyle));
        Assert.Equal("Hamlet", TerrainBlockGizmo.ChipText("hamlet", 4471, false));
        Assert.Equal("Town block · seed 4471", TerrainBlockGizmo.ChipText("town-block", 4471, true));
    }

    [Fact]
    public void U3_TheOutlineFollowsTheNextBake_AMovedBlockMovesItsOutline_ASteadyFrameAllocatesNothing()
    {
        var e = Block();
        var gizmo = new TerrainBlockGizmo();
        Publish(80, 90, 140, 130, bakes: 1);
        var first = new DebugPrimitiveBuffer(); gizmo.Draw(_repo, e, first);
        float x1 = Lines(first).Min(l => Math.Min(l.LineStart.X, l.LineEnd.X));

        Publish(280, 90, 340, 130, bakes: 2);   // the next bake held the block 200 m east
        var second = new DebugPrimitiveBuffer(); gizmo.Draw(_repo, e, second);
        float x2 = Lines(second).Min(l => Math.Min(l.LineStart.X, l.LineEnd.X));
        Assert.Equal(200f, x2 - x1, 0.01f);

        var buffer = new DebugPrimitiveBuffer();
        for (int k = 0; k < 3; k++) { buffer.Clear(); gizmo.Draw(_repo, e, buffer); }
        buffer.Clear();
        long before = GC.GetAllocatedBytesForCurrentThread();
        gizmo.Draw(_repo, e, buffer);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private sealed class Recorder : IEditStepRecorder
    {
        public readonly List<string> Log = new();
        public void Begin(EntityRepository repo, string label) => Log.Add("begin " + label);
        public void End(EntityRepository repo) => Log.Add("end");
        public void Cancel(EntityRepository repo) => Log.Add("cancel");
    }

    [Fact]
    public void U3_ReRoll_IsOneEditStep_DeterministicSoARedoIsTheSameLayout_AndRotateAndChangeRecipeAreSteps()
    {
        var rec = new Recorder();
        EditSteps.Install(_repo, rec);
        var e = Block(seed: 4471);
        // ⭐ through the menu's own action (the registry the editor fills), not the operation directly
        var actions = new Hrot.Common.Interactions.GlobalActionRegistry();
        new TerrainBlockActions(() => _repo, () => null, () => Array.Empty<TerrainPick>()).RegisterOn(actions);
        Assert.True(actions.TryGetHandler(GlobalActionIds.TerrainReroll, out var reroll));
        reroll(_repo, e);
        uint next = _repo.GetComponentRO<TerrainBlock>(e).Seed;
        Assert.Equal(BlockOperations.NextSeed(4471), next);   // ⭐ the ONE seed rule: the UI path lands in BlockOperations (🔴 red-proof: a seed rule of its own in the menu path)
        Assert.NotEqual(4471u, next);
        Assert.Equal(new[] { "begin Re-roll block", "end" }, rec.Log);   // ONE step

        Assert.True(TerrainBlockOps.Rotate(_repo, e));
        Assert.Equal(90f, _repo.GetComponentRO<TerrainBlock>(e).HeadingDeg);
        Assert.True(BlockOperations.ChangeRecipe(_repo, e, "farmstead").Done);
        Assert.Equal("farmstead", _repo.GetComponentRO<TerrainBlock>(e).Recipe.ToString());
        Assert.Equal(6, rec.Log.Count);
        Assert.False(BlockOperations.Reroll(_repo, _repo.CreateEntity()).Done);   // not a block: nothing written, no step
        Assert.Equal(6, rec.Log.Count);
    }

    [Fact]
    public void U3_TheBlockHasItsOwnMenu_ReRollRotateUnpack_ChangeRecipe_AndDelete()
    {
        var e = Block();
        string json = MapMenuActions.JsonFor(_repo, e);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var rows = doc.RootElement.EnumerateArray().Where(r => r.TryGetProperty("label", out _)).ToList();   // parsed: the ellipsis is escaped in the raw JSON
        string flat = string.Join("|", rows.Select(r => r.GetProperty("label").GetString())) + "|" + string.Join("|", rows.Where(r => r.TryGetProperty("tooltip", out _)).Select(r => r.GetProperty("tooltip").GetString()));
        foreach (var label in new[] { "Re-roll layout", "Rotate", "Unpack into houses and features", "Change recipe…", "Delete" }) Assert.Contains(label, flat);
        Assert.Contains(GlobalActionIds.TerrainReroll.ToString(), json);
        Assert.DoesNotContain("T-4", flat);   // Unpack is a live row now (backend's T-4 is wired): no "not built yet" tooltip
        // ⭐ the mockup's six rows, in its order, each with its icon key; the generic Select / Center View / Properties rows are not in the block's menu
        var labels = rows.Select(r => r.GetProperty("label").GetString()).ToList();
        Assert.Equal(new[] { "Re-roll layout", "Rotate", "Move", "Unpack into houses and features", "Change recipe…", "Delete" }, labels);
        Assert.Equal(new[] { "terrain/reroll", "terrain/rotate", "terrain/move", "terrain/unpack", "terrain/block", "terrain/delete" },
                     rows.Select(r => r.GetProperty("icon").GetString()).ToArray());
        Assert.False(rows[2].GetProperty("enabled").GetBoolean());   // Move is the drag: an informational row
        // a unit's menu is not the block's
        Assert.DoesNotContain("Re-roll layout", MapMenuActions.JsonFor(_repo, Plain()));
    }

    private Entity Plain() { var e = _repo.CreateEntity(); _repo.AddComponent(e, new SimTransform { Position = new Vector3(1, 1, 0), Rotation = Quaternion.Identity }); return e; }
}
