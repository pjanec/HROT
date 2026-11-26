using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Common;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐ CE-6503 (docs/designs/engineering/DESIGN_Bridge_Laying.md §13.4; the acceptance spec is <c>UX/laid-bridge-visual.svg</c>) — a LAID bridge reads as a bridge: the TERRAIN
/// still draws its deck and ramps (the walkables units stand on — U-7, nothing about crossing changes), and the bridge ENTITY draws its details on and under it — the
/// assault bridge's girders, hinge, end plates and treadways, the floating bridge's pontoon bays, joints and curbs — sized by the entity's own <c>ObstacleShape</c>.
/// Before this, the obstacle had no family and drew as the 2 m unknown box in the river. ⛔ The frame test SKIPS without a display: <c>xvfb-run -a dotnet test … --filter FullyQualifiedName~LaidBridgeModelRail</c>.
/// 🔴 Red-proof (compiles): <c>VisualFamilies.Of</c> without the two type checks ⇒ Unknown ⇒ red.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class LaidBridgeModelRail
{
    [Theory]
    [InlineData(TkbEntityTypes.Obstacle_AssaultBridge, VisualFamily.LaidAssaultBridge, 26f, 4f, 1.2f)]
    [InlineData(TkbEntityTypes.Obstacle_FloatingBridge, VisualFamily.LaidFloatingBridge, 100f, 4.5f, 0.6f)]
    public void TheBridgeObstacle_HasItsOwnFamily_AndKit_SizedLikeItsDeck(long type, VisualFamily family, float l, float w, float h)
    {
        var t = Map3DTests.BuiltIn().GetByType(type)!;
        Assert.Equal(family, VisualFamilies.Of(t));
        var look = EntityBodyLayer3D.Resolve(t);
        Assert.Equal(family, look.Family);
        Assert.Same(ShapeKits.For(family), EntityBodyLayer3D.KitOf(look));
        Assert.Equal(new Vector3(l, w, h), ShapeKits.DefaultSize(family));
        Assert.NotEqual(VisualFamily.Unknown, VisualFamilies.Of(Map3DTests.BuiltIn().GetByType(TkbEntityTypes.Obstacle_AssaultBridge)!));
    }

    /// <summary>The details lie along the deck (inside its length), never ON the slab's walking surface as a box across it (the treadway gap and the joints are a few cm),
    /// and the floating bridge's bays reach down into the water under the deck.</summary>
    [Fact]
    public void TheDetails_RunAlongTheDeck_TheBaysReachTheWater()
    {
        foreach (var family in new[] { VisualFamily.LaidAssaultBridge, VisualFamily.LaidFloatingBridge })
            foreach (var p in ShapeKits.For(family).Parts)
                Assert.True(MathF.Abs(p.Centre.X) + p.Extent.X / 2f <= 0.5f + 1e-3f, $"{family}: a part beyond the deck's ends {p.Centre} ± {p.Extent}");
        var bays = ShapeKits.LaidFloatingBridge.Parts.Where(p => p.Colour == ColourRole.Body).ToArray();
        Assert.Equal(ShapeKits.FloatingBridgeBays, bays.Length);
        Assert.All(bays, b => Assert.True(b.Centre.Z - b.Extent.Z / 2f < 0f, "a bay reaches below the bank's height, into the water"));
    }

    /// <summary>
    /// ⭐ The real frame: two rivers, each with the deck the BAKE lays (<c>TerrainObstacles.DeckOf</c> / <c>With</c>) and its bridge entity on it, the laying engineers beside them
    /// with no span on board, a tank on the assault deck and a man on the floating one. The entity's details change the picture (they are drawn), the deck under them is the
    /// terrain's. Pictures into <c>HROT_RAIL_SHOTS</c> as <c>laid-*.png</c>.
    /// </summary>
    [SkippableFact]
    public void TheLaidBridges_AreDrawn_ByTheRealFrame_OnTheTerrainsDeck()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);
        const string json = """
            { "type": "FeatureCollection", "hrot": { "schemaVersion": 1, "bounds": [-100, -100, 400, 400], "groundZ": 0 }, "features": [
              { "type": "Feature", "properties": { "kind": "surface", "surface": "water" },
                "geometry": { "type": "Polygon", "coordinates": [[[-100,60],[400,60],[400,72],[-100,72],[-100,60]]] } },
              { "type": "Feature", "properties": { "kind": "surface", "surface": "water" },
                "geometry": { "type": "Polygon", "coordinates": [[[-100,160],[400,160],[400,240],[-100,240],[-100,160]]] } } ] }
            """;
        var bare = TerrainWorldParser.Parse(json, "rivers");
        var assaultShape = new ObstacleShape { Length = 14f, Width = 4f, Height = 1.2f };     // a 12 m river + 1 m bearing each side (BridgeLaying.Plan)
        var floatingShape = new ObstacleShape { Length = 82f, Width = 4.5f, Height = 0.6f };  // an 80 m river
        var decks = new List<TerrainWalkable>();
        TerrainObstacles.DeckOf(new Vector3(100f, 66f, 0f), MathF.PI / 2f, assaultShape, (_, _) => 0f, 1, false, decks);
        TerrainObstacles.DeckOf(new Vector3(100f, 200f, 0f), MathF.PI / 2f, floatingShape, (_, _) => 0f, 2, false, decks);
        var terrain = TerrainObstacles.With(bare, Array.Empty<TerrainPrism>(), null, null, null, null, decks);
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<BridgeCarried>();
        world.RegisterComponent<ObstacleShape>();
        var tkb = Map3DTests.BuiltIn();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        Entity Put(long type, float x, float y, float z, float yaw)
        {
            var e = world.CreateEntity();
            world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, z), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw) });
            world.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)type });
            return e;
        }
        var assault = Put(TkbEntityTypes.Obstacle_AssaultBridge, 100f, 66f, 0f, MathF.PI / 2f);
        world.AddComponent(assault, assaultShape);
        var floating = Put(TkbEntityTypes.Obstacle_FloatingBridge, 100f, 200f, 0f, MathF.PI / 2f);
        world.AddComponent(floating, floatingShape);
        world.AddComponent(Put(TkbEntityTypes.Engineer_AssaultBridgeLayer, 106f, 44f, 0f, MathF.PI / 2f + 0.5f), new BridgeCarried { Count = 0, BridgeType = 1 });
        world.AddComponent(Put(TkbEntityTypes.Engineer_FloatingBridge, 92f, 145f, 0f, MathF.PI / 2f - 0.4f), new BridgeCarried { Count = 0, BridgeType = 1 });
        Put(TkbEntityTypes.Tank_M1Abrams, 100f, 66f, 1.2f, MathF.PI / 2f);
        Put(TkbEntityTypes.Infantry_Rifleman, 100f, 190f, 0.6f, MathF.PI / 2f);

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (_, _) => terrain.GroundZ;
        sw.Set(true, animate: false);
        void Look(Vector3 at, float dist, float yaw, float pitch) => sw.Camera3D.Pose = new CameraPose(at, dist, yaw, pitch);
        void Render() { for (int i = 0; i < 3; i++) frame.Step(canvas.Draw); }
        void Shot(string name) { Render(); frame.Screenshot(Path.Combine(shots, name)); }

        Look(new Vector3(100f, 66f, 1f), 30f, 0.6f, -0.35f); Shot("laid-1-assault-34.png");
        Look(new Vector3(100f, 66f, 1f), 30f, 0f, -0.10f); Shot("laid-2-assault-side.png");
        Look(new Vector3(100f, 200f, 1f), 95f, 0.7f, -0.45f); Shot("laid-3-floating-34.png");
        Look(new Vector3(100f, 172f, 1f), 26f, 0.9f, -0.16f); Shot("laid-4-floating-low.png");

        // the details are drawn: the assault bridge without its entity is the terrain's bare slab
        Look(new Vector3(100f, 66f, 1f), 30f, 0.6f, -0.35f);
        var with = AirCombatLooksRail.ScreenOf(frame, Render);
        world.SetComponent(assault, new SimTransform { Position = new Vector3(5000f, 5000f, 0f), Rotation = Quaternion.Identity });
        var without = AirCombatLooksRail.ScreenOf(frame, Render);
        Assert.True(AirCombatLooksRail.Diff(with, without) > 1500, "the bridge's girders and plates are drawn on the deck");
    }
}
