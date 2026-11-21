using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replay;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common;
using Hrot.Presentation.Edits;
using Hrot.UI.Common.AddEntity;
using Xunit;

namespace Hrot.Presentation.Tests.AddEntity;

/// <summary>
/// ⭐⭐ CE-5008 (docs/DESIGN_Terrain_Editor_UX.md §5 TE-5, R-4264, R-5041; mockup D) — the OBSTACLE ROW: a drawn line becomes N obstacle ENTITIES of the chosen type at the shared line places, all in one <see cref="PlacementGroup"/>, created inside ONE edit
/// step; the road-blockage routine sees N footprints. (The feature's suite; the tree Row / Group live in <c>FeatureKindsTests</c> and <c>TerrainAuthoringPickerTests</c>, the click that selects a group in <c>SelectionInteractionSystemTests</c>.)
/// </summary>
public sealed class ObstacleRowTests
{
    private static readonly Vector2[] Line20 = { new(0, 0), new(20, 0) };

    /// <summary>
    /// A 20 m line, concrete blocks (2 m long), a 1.5 m gap, facing ALONG: a block every 3.5 m (the first half a spacing in) = 6 entities of the concrete-block TKB type, headings along the line, ONE group id on all. 🔴 Red-proof: spacing = the length only
    /// (ignore the gap) ⇒ the count and positions change ⇒ red.
    /// </summary>
    [Fact]
    public void ARowAlong_PlacesOneBlockEvery3_5m_AllInOneGroup()
    {
        var row = new ObstacleRowOptions();
        Assert.Equal(TkbEntityTypes.Obstacle_ConcreteBlock, row.TkbType);
        Assert.Equal(1.5f, row.Gap); Assert.Equal(RowFacing.Along, row.Facing);
        Assert.Equal(3.5f, row.Spacing, 3);
        Assert.Equal(6, row.CountOf(Line20));

        var cmds = row.Commands(Line20, 0f, 77u);
        Assert.Equal(6, cmds.Count);
        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(TkbEntityTypes.Obstacle_ConcreteBlock, (long)cmds[i].TkbType);
            var t = cmds[i].InitialTransform!.Value;
            Assert.Equal(1.75f + 3.5f * i, t.Position.X, 3); Assert.Equal(0f, t.Position.Y, 3);
            Assert.Equal(0f, YawDeg(t.Rotation), 2);                                       // the length lies along the line
            Assert.Equal(77u, Assert.Single(cmds[i].InitialComponents!.OfType<PlacementGroup>()).Id);
        }
        Assert.Equal("6 concrete blocks · 1.5 m gaps · Enter ends", row.Summary(Line20));
    }

    /// <summary>Facing ACROSS turns every obstacle 90° and spaces by the WIDTH (1 m) + the gap = 2.5 m ⇒ 8 of them; gap 0 = end to end (the length alone). 🔴 Red-proof: no +90° ⇒ the yaw assert fails.</summary>
    [Fact]
    public void ARowAcross_TurnsEachObstacle90Degrees_AndSpacesByTheWidth()
    {
        var row = new ObstacleRowOptions { Facing = RowFacing.Across };
        Assert.Equal(2.5f, row.Spacing, 3);
        var cmds = row.Commands(Line20, 0f, 5u);
        Assert.Equal(8, cmds.Count);
        Assert.All(cmds, c => Assert.Equal(90f, YawDeg(c.InitialTransform!.Value.Rotation), 2));

        var tight = new ObstacleRowOptions { Gap = 0f };
        Assert.Equal(2f, tight.Spacing, 3);                  // end to end: one block's length
        Assert.Equal(10, tight.CountOf(Line20));
        var sandbags = new ObstacleRowOptions(); sandbags.SetType(TkbEntityTypes.Obstacle_SandbagWall);
        Assert.Equal(TkbEntityTypes.Obstacle_SandbagWall, sandbags.TkbType);
        Assert.Equal(4.5f, sandbags.Spacing, 3);             // 3.0 m + the gap
    }

    /// <summary>A bent line turns the headings with it (ALONG follows each segment) — the same routine as the tree Row. 🔴 Red-proof: use the first segment's direction for all ⇒ red.</summary>
    [Fact]
    public void ABentLine_TurnsTheHeadingsWithTheSegments()
    {
        var row = new ObstacleRowOptions { Gap = 0f };
        var cmds = row.Commands(new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10) }, 0f, 1u);
        Assert.Equal(0f, YawDeg(cmds[0].InitialTransform!.Value.Rotation), 2);
        Assert.Equal(90f, YawDeg(cmds[^1].InitialTransform!.Value.Rotation), 2);
    }

    /// <summary>
    /// ⭐ ONE undo step: the whole row is sent inside one <c>EditSteps</c> bracket, so the history holds ONE step "Row of obstacles"; undo removes all N entities, redo brings them back. 🔴 Red-proof: no bracket round the loop ⇒ no step is recorded
    /// (nothing to undo) ⇒ red.
    /// </summary>
    [Fact]
    public void TheRow_IsOneUndoStep_UndoRemovesEveryMember()
    {
        using var world = new EntityRepository();
        HrotSharedComponentRegistry.RegisterAll(world);
        world.Tick();
        var history = new EditHistory();
        EditSteps.Install(world, new WorldUndoRecorder(history, new WorldSnapshotRing(), () => true));
        int Members() { int n = 0; foreach (var _ in world.Query().With<PlacementGroup>().Build()) n++; return n; }

        var row = new ObstacleRowOptions();
        var cmds = row.Commands(Line20, 0f, PlacementGroup.NewId());
        ObstacleRowOptions.Place(world, cmds, c =>
        {
            var e = world.CreateEntity();
            world.AddComponent(e, c.InitialTransform!.Value);
            world.AddComponent(e, (PlacementGroup)c.InitialComponents![0]);
        });

        Assert.Equal(6, Members());
        Assert.Equal(1, history.Count);
        Assert.Equal(ObstacleRowOptions.StepLabel, history.UndoLabel);
        Assert.True(history.Undo());
        Assert.Equal(0, Members());
        Assert.True(history.Redo());
        Assert.Equal(6, Members());
    }

    /// <summary>A send that throws cancels the step (nothing half-recorded) and the exception still reaches the caller.</summary>
    [Fact]
    public void AFailingSend_CancelsTheStep()
    {
        using var world = new EntityRepository();
        HrotSharedComponentRegistry.RegisterAll(world);
        var history = new EditHistory();
        EditSteps.Install(world, new WorldUndoRecorder(history, new WorldSnapshotRing(), () => true));
        Assert.Throws<System.InvalidOperationException>(() => ObstacleRowOptions.Place(world, new ObstacleRowOptions().Commands(Line20, 0f, 1u), _ => throw new System.InvalidOperationException("x")));
        Assert.Equal(0, history.Count);
    }

    /// <summary>
    /// The road sees the row: built into the world the way a creation would (transform + the TKB's static obstacle marker and box), <c>RoadBlockage.Blockers</c> returns N footprints, each centred on its place and as long as the box — along the line,
    /// or across it. 🔴 Red-proof: the Across row without its 90° ⇒ the footprints stay long in x ⇒ red.
    /// </summary>
    [Theory]
    [InlineData(RowFacing.Along, 2f, 1f)]
    [InlineData(RowFacing.Across, 1f, 2f)]
    public void TheRoadSeesNFootprints(RowFacing facing, float extentX, float extentY)
    {
        using var world = new EntityRepository();
        HrotSharedComponentRegistry.RegisterAll(world);
        var row = new ObstacleRowOptions { Facing = facing };
        var cmds = row.Commands(Line20, 0f, 3u);
        var box = row.TypeInfo.Box;
        foreach (var c in cmds)
        {
            var e = world.CreateEntity();
            world.AddComponent(e, c.InitialTransform!.Value);
            world.AddComponent(e, new StaticObstacle { Material = new FixedString32(box.Material) });
            world.AddComponent(e, new ObstacleShape { Length = box.Length, Width = box.Width, Height = box.Height });
        }
        world.Tick();
        var into = new List<Vector2[]>();
        Hrot.Traffic.RoadBlockage.Blockers(world, into);
        Assert.Equal(cmds.Count, into.Count);
        foreach (var fp in into)
        {
            float w = fp.Max(p => p.X) - fp.Min(p => p.X), h = fp.Max(p => p.Y) - fp.Min(p => p.Y);
            Assert.Equal(extentX, w, 2); Assert.Equal(extentY, h, 2);
        }
        Assert.Equal(cmds.Select(c => c.InitialTransform!.Value.Position.X).OrderBy(x => x).ToArray(), into.Select(fp => (fp.Min(p => p.X) + fp.Max(p => p.X)) / 2f).OrderBy(x => x).ToArray(), new FloatCmp());
    }

    /// <summary>
    /// ⭐ CE-5010 (TE-5, R-4264) — SEVEN obstacle types, ONE table: every id of <c>TkbEntityTypes.Obstacles</c> is a registered obstacle whose box and material are its <c>ClutterTypes</c> entry's (so a baked clutter item and the live obstacle of that name are one box), the row's Item list
    /// offers exactly those types in the same order, and the three new wire ids are distinct from every other obstacle. 🔴 Red-proof: register the barrel from <c>ClutterTypes.Crate</c> ⇒ the size assert fails; drop one from <c>Obstacles</c> ⇒ the count / list assert fails.
    /// </summary>
    [Fact]
    public void TheSevenObstacleTypes_AreRegisteredFromTheClutterTable_AndTheRowOffersThemAll()
    {
        var db = new Fdp.Toolkit.Tkb.TkbDatabase();
        Hrot.Map.Definitions.Tkb.NedTkbCatalog.RegisterObstacles(db);
        Assert.Equal(9, TkbEntityTypes.Obstacles.Length);   // ⭐ CE-3473 — + the water- and sand-filled barrels (8823, 8824)
        Assert.Equal(TkbEntityTypes.Obstacles.Length, TkbEntityTypes.Obstacles.Distinct().Count());
        Assert.Equal(TkbEntityTypes.Obstacles, ObstacleRowOptions.Types.Select(t => t.Type).ToArray());
        Assert.Equal((8820L, 8821L, 8822L), (TkbEntityTypes.Obstacle_Barrel, TkbEntityTypes.Obstacle_JerseyBarrier, TkbEntityTypes.Obstacle_GuardBooth));
        foreach (var (type, name, box, icon) in ObstacleRowOptions.Types)
        {
            var dto = db.GetByType(type)!.GetDescriptor<Fdp.Toolkit.Tkb.Domain.StaticObstacleDto>()!;
            Assert.Equal((box.Length, box.Width, box.Height, box.Material), (dto.Length, dto.Width, dto.Height, dto.Material));
            Assert.Equal(box.Name, name);
            Assert.True(NodeEditor.Core.Widgets.Icons.TryGet(icon, out _), icon);
        }
        var health = TkbEntityTypes.Obstacles.ToDictionary(t => t, t => db.GetByType(t)!.GetDescriptor<Fdp.Toolkit.Tkb.Domain.StaticObstacleDto>()!.MaxHealth);
        Assert.Equal((50f, 400f, 120f), (health[TkbEntityTypes.Obstacle_Barrel], health[TkbEntityTypes.Obstacle_JerseyBarrier], health[TkbEntityTypes.Obstacle_GuardBooth]));   // starter values
        Assert.Equal((150f, 250f), (health[TkbEntityTypes.Obstacle_BarrelWater], health[TkbEntityTypes.Obstacle_BarrelSand]));   // ⭐ CE-3473 — the filled drums
    }

    private sealed class FloatCmp : IEqualityComparer<float> { public bool Equals(float a, float b) => System.MathF.Abs(a - b) < 0.01f; public int GetHashCode(float f) => 0; }

    private static float YawDeg(Quaternion q) => System.MathF.Round(2f * System.MathF.Atan2(q.Z, q.W) * (180f / MathF.PI) * 100f) / 100f;
}
