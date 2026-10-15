using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Fdp.Toolkit.Physics.BodyParts;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐ <c>CE-3211</c> (backend, cross-lane rail in ui's suite; docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md §9 S2-2) — the
/// boxes a round meets on a ground vehicle (<see cref="VehicleBody"/>, Fdp.Toolkits) are the parts the 3-D map draws (<see cref="ShapeKits"/>
/// placed by <see cref="KitFromTkb"/>, posed by <see cref="KitPart.BodyTransform(Vector3, KitPivots, ArticulationPose)"/>), for EVERY
/// catalog type of the tracked and wheeled families, at three turret poses. ⭐ The sim's kits are GENERATED from ui's:
/// <c>HROT_GEN_VEHICLE_BODY=1 dotnet test --filter VehicleBodyParity</c> rewrites <c>VehicleBody.Tables.g.cs</c>. 🔴 Change a kit, the
/// kit placement or a catalog type's size without regenerating and this rail fails — a round would no longer land where the part is drawn.
/// </summary>
public class VehicleBodyParityTests
{
    /// <summary>The families S2 covers (S2-1), in the generated table's order.</summary>
    private static readonly VisualFamily[] Families =
        { VisualFamily.Tank, VisualFamily.Afv, VisualFamily.SpArtillery, VisualFamily.WheeledCar, VisualFamily.WheeledUtility };

    /// <summary>The parts that collide: hull, turret and gun parts (spokes, rotors, gear and limbs do not).</summary>
    private static bool Collides(KitPart p) => p.Role is PartRole.Hull or PartRole.Turret or PartRole.Gun;

    /// <summary>S2-3 — a part's id: the turret and gun by role; a dark hull part low and to one side (a track, a wheel) is the running gear
    /// on its side (+y = left); every other hull part is the hull.</summary>
    private static byte IdOf(KitPart p) => p.Role switch
    {
        PartRole.Turret => VehiclePart.Turret,
        PartRole.Gun => VehiclePart.Gun,
        _ when p.Colour == ColourRole.Dark && p.Centre.Z < 0.3f && MathF.Abs(p.Centre.Y) >= 0.25f
            => p.Centre.Y > 0f ? VehiclePart.RunningGearL : VehiclePart.RunningGearR,
        _ => VehiclePart.Hull,
    };

    /// <summary>The turret poses checked — ⭐ the last two are a gun LAID as a howitzer is (25°, 60°): ui's CE-1114 draws the SP gun from the
    /// replicated <c>TurretPose</c>, so the drawn lay and the sim's boxes must agree there too (ui steer, `2026-10-15`).</summary>
    private static readonly (float Az, float El)[] Poses = { (0f, 0f), (0.7f, 0.1f), (-1.9f, 0.25f), (0.3f, 0.436f), (0f, 1.047f) };

    [Fact]
    public void CE3211_TheBoxesTheRoundsMeet_AreThePartsTheMapDraws_ForEveryCatalogVehicle()
    {
        if (Environment.GetEnvironmentVariable("HROT_GEN_VEHICLE_BODY") == "1") Generate();

        // ① the generated kits are ui's kits, part by part
        foreach (var family in Families)
        {
            var kit = ShapeKits.For(family);
            var sim = VehicleBody.KitNamed(family.ToString());
            Assert.True(sim != null, $"{family}: no generated kit — regenerate (HROT_GEN_VEHICLE_BODY=1)");
            var drawn = kit.Parts.Where(Collides).ToArray();
            Assert.True(drawn.Length == sim!.Parts.Length, $"{family}: {sim.Parts.Length} parts vs {drawn.Length} drawn — regenerate");
            Assert.Equal(ShapeKits.DefaultSize(family), sim.DefaultSize);
            for (int i = 0; i < drawn.Length; i++)
            {
                Assert.Equal(IdOf(drawn[i]), sim.Parts[i].Part);
                Assert.Equal(drawn[i].Centre, sim.Parts[i].Centre);
                Assert.Equal(drawn[i].Extent, sim.Parts[i].Extent);
            }
        }

        // ② every catalog vehicle of those families: the same kit, the same size, and the same posed boxes at three turret poses
        var db = Map3DTests.BuiltIn();
        int checkedTypes = 0;
        foreach (var t in db.GetAll())
        {
            var look = EntityBodyLayer3D.Resolve(t);
            var shape = VehicleBody.Of(t);
            bool inScope = Families.Contains(look.Family) && t.GetDescriptor<CombatPlatformDefDto>() != null;
            Assert.True(inScope == (shape != null),
                $"{t.Name} ({t.DisType.Kind}.{t.DisType.Domain}.{t.DisType.Country}.{t.DisType.Category}): drawn as {look.Family}, sim shape {shape?.Kit.Name ?? "none"}");
            if (shape == null) continue;
            checkedTypes++;
            Assert.Equal(look.Family.ToString(), shape.Kit.Name);
            Assert.True(Vector3.Distance(look.Size, shape.Size) < 1e-4f, $"{t.Name}: sim size {shape.Size} vs drawn {look.Size}");

            var (parts, pivots) = KitFromTkb.For(ShapeKits.For(look.Family), t, look.Size);
            var drawn = parts.Where(Collides).ToArray();
            var kitToBody = look.Geometry is { } g
                ? Matrix4x4.CreateTranslation(g.BodyCentreX, g.BodyCentreY, g.BodyCentreZ - look.Size.Z / 2f) : Matrix4x4.Identity;
            Span<BodyBox> boxes = stackalloc BodyBox[VehicleBody.MaxBoxes];
            foreach (var (az, el) in Poses)
            {
                var pose = ShapeKits.For(look.Family).IsArticulated ? new ArticulationPose(az, el) : ArticulationPose.Neutral;
                int n = VehicleBody.Pose(shape, look.Size, az, el, boxes);
                Assert.Equal(drawn.Length, n);
                for (int i = 0; i < n; i++)
                {
                    var m = drawn[i].BodyTransform(look.Size, pivots, pose) * kitToBody;
                    var want = VehicleBody.BoxOf(IdOf(drawn[i]), drawn[i].Shape == PartShape.Box ? KitShape.Box : KitShape.Cylinder, m);
                    var got = boxes[i];
                    string at = $"{t.Name} part {i} ({VehiclePart.NameOf(want.Part)}) at az {az} el {el}";
                    Assert.True(Vector3.Distance(want.Centre, got.Centre) < 1e-3f, $"{at}: centre {got.Centre} vs drawn {want.Centre}");
                    Assert.True(Vector3.Distance(want.Half, got.Half) < 1e-3f, $"{at}: half {got.Half} vs drawn {want.Half}");
                    Assert.True(MathF.Abs(Quaternion.Dot(want.Rotation, got.Rotation)) > 0.9999f, $"{at}: rotation differs");
                    // the bound sphere holds every posed box
                    var (bc, br) = (shape.BoundCentre, shape.BoundRadius);
                    Assert.True(Vector3.Distance(got.Centre, bc) + got.Half.Length() <= br + 1e-3f, $"{at}: outside the bound sphere");
                }
            }
        }
        Assert.True(checkedTypes >= 5, $"only {checkedTypes} catalog vehicles checked");
    }

    private static void Generate()
    {
        static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture) + "f";
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated> by Hrot.Presentation.Tests VehicleBodyParityTests (HROT_GEN_VEHICLE_BODY=1) from ui's ShapeKits —");
        sb.AppendLine("//   do not edit by hand; CE-3211, docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md §9 S2-2 </auto-generated>");
        sb.AppendLine("using System.Numerics;");
        sb.AppendLine();
        sb.AppendLine("namespace Fdp.Toolkit.Physics.BodyParts");
        sb.AppendLine("{");
        sb.AppendLine("    public static partial class VehicleBody");
        sb.AppendLine("    {");
        sb.AppendLine("        /// <summary>The ground-vehicle kits, generated from ui's <c>ShapeKits</c> (the colliding parts, in their order).</summary>");
        sb.AppendLine("        public static readonly VehicleKit[] Kits =");
        sb.AppendLine("        {");
        foreach (var family in Families)
        {
            var kit = ShapeKits.For(family);
            var size = ShapeKits.DefaultSize(family);
            var parts = kit.Parts.Where(Collides).ToArray();
            // the main hull box: the largest hull-id box — where the engine region lies (S2-4)
            int engine = -1;
            float best = 0f;
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i];
                float v = p.Extent.X * p.Extent.Y * p.Extent.Z;
                if (p.Shape == PartShape.Box && IdOf(p) == VehiclePart.Hull && v > best) { best = v; engine = i; }
            }
            sb.AppendLine($"            new(\"{family}\", new({F(size.X)}, {F(size.Y)}, {F(size.Z)}), new({F(kit.Pivots.Turret.X)}, {F(kit.Pivots.Turret.Y)}), " +
                          $"new({F(kit.Pivots.GunTrunnionXZ.X)}, {F(kit.Pivots.GunTrunnionXZ.Y)}), new VehicleKitPart[]");
            sb.AppendLine("            {");
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i];
                byte id = IdOf(p);
                string role = p.Role switch { PartRole.Turret => "KitRole.Turret", PartRole.Gun => "KitRole.Gun", _ => "KitRole.Hull" };
                sb.AppendLine($"                new({id}, KitShape.{p.Shape}, new({F(p.Centre.X)}, {F(p.Centre.Y)}, {F(p.Centre.Z)}), " +
                              $"new({F(p.Extent.X)}, {F(p.Extent.Y)}, {F(p.Extent.Z)}), KitAxis.{p.Axis}, {F(p.PitchDegrees)}, {F(p.YawDegrees)}, {role}" +
                              (i == engine ? ", EngineBox: true" : "") + $"),   // {VehiclePart.NameOf(id)}");
            }
            sb.AppendLine("            }),");
        }
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "HROT.sln"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        File.WriteAllText(Path.Combine(dir!, "FDP", "Toolkits", "Fdp.Toolkits", "Physics", "BodyParts", "VehicleBody.Tables.g.cs"), sb.ToString());
    }
}
