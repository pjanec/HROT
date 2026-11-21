using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.UI.Common.Map2D;
using Xunit;

namespace Hrot.Presentation.Tests;

/// <summary>
/// ⭐ CE-1462 (W-B1) — the layer that frames the 2-D map's terrain on load, over a real world and a real 2-D camera: it frames once on the first resident terrain (cqb-village's 330 × 232 m is not left at the pixel default), again when
/// the terrain changes, never otherwise (a pan wins), and a camera the user or a station owns is left alone. Red-proof (compiles): drop the layer's <c>leaveAlone()</c> argument ⇒ the "left alone" rail fails.
/// </summary>
public sealed class TerrainFramingLayerTests
{
    private static EntityRepository Resident(string name, float w, float h)
    {
        var world = new EntityRepository();
        world.SetSingletonManaged(new TerrainDefinition { ResolvedName = name });
        world.SetSingletonManaged(new TerrainWorld { Name = name, BoundsMin = new Vector2(0, 0), BoundsMax = new Vector2(w, h) });
        return world;
    }

    private static MapCamera Camera() => new() { ViewportOverride = new Vector2(1000, 600) };

    [Fact]
    public void TheFirstResidentTerrain_IsFramed_WithItsBounds_AndAPanIsNeverUndone()
    {
        var world = new EntityRepository();
        var camera = Camera();
        bool leave = false;
        var layer = new TerrainFramingLayer(() => world, () => camera, () => leave, () => false);

        layer.Update(0.016f);
        Assert.Equal(0, layer.Framings);                                     // no terrain yet: nothing moved

        world.SetSingletonManaged(new TerrainDefinition { ResolvedName = "cqb-village" });
        world.SetSingletonManaged(new TerrainWorld { BoundsMin = Vector2.Zero, BoundsMax = new Vector2(330, 232) });
        layer.Update(0.016f);
        Assert.Equal(1, layer.Framings);
        var view = camera.GetCameraView()!.Value;
        Assert.Equal(new Vector2(165, 116), view.Target);                    // the terrain's centre
        Assert.True(view.Zoom > 2f);                                          // magnified, not the default 1 that left it in a corner
        // the visible world rectangle covers the terrain plus the margin
        var topLeft = camera.ScreenToWorld(Vector2.Zero); var bottomRight = camera.ScreenToWorld(new Vector2(1000, 600));
        Assert.True(Math.Min(topLeft.X, bottomRight.X) <= -16f && Math.Max(topLeft.X, bottomRight.X) >= 346f, "covers 330 m plus 5 % each side");
        Assert.True(Math.Min(topLeft.Y, bottomRight.Y) <= -11f && Math.Max(topLeft.Y, bottomRight.Y) >= 243f, "covers 232 m plus 5 % each side");

        camera.Target = new Vector2(10, 10); camera.Zoom = 7f;               // the user pans and zooms
        layer.Update(0.016f); layer.Update(0.016f);
        Assert.Equal(1, layer.Framings);
        Assert.Equal(new Vector2(10, 10), camera.Target);                    // ... and it stays theirs
    }

    [Fact]
    public void ATerrainChange_FramesAgain_ASecondLoadOfTheSameTerrainAfterAnUnloadToo()
    {
        var world = Resident("cqb-village", 330, 232);
        var camera = Camera();
        var layer = new TerrainFramingLayer(() => world, () => camera, () => false, () => false);
        layer.Update(0.016f);
        Assert.Equal(1, layer.Framings);

        world.SetSingletonManaged(new TerrainDefinition { ResolvedName = "demo-city" });
        world.SetSingletonManaged(new TerrainWorld { BoundsMin = Vector2.Zero, BoundsMax = new Vector2(2000, 1500) });
        layer.Update(0.016f);
        Assert.Equal(2, layer.Framings);
        Assert.Equal(new Vector2(1000, 750), camera.GetCameraView()!.Value.Target);
        Assert.True(camera.Zoom < 0.6f);                                      // a big terrain is zoomed OUT

        world.SetSingletonManaged(new TerrainDefinition { ResolvedName = "" });   // unloaded
        layer.Update(0.016f);
        world.SetSingletonManaged(new TerrainDefinition { ResolvedName = "demo-city" });
        layer.Update(0.016f);
        Assert.Equal(3, layer.Framings);
    }

    [Fact]
    public void AFollowedDrivenOr3DCamera_IsLeftAlone_AndThatTerrainIsNotFramedLater()
    {
        var world = Resident("cqb-village", 330, 232);
        var camera = Camera();
        var before = camera.GetCameraView()!.Value;
        bool leave = true;
        var layer = new TerrainFramingLayer(() => world, () => camera, () => leave, () => false);
        layer.Update(0.016f);
        Assert.Equal(0, layer.Framings);
        Assert.Equal(before.Target, camera.GetCameraView()!.Value.Target);
        Assert.Equal(before.Zoom, camera.GetCameraView()!.Value.Zoom);
        leave = false;
        layer.Update(0.016f);
        Assert.Equal(0, layer.Framings);                                      // consumed: not taken behind the user's back

        bool switching = true;
        var world2 = Resident("other", 100, 100);
        var layer2 = new TerrainFramingLayer(() => world2, () => camera, () => false, () => switching);
        layer2.Update(0.016f);
        Assert.Equal(0, layer2.Framings);                                     // mid-switch: wait ...
        switching = false;
        layer2.Update(0.016f);
        Assert.Equal(1, layer2.Framings);                                     // ... then frame
    }

    [Fact]
    public void ATerrainWithNoExtent_IsNotFramed_AndRoadsOnlyWorldsCostNothing()
    {
        var world = new EntityRepository();
        world.SetSingletonManaged(new TerrainDefinition { ResolvedName = "roads-only" });   // no TerrainWorld at all
        var camera = Camera();
        var layer = new TerrainFramingLayer(() => world, () => camera, () => false, () => false);
        layer.Update(0.016f);
        Assert.Equal(0, layer.Framings);
        Assert.Null(TerrainFramingLayer.BoundsOf(world));
        world.SetSingletonManaged(new TerrainWorld { BoundsMin = Vector2.Zero, BoundsMax = Vector2.Zero });
        Assert.Null(TerrainFramingLayer.BoundsOf(world));                                    // a degenerate extent is none
    }
}
