using System;
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
/// ⭐⭐ CE-6502 (docs/designs/engineering/DESIGN_Bridge_Laying.md §13 U-6; the acceptance spec is the mockup panel ④, <c>UX/bridge-laying-ui-visual.svg</c>) — the two bridge
/// engineers' 3-D looks: the ASSAULT BRIDGE LAYER (TKB 118, class <c>AssaultBridgeLayer</c>) a tank chassis with its folded span on the roof, the FLOATING-BRIDGE truck (TKB 119,
/// class <c>FloatingBridgeEngineer</c>) a four-axle truck with a ribbon bay on the bed. The span parts carry <see cref="ShapeKits.BridgeSpanRound"/>: drawn while the engineer
/// carries a span, gone once it is laid (<see cref="BridgeLayerLook"/> NoSpan). ⛔ The frame test SKIPS without a display: <c>xvfb-run -a dotnet test … --filter FullyQualifiedName~BridgeEngineerModelRail</c>.
/// 🔴 Red-proof (run): the layer's NoSpan ⇒ rounds 0 line removed ⇒ the laid engineer still draws its span ⇒ the frame rail's diff is 0 ⇒ red.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class BridgeEngineerModelRail
{
    [Theory]
    [InlineData(VisualFamily.BridgeLayer, "AssaultBridgeLayer", TkbEntityTypes.Engineer_AssaultBridgeLayer, 13.4f, 4.0f, 4.0f, "_tank")]
    [InlineData(VisualFamily.FloatingBridge, "FloatingBridgeEngineer", TkbEntityTypes.Engineer_FloatingBridge, 11.0f, 2.6f, 3.5f, "_wheeled")]
    public void TheFamily_HasItsKit_Box_Colour_Glyph_AndTheRealTypeResolvesToIt(VisualFamily family, string cls, long type, float l, float w, float h, string glyph)
    {
        var kit = ShapeKits.For(family);
        Assert.NotSame(ShapeKits.For(VisualFamily.Unknown), kit);
        Assert.Equal(new Vector3(l, w, h), ShapeKits.DefaultSize(family));
        Assert.Equal(family, VisualFamilies.FamilyOfClass[cls]);
        Assert.Equal(glyph, VisualFamilies.IconName(family));
        Assert.NotEqual(EntityBodyLayer3D.DefaultColour(VisualFamily.Unknown), EntityBodyLayer3D.DefaultColour(family));
        var t = Map3DTests.BuiltIn().GetByType(type)!;
        var look = EntityBodyLayer3D.Resolve(t);
        Assert.Equal(family, look.Family);
        Assert.Same(kit, EntityBodyLayer3D.KitOf(look));
        Assert.Equal(new Vector3(l, w, h), look.Size);   // the TKB's own box
    }

    /// <summary>The span parts: there are some, every one sits above the hull (on the roof / the bed), and the kit lies inside its box on the ground.</summary>
    [Theory]
    [InlineData(VisualFamily.BridgeLayer, 2.4f)]
    [InlineData(VisualFamily.FloatingBridge, 1.55f)]
    public void TheSpan_IsTheKitsTopParts_TiedToRoundZero_AndTheKitStandsInItsBox(VisualFamily family, float aboveMetres)
    {
        var size = ShapeKits.DefaultSize(family);
        var kit = ShapeKits.For(family);
        var span = kit.Parts.Where(p => p.Round == ShapeKits.BridgeSpanRound).ToArray();
        Assert.True(span.Length >= 3, $"{family}: {span.Length} span parts");
        Assert.All(kit.Parts.Where(p => p.Round < 0), p => Assert.True(p.Round == -1));   // nothing else is tied to a round
        foreach (var p in span)
        {
            var bottom = (p.Centre.Z - p.Extent.Z / 2f) * size.Z;
            Assert.True(bottom >= aboveMetres - 0.3f, $"{family}: a span part's bottom at {bottom:0.00} m");
        }
        foreach (var p in kit.Parts)
        {
            Assert.True(MathF.Abs(p.Centre.X) <= 0.5f + 1e-3f && MathF.Abs(p.Centre.Y) <= 0.5f + 1e-3f, $"{family}: a part centred outside the box {p.Centre}");
            Assert.True(p.Centre.Z - p.Extent.Z / 2f >= -1e-3f && p.Centre.Z + p.Extent.Z / 2f <= 1.0f + 1e-3f, $"{family}: a part outside the box height {p.Centre} ± {p.Extent}");
        }
    }

    /// <summary>
    /// ⭐ The real frame draws both engineers carrying their spans (side, three-quarter) into <c>HROT_RAIL_SHOTS</c> as <c>bridge-*.png</c>; once a span is LAID (Count 0,
    /// no order ⇒ <see cref="BridgeLayerState.NoSpan"/>) the roof / bed is bare — the picture changes, and only where the span was.
    /// </summary>
    [SkippableFact]
    public void TheEngineers_AreDrawn_ByTheRealFrame_AndALaidSpanLeavesTheRoof()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);
        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<BridgeCarried>();
        var tkb = Map3DTests.BuiltIn();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        var query = WorldQuery.Of(world)!;
        var spot = BulldozerModelRail.OpenGround(terrain, query);
        float G(float x, float y) => query.GroundHeightAt(x, y);
        Entity Put(long type, float dx, float dy)
        {
            var e = world.CreateEntity();
            float x = spot.X + dx, y = spot.Y + dy;
            world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, G(x, y)), Rotation = Quaternion.Identity });
            world.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)type });
            world.AddComponent(e, new BridgeCarried { Count = 1, BridgeType = 1 });
            return e;
        }
        var layer = Put(TkbEntityTypes.Engineer_AssaultBridgeLayer, 0f, 0f);
        var truck = Put(TkbEntityTypes.Engineer_FloatingBridge, 0f, 9f);
        Assert.Equal(BridgeLayerState.Carried, BridgeLayerLook.Of(world, layer));

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => G(x, y);
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        sw.Set(true, animate: false);
        float ground = G(spot.X, spot.Y);
        void Look(float dist, float yaw, float pitch) => sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X, spot.Y + 4.5f, ground + 1.8f), dist, yaw, pitch);
        void Render() { frame.Step(canvas.Draw); frame.Step(canvas.Draw); }
        void Shot(string name) { Render(); frame.Screenshot(Path.Combine(shots, name)); }
        const float Side = MathF.PI * 0.5f;

        Look(30f, Side, -0.10f);
        Shot("bridge-1-side-carried.png");
        Assert.Equal(2, bodies.BodiesDrawn);
        Look(30f, Side + 0.75f, -0.35f);
        Shot("bridge-2-34-carried.png");

        Look(30f, Side, -0.10f);
        var carried = AirCombatLooksRail.ScreenOf(frame, Render);
        world.SetComponent(layer, new BridgeCarried { Count = 0, BridgeType = 1 });   // laid: no span on board
        world.SetComponent(truck, new BridgeCarried { Count = 0, BridgeType = 1 });
        Assert.Equal(BridgeLayerState.NoSpan, BridgeLayerLook.Of(world, layer));
        var laid = AirCombatLooksRail.ScreenOf(frame, Render);
        Shot("bridge-3-side-laid.png");
        int changed = AirCombatLooksRail.Diff(carried, laid);
        Assert.True(changed > 2000, $"the spans leave the roof and the bed: {changed} px changed");
    }
}
