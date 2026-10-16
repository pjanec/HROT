using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using GizmoMap.Network;
using Hrot.Map.Common;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ CE-1033 S2 — picking in 3-D (<c>docs/DESIGN_Map_3D_Mode.md</c> §3.8, M17–M20): a click lands on the drawn terrain with its
/// height, on an entity's drawn box (returning the entity's own position), a drag follows the terrain only, and the height
/// reaches the gizmo events.
/// </summary>
public sealed class Map3DPickingTests
{
    private static readonly Vector2 Viewport = new(1280, 720);

    private static (TerrainWorld Terrain, EntityRepository World, IWorldQuery Query) Town()
    {
        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        return (terrain, world, WorldQuery.Of(world)!);
    }

    /// <summary>A point on a roof (a second surface above the ground) and a point of open ground.</summary>
    private static (Vector2 Roof, float RoofZ, Vector2 Open, float GroundZ) Spots(TerrainWorld terrain, IWorldQuery q)
    {
        Vector2? roof = null, open = null; float roofZ = 0, groundZ = 0;
        for (float x = terrain.BoundsMin.X + 5; x < terrain.BoundsMax.X && (roof == null || open == null); x += 3f)
            for (float y = terrain.BoundsMin.Y + 5; y < terrain.BoundsMax.Y && (roof == null || open == null); y += 3f)
            {
                var s = q.SurfacesAt(x, y, out int g);
                if (roof == null && s.Count > g + 1 && s[^1] - s[g] > 4f)
                {
                    // the roof must also be roof a little around, so a vertical ray is not on an edge
                    bool solid = true;
                    foreach (var (dx, dy) in new[] { (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f) })
                        solid &= q.SurfacesAt(x + dx, y + dy, out int g2).Count > g2 + 1;
                    if (solid) { roof = new Vector2(x, y); roofZ = s[^1]; }
                }
                if (open == null && s.Count == g + 1) { open = new Vector2(x, y); groundZ = s[g]; }
            }
        Assert.True(roof != null && open != null, "test-town should have a roof and open ground");
        return (roof!.Value, roofZ, open!.Value, groundZ);
    }

    [Fact]
    public void S2_ARayDown_LandsOnTheRoof_OrTheGround_WithItsHeight()
    {
        var (terrain, world, q) = Town();
        var (roof, roofZ, open, groundZ) = Spots(terrain, q);
        var picker = new TerrainPicker(() => WorldQuery.RenderGeometryOf(world));

        Assert.True(picker.TryPick(new Vector3(roof, 500f), -Vector3.UnitZ, 1000f, out var onRoof));
        Assert.InRange(onRoof.Point.Z, roofZ - 0.05f, roofZ + 0.05f);
        Assert.Equal(PickKind.Terrain, onRoof.Kind);

        Assert.True(picker.TryPick(new Vector3(open, 500f), -Vector3.UnitZ, 1000f, out var onGround));
        Assert.InRange(onGround.Point.Z, groundZ - 0.05f, groundZ + 0.05f);

        Assert.Equal(1, Levels.LevelOf(q, onRoof.Point) >= 1 ? 1 : 0);   // the roof is a level above the ground (M20)
        Assert.Equal(0, Levels.LevelOf(q, onGround.Point));
    }

    [Fact]
    public void S2_ASlantedRay_StopsAtTheFirstSurface_NotTheGroundBehindIt()
    {
        var (terrain, world, q) = Town();
        var (roof, roofZ, _, _) = Spots(terrain, q);
        var picker = new TerrainPicker(() => WorldQuery.RenderGeometryOf(world));
        // from above and to the side, aimed at the roof point: the first thing hit must be at or above the roof's height
        var from = new Vector3(roof.X - 60f, roof.Y - 60f, roofZ + 60f);
        var dir = Vector3.Normalize(new Vector3(roof, roofZ) - from);
        Assert.True(picker.TryPick(from, dir, 1000f, out var hit));
        Assert.True(hit.Point.Z >= roofZ - 0.1f, $"hit {hit.Point} below the roof {roofZ} — went through the building");
    }

    private static Entity Tank(EntityRepository world, Vector3 at, long netId)
    {
        if (!world.IsComponentTypeRegistered<SimTransform>()) world.RegisterComponent<SimTransform>();
        if (!world.IsComponentTypeRegistered<TkbIdentity>()) world.RegisterComponent<TkbIdentity>();
        if (!world.IsComponentTypeRegistered<NetworkIdentity>()) world.RegisterComponent<NetworkIdentity>();
        var e = world.CreateEntity();
        world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
        world.AddComponent(e, new TkbIdentity { TkbType = TkbEntityTypes.Tank_M1Abrams });
        world.AddComponent(e, new NetworkIdentity(netId));
        return e;
    }

    [Fact]
    public void S2_AnEntityIsHitByItsDrawnBox_AndAnswersWithItsOwnPosition_NeverBesideIt()
    {
        var (_, world, q) = Town();
        var tkb = Map3DTests.BuiltIn();
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var at = new Vector3(10f, 20f, q.GroundHeightAt(10f, 20f));
        Tank(world, at, 77);
        var picker = new EntityBoxPicker(() => world, () => tkb, bodies);

        // Down onto the front of the hull (the M1 is 7.93 m long, facing east): hit, and the answer is the tank's position.
        Assert.True(picker.TryPick(new Vector3(at.X + 3f, at.Y, at.Z + 50f), -Vector3.UnitZ, 200f, out var hit));
        Assert.Equal(PickKind.Entity, hit.Kind);
        Assert.Equal(77, hit.NetworkId);
        Assert.Equal(at, hit.Point);

        // 2.5 m to the side of a 3.66 m-wide hull: a miss (the sim's bounding circle would have caught it).
        Assert.False(picker.TryPick(new Vector3(at.X, at.Y + 2.5f, at.Z + 50f), -Vector3.UnitZ, 200f, out _));
    }

    [Fact]
    public void S2_TheCamera_PicksTheEntity_ButADragFollowsTheTerrain()
    {
        var (_, world, q) = Town();
        var tkb = Map3DTests.BuiltIn();
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var at = new Vector3(10f, 20f, q.GroundHeightAt(10f, 20f));
        Tank(world, at + new Vector3(0, 0, 0), 5);
        var cam = new MapCamera3D { ViewportOverride = Viewport, LookAt = at, Distance = 40f, Pitch = -0.9f, Yaw = 1.0f };
        cam.Pickers.Add(new TerrainPicker(() => WorldQuery.RenderGeometryOf(world)));
        cam.Pickers.Add(new EntityBoxPicker(() => world, () => tkb, bodies));

        // The screen centre looks at the tank's footprint centre; aim a little up the hull to be sure to hit its box.
        var px = cam.WorldToScreen(at + new Vector3(0, 0, 1.2f));
        var click = cam.ScreenToWorld3D(px);
        Assert.Equal(PickKind.Entity, cam.LastPick.Kind);
        Assert.Equal(at, click);

        var drag = cam.ScreenToWorld3D(px, forDrag: true);
        Assert.NotEqual(PickKind.Entity, cam.Pick(px, PickFilter.Terrain).Kind);
        Assert.True(Vector2.Distance(new Vector2(drag.X, drag.Y), new Vector2(at.X, at.Y)) > 0.3f,
            "the drag point is the terrain behind the tank's top, not the tank's own position");
    }

    /// <summary>
    /// ⭐⭐ CE-1080 (docs/DESIGN_Entity_Control.md §8e D-4) — the first-person crosshair sees UNITS (it picked terrain only, so a tank under
    /// it was aimed at the ground behind), never the operator's OWN body (the eye sits inside its box, and a ray from inside meets the
    /// box on the way out — nearer than anything), and its aim point is where the ray meets the unit's box, not the unit's feet.
    /// </summary>
    [Fact]
    public void CE1080_TheFirstPersonCrosshair_SeesTheUnit_NeverTheOwnBody_AndAimsAtTheBoxHit()
    {
        var (_, world, q) = Town();
        var tkb = Map3DTests.BuiltIn();
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var at = new Vector3(10f, 20f, q.GroundHeightAt(10f, 20f));
        Tank(world, at, 77);
        var cam = new MapCamera3D { ViewportOverride = Viewport, LookAt = at + new Vector3(0f, 0f, 1.2f), Distance = 40f, Pitch = -0.05f, Yaw = 1.0f };
        cam.Pickers.Add(new TerrainPicker(() => WorldQuery.RenderGeometryOf(world)));
        cam.Pickers.Add(new EntityBoxPicker(() => world, () => tkb, bodies));
        cam.Pickers.Add(new EntityPartPicker(() => world, () => tkb, bodies));   // ⭐ CE-1126 — the crosshair asks for the parts
        var own = Tank(world, cam.Eye - new Vector3(0f, 0f, 1.5f), 5);   // the operator's tank: the eye is inside its hull box

        bodies.FirstPerson = own;
        var (point, target) = FpsCameraDriver.Aim(cam);
        Assert.Equal(77L, target);
        Assert.True(point.Z > at.Z + 0.3f, $"aimed at {point}: on the hull, not at the feet {at}");
        Assert.True(Vector3.Distance(point, at) < 5f, $"aimed at {point}, the tank is at {at}");

        bodies.FirstPerson = Entity.Null;                                   // what the skip prevents: the own hull wins the pick
        cam.Yaw += 1e-4f;                                                   // (a new pose: the pick is cached per pose)
        Assert.Equal(5L, FpsCameraDriver.Aim(cam).Target);
    }

    private static Entity Rifleman(EntityRepository world, Vector3 at, long netId)
    {
        var e = Tank(world, at, netId);
        world.SetComponent(e, new TkbIdentity { TkbType = TkbEntityTypes.Infantry_Rifleman });
        return e;
    }

    /// <summary>
    /// ⭐⭐ CE-1126 (docs/DESIGN_Entity_Control.md §8g F-4) — the FPS crosshair meets a man by the PARTS a round meets (BodyPartRay, the
    /// bullet solver's own test): a ray at his head answers a point ON his head (the round goes there), and a ray past his hip — inside
    /// the selection box, between his parts — answers nothing, while RTS selection (<see cref="EntityBoxPicker"/>) still takes it.
    /// 🔒 User, 2026-10-16: <i>"when i aim at character and fire i always hit the belly"</i> · <i>"yes, let's use the body part boxes for the crosshair"</i>.
    /// </summary>
    [Fact]
    public void CE1126_TheCrosshair_MeetsTheMansParts_TheHeadPointIsOnHisHead_AndTheGapPastHisHipIsEmpty()
    {
        var (_, world, q) = Town();
        var tkb = Map3DTests.BuiltIn();
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var at = new Vector3(10f, 20f, q.GroundHeightAt(10f, 20f));
        Rifleman(world, at, 42);
        var parts = new EntityPartPicker(() => world, () => tkb, bodies);
        var boxes = new EntityBoxPicker(() => world, () => tkb, bodies);

        // a level ray from 20 m west at his head (a standing figure of 1.8 m: the head is ~1.6-1.8 m)
        var eye = new Vector3(at.X - 20f, at.Y, at.Z + 1.68f);
        Assert.True(parts.TryPick(eye, Vector3.UnitX, 200f, out var head), "the crosshair at his head meets him");
        Assert.Equal(PickKind.Entity, head.Kind);
        Assert.Equal(42L, head.NetworkId);
        Assert.InRange(head.Point.Z, at.Z + 1.55f, at.Z + 1.85f);   // the point is ON the head — not his position, not his belly
        Assert.InRange(head.Point.X, at.X - 0.4f, at.X + 0.1f);      // and on the near side of him, where the ray enters

        // a level ray 0.33 m beside him at knee height: the selection box (half ≥ 0.4 m) takes it, the parts do not
        var beside = new Vector3(at.X - 20f, at.Y + 0.33f, at.Z + 0.45f);
        Assert.True(boxes.TryPick(beside, Vector3.UnitX, 200f, out _), "RTS selection keeps its generous box");
        Assert.False(parts.TryPick(beside, Vector3.UnitX, 200f, out _), "the crosshair between his parts is on nothing");
    }

    [Fact]
    public void S2_TheGizmoProxy_CarriesTheHeight_IntoTheInteractionEvents()
    {
        var events = new List<(GizmoInteractionEventKind Kind, Vector3 Pos)>();
        float z = 12.5f;
        var tool = new GizmoMap.Presentation.GizmoInteractionProxyTool(
            default, new Vector2(1, 2), (_, kind, pos, _, _) => events.Add((kind, pos)),
            initialZ: 3f, height: () => z);
        tool.HandlePress(new Vector2(1, 2), Raylib_cs.MouseButton.Left);
        tool.HandleDrag(new Vector2(4, 5), new Vector2(1, 1));
        z = 14f;
        tool.HandleClick(new Vector2(6, 7), Raylib_cs.MouseButton.Left);

        Assert.Equal(new Vector3(1, 2, 3), events[0].Pos);                   // Started at the press point's height
        Assert.Equal(new Vector3(4, 5, 12.5f), events[1].Pos);               // DragUpdate at the terrain under the cursor
        Assert.Equal(new Vector3(6, 7, 14f), events[2].Pos);                 // Commit
    }
}
