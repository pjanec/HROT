using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
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
/// ⭐⭐ <c>CE-6007</c> (showcase world, DESIGN_City_Battle_Showcase V-1b; 🔒 user, 2026-11-13: <i>"just new bulldozer model … no need to look armored"</i>) — the
/// bulldozer's 3-D model: the type resolves to its own family and kit; the kit's parts lie inside the type's 8 × 4.3 × 4 m box (a D9 with the blade); and the
/// real 3-D frame draws it (side, three-quarter, rear, from above — beside a T-72 and a rifleman for scale) into <c>HROT_RAIL_SHOTS</c> as <c>dozer-*.png</c>.
/// ⛔ The frame test SKIPS without a display: <c>xvfb-run -a dotnet test … --filter FullyQualifiedName~BulldozerModelRail</c>. 🔴 Red-proof: the family
/// mapping removed (⇒ the grey unknown box) fails the family rail; a blade pushed past the box fails the bounds rail.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class BulldozerModelRail
{
    [Fact]
    public void TheBulldozerType_ResolvesToItsOwnFamily_ItsKitAndItsD9Size_NotTheUnknownBox()
    {
        var tkb = Map3DTests.BuiltIn();
        var look = EntityBodyLayer3D.Resolve(tkb.GetByType(TkbEntityTypes.Engineer_Bulldozer));
        Assert.Equal(VisualFamily.Bulldozer, look.Family);
        Assert.Equal(new Vector3(8.0f, 4.3f, 4.0f), look.Size);
        Assert.Same(ShapeKits.Bulldozer, EntityBodyLayer3D.KitOf(look));
        Assert.True(ShapeKits.Bulldozer.Parts.Length >= 30, "a modelled machine, not a box");
        Assert.False(ShapeKits.Bulldozer.IsArticulated, "no turret and no gun");
        // yellow where it is told (the TKB's own colour)
        Assert.True(look.Colour.R > 200 && look.Colour.G > 150 && look.Colour.B < 60, $"construction yellow, got {look.Colour}");
    }

    [Fact]
    public void TheBulldozerKit_LiesInsideItsBox_TheBladeAtThe_FrontEdge_TheRipperAtTheRear_AllAboveTheGround()
    {
        var size = new Vector3(8.0f, 4.3f, 4.0f);
        float minX = float.MaxValue, maxX = float.MinValue, maxY = 0f, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var part in ShapeKits.Bulldozer.Parts)
        {
            var m = part.BodyTransform(size);
            for (int i = 0; i < 8; i++)
            {
                // the unit mesh: a box is -0.5…0.5 on each axis, a cylinder / cone 0…1 along z (radius 0.5)
                var local = part.Shape == PartShape.Box
                    ? new Vector3((i & 1) - 0.5f, ((i >> 1) & 1) - 0.5f, ((i >> 2) & 1) - 0.5f)
                    : new Vector3(((i & 1) - 0.5f), (((i >> 1) & 1) - 0.5f), (i >> 2) & 1);
                var w = Vector3.Transform(local, m);
                minX = MathF.Min(minX, w.X); maxX = MathF.Max(maxX, w.X); maxY = MathF.Max(maxY, MathF.Abs(w.Y));
                minZ = MathF.Min(minZ, w.Z); maxZ = MathF.Max(maxZ, w.Z);
            }
        }
        Assert.InRange(maxX, 3.8f, 4.05f);    // the blade's cutting edge is the front of the machine
        Assert.InRange(minX, -4.05f, -3.6f);  // the ripper's beam the rear
        Assert.InRange(maxY, 2.0f, 2.2f);     // the blade is the widest part: 4.3 m
        Assert.InRange(minZ, -0.05f, 0.05f);  // standing on the ground
        Assert.InRange(maxZ, 3.4f, 4.05f);    // the ROPS roof / the exhaust stack
    }

    [SkippableFact]
    public void TheBulldozer_IsDrawn_ByTheRealFrame_Side_ThreeQuarter_Rear_AndFromAbove()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);
        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        var tkb = Map3DTests.BuiltIn();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 0.0 });
        var query = WorldQuery.Of(world)!;
        var spot = OpenGround(terrain, query);
        float G(float x, float y) => query.GroundHeightAt(x, y);
        var scale = new List<Entity>();
        void Put(long type, float dx, float dy, bool forScale = false)
        {
            var e = world.CreateEntity();
            float x = spot.X + dx, y = spot.Y + dy;
            world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, G(x, y)), Rotation = Quaternion.Identity });
            world.AddComponent(e, new TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)(type) });
            if (forScale) scale.Add(e);
        }
        Put(TkbEntityTypes.Engineer_Bulldozer, 0f, 0f);
        Put(TkbEntityTypes.Tank_T72, 0f, 12f, forScale: true);                  // for scale: a tank beside it
        Put(TkbEntityTypes.Infantry_Rifleman, 6f, -5f, forScale: true);         // and a man in front of it

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
        void Shot(string name, float lookX, float lookY, float lookUp, float dist, float yaw, float pitch)
        {
            sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X + lookX, spot.Y + lookY, ground + lookUp), dist, yaw, pitch);
            frame.Step(canvas.Draw);
            frame.Step(canvas.Draw);
            frame.Screenshot(Path.Combine(shots, name));
        }
        Shot("dozer-1-side.png", 0f, 0f, 1.8f, 17f, MathF.PI * 0.5f, -0.08f);
        Shot("dozer-2-front34.png", 0f, 0f, 1.8f, 17f, MathF.PI * 0.5f + 0.75f, -0.30f);
        Shot("dozer-3-rear34.png", 0f, 0f, 1.8f, 17f, MathF.PI * 0.5f - 0.75f, -0.30f);
        Shot("dozer-4-above.png", 0f, 0f, 1.0f, 20f, MathF.PI * 0.5f, -1.2f);
        Assert.Equal(3, bodies.BodiesDrawn);
        // the gallery's picture (scripts/thumbnails.py --add-image): the dozer alone, three-quarter from the front
        foreach (var e in scale) world.DestroyEntity(e);
        Shot("dozer-5-thumb.png", 0f, 0f, 1.7f, 15f, MathF.PI * 0.5f + 0.7f, -0.26f);
        Assert.Equal(1, bodies.BodiesDrawn);
    }

    private static Vector2 OpenGround(Fdp.Toolkit.Terrain.TerrainWorld terrain, IWorldQuery query)
    {
        var mid = (terrain.BoundsMin + terrain.BoundsMax) / 2f;
        for (float r = 0f; r < 400f; r += 6f)
            for (int k = 0; k < 16; k++)
            {
                float a = k * MathF.PI / 8f;
                var p = mid + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
                bool open = true;
                for (float dx = -30f; dx <= 15f && open; dx += 3f)
                    for (float dy = -12f; dy <= 14f && open; dy += 3f)
                        open = MathF.Abs(query.SurfaceZ(p.X + dx, p.Y + dy, 0) - query.GroundHeightAt(p.X + dx, p.Y + dy)) < 0.2f;
                if (open) return p;
            }
        return mid;
    }
}
