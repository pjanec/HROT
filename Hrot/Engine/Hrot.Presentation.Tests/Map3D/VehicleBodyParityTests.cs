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
        { VisualFamily.Tank, VisualFamily.Afv, VisualFamily.SpArtillery, VisualFamily.WheeledCar, VisualFamily.WheeledUtility,
          VisualFamily.Helicopter, VisualFamily.AttackHelicopter, VisualFamily.Jet, VisualFamily.AttackJet, VisualFamily.CargoPlane };   // ⭐ CE-3215

    /// <summary>
    /// ⭐ <c>CE-3215</c> (§12 S2c-2) — an AIRCRAFT kit's part ids, by the index of its colliding parts: the air kits carry no roles that name
    /// a fuselage, wing, engine or tail, so the map is explicit — and checked (<see cref="CheckAircraftIds"/>): a glass part is the Cockpit,
    /// a turret / gun role its id, and the map is as long as the kit.
    /// </summary>
    private static readonly Dictionary<VisualFamily, byte[]> AircraftIds = new()
    {
        [VisualFamily.Helicopter] = new byte[] { 0, 1, 4, 4, 3 },                    // fuselage, cockpit, tail boom, fin, mast
        [VisualFamily.Jet] = new byte[] { 0, 0, 1, 2, 2, 4, 4, 4, 3 },               // fuselage, nose, canopy, wings, tailplanes, fin, nozzle
        [VisualFamily.CargoPlane] = new byte[] { 0, 0, 1, 2, 3, 3, 3, 3, 4, 4, 4 },  // fuselage, nose, cockpit, wing, 4 engines, tail, fin, tailplane
        [VisualFamily.AttackJet] = new byte[] { 0, 0, 6, 1, 2, 3, 3, 4, 4, 4 },      // fuselage, nose, the GAU-8, canopy, wing, 2 engines, tailplane, 2 fins
        [VisualFamily.AttackHelicopter] = new byte[]                                  // fuselage, nose, sensor ball, 2 canopies, 2 nacelles, pylon, mast,
            { 0, 0, 1, 1, 1, 3, 3, 3, 3, 4, 4, 4, 2, 2, 7, 7, 7, 7, 5, 6 },          //   boom, fin, stabiliser, 2 stub wings, 2 pods, 2 rails, turret, barrel
    };

    private static byte IdOf(VisualFamily family, int i, KitPart p) => AircraftIds.TryGetValue(family, out var ids) ? ids[i] : IdOf(p);

    private static void CheckAircraftIds(VisualFamily family, KitPart[] drawn)
    {
        if (!AircraftIds.TryGetValue(family, out var ids)) return;
        Assert.True(ids.Length == drawn.Length, $"{family}: {ids.Length} ids for {drawn.Length} colliding parts — update the map");
        for (int i = 0; i < drawn.Length; i++)
        {
            if (drawn[i].Colour == ColourRole.Glass) Assert.True(ids[i] == AircraftPart.Cockpit, $"{family} part {i} is glass, mapped {AircraftPart.NameOf(ids[i])}");
            if (drawn[i].Role == PartRole.Turret) Assert.Equal(AircraftPart.Turret, ids[i]);
            if (drawn[i].Role == PartRole.Gun) Assert.Equal(AircraftPart.Gun, ids[i]);
        }
    }

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
    public void CE3211_TheBoxesTheRoundsMeet_AreThePartsTheMapDraws_ForEveryCatalogVehicle()   // ⭐ CE-3215 — and every catalog aircraft
    {
        if (Environment.GetEnvironmentVariable("HROT_GEN_VEHICLE_BODY") == "1") Generate();

        // ① the generated kits are ui's kits, part by part
        foreach (var family in Families)
        {
            var kit = ShapeKits.For(family);
            var sim = VehicleBody.KitNamed(family.ToString());
            Assert.True(sim != null, $"{family}: no generated kit — regenerate (HROT_GEN_VEHICLE_BODY=1)");
            var drawn = kit.Parts.Where(Collides).ToArray();
            CheckAircraftIds(family, drawn);   // ⭐ CE-3215
            Assert.True(drawn.Length == sim!.Parts.Length, $"{family}: {sim.Parts.Length} parts vs {drawn.Length} drawn — regenerate");
            Assert.Equal(ShapeKits.DefaultSize(family), sim.DefaultSize);
            for (int i = 0; i < drawn.Length; i++)
            {
                Assert.Equal(IdOf(family, i, drawn[i]), sim.Parts[i].Part);
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
            if (look.Family == VisualFamily.WeaponSystem) continue;   // ⭐ CE-3214 — the weapon rail below
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
                    var want = VehicleBody.BoxOf(IdOf(look.Family, i, drawn[i]), drawn[i].Shape == PartShape.Box ? KitShape.Box : KitShape.Cylinder, m);
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

    // ── ⭐⭐ CE-3214 (§11 S2b) — weapon systems: the same rail for the stowed and deployed kits of every WeaponKind ───────────────────

    /// <summary>S2b-3 — a weapon part's id, by its INDEX in the deployed kit (both kits list the same parts): Turret ⇒ Traverse, Gun ⇒ Weapon,
    /// else Mount; a kit with no articulated part (a tube fired from the shoulder) is all weapon.</summary>
    private static byte WeaponIdOf(ShapeKit deployed, int i) => !deployed.IsArticulated ? WeaponPart.Weapon : deployed.Parts[i].Role switch
    {
        PartRole.Turret => WeaponPart.Traverse,
        PartRole.Gun => WeaponPart.Weapon,
        _ => WeaponPart.Mount,
    };

    /// <summary>The deploy fractions checked: stowed, half-way, deployed.</summary>
    private static readonly float[] Deploys = { 0f, 0.5f, 1f };

    /// <summary>
    /// ⭐⭐ <c>CE-3214</c> — the boxes a round meets on a weapon system are the parts the map draws: the generated kits are ui's
    /// <see cref="WeaponKits"/> part by part, and for EVERY catalog weapon type the sim picks the same kind and size and poses the same boxes
    /// stowed, half-deployed and deployed (laid three ways), inside the bound sphere.
    /// </summary>
    [Fact]
    public void CE3214_TheBoxesTheRoundsMeet_AreThePartsTheMapDraws_ForEveryCatalogWeapon()
    {
        if (Environment.GetEnvironmentVariable("HROT_GEN_VEHICLE_BODY") == "1") Generate();

        foreach (var kind in Enum.GetValues<WeaponKind>())
        {
            var sim = VehicleBody.WeaponKitNamed(kind.ToString());
            Assert.True(sim != null, $"{kind}: no generated weapon kit — regenerate (HROT_GEN_VEHICLE_BODY=1)");
            var deployed = WeaponKits.Kit(kind, true);
            foreach (var (drawnKit, simKit) in new[] { (WeaponKits.Kit(kind, false), sim!.Value.Stowed), (deployed, sim.Value.Deployed) })
            {
                var drawn = drawnKit.Parts.Where(Collides).ToArray();
                Assert.True(drawn.Length == simKit.Parts.Length, $"{kind}: {simKit.Parts.Length} parts vs {drawn.Length} drawn — regenerate");
                Assert.Equal(WeaponKits.DefaultSize(kind), simKit.DefaultSize);
                for (int i = 0; i < drawn.Length; i++)
                {
                    Assert.Equal(WeaponIdOf(deployed, i), simKit.Parts[i].Part);
                    Assert.Equal(drawn[i].Centre, simKit.Parts[i].Centre);
                    Assert.Equal(drawn[i].Extent, simKit.Parts[i].Extent);
                }
            }
            Assert.Equal(WeaponKits.DefaultElevation(kind), sim.Value.Deployed.RestElevation);
        }

        var db = Map3DTests.BuiltIn();
        int checkedTypes = 0;
        foreach (var t in db.GetAll())
        {
            var look = EntityBodyLayer3D.Resolve(t);
            if (look.Family != VisualFamily.WeaponSystem || t.GetDescriptor<CombatPlatformDefDto>() == null) continue;
            var shape = VehicleBody.Of(t);
            Assert.True(shape is { Stowed: not null }, $"{t.Name}: drawn as a {look.Weapon} weapon, no sim weapon shape ({shape?.Kit.Name ?? "none"})");
            checkedTypes++;
            Assert.Equal(look.Weapon.ToString(), shape!.Kit.Name);
            Assert.True(Vector3.Distance(look.Size, shape.Size) < 1e-4f, $"{t.Name}: sim size {shape.Size} vs drawn {look.Size}");
            var kitToBody = look.Geometry is { } g
                ? Matrix4x4.CreateTranslation(g.BodyCentreX, g.BodyCentreY, g.BodyCentreZ - look.Size.Z / 2f) : Matrix4x4.Identity;
            Span<BodyBox> boxes = stackalloc BodyBox[VehicleBody.MaxBoxes];
            foreach (var t01 in Deploys)
                foreach (var (az, el) in t01 >= 1f ? Poses : new[] { (0f, 0f) })
                {
                    var kit = WeaponKits.Blend(look.Weapon, t01);
                    var drawn = kit.Parts.Where(Collides).ToArray();
                    var pose = t01 >= 1f && kit.IsArticulated ? new ArticulationPose(az, el) : ArticulationPose.Neutral;
                    int n = VehicleBody.Pose(shape, look.Size, az, el, t01, boxes);
                    Assert.Equal(drawn.Length, n);
                    for (int i = 0; i < n; i++)
                    {
                        var m = drawn[i].BodyTransform(look.Size, kit.Pivots, pose) * kitToBody;
                        var want = VehicleBody.BoxOf(0, drawn[i].Shape == PartShape.Box ? KitShape.Box : KitShape.Cylinder, m);
                        var got = boxes[i];
                        string at = $"{t.Name} part {i} ({WeaponPart.NameOf(got.Part)}) deployed {t01} az {az} el {el}";
                        Assert.True(Vector3.Distance(want.Centre, got.Centre) < 1e-3f, $"{at}: centre {got.Centre} vs drawn {want.Centre}");
                        Assert.True(Vector3.Distance(want.Half, got.Half) < 1e-3f, $"{at}: half {got.Half} vs drawn {want.Half}");
                        Assert.True(MathF.Abs(Quaternion.Dot(want.Rotation, got.Rotation)) > 0.9999f, $"{at}: rotation differs");
                        Assert.True(Vector3.Distance(got.Centre, shape.BoundCentre) + got.Half.Length() <= shape.BoundRadius + 1e-3f, $"{at}: outside the bound sphere");
                    }
                }
        }
        Assert.True(checkedTypes >= 10, $"only {checkedTypes} catalog weapons checked");
    }

    private static void GenerateWeapons(string root)
    {
        // the weapon kits are COMPUTED (metres ÷ size): printed round-trip, or the parts would not be ui's exactly
        static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture) + "f";
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated> by Hrot.Presentation.Tests VehicleBodyParityTests (HROT_GEN_VEHICLE_BODY=1) from ui's WeaponKits —");
        sb.AppendLine("//   do not edit by hand; CE-3214, docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md §11 S2b-2 </auto-generated>");
        sb.AppendLine("using System.Numerics;");
        sb.AppendLine();
        sb.AppendLine("namespace Fdp.Toolkit.Physics.BodyParts");
        sb.AppendLine("{");
        sb.AppendLine("    public static partial class VehicleBody");
        sb.AppendLine("    {");
        sb.AppendLine("        /// <summary>The weapon-system kits (stowed, deployed) per kind, generated from ui's <c>WeaponKits</c>.</summary>");
        sb.AppendLine("        public static readonly (VehicleKit Stowed, VehicleKit Deployed)[] WeaponKits =");
        sb.AppendLine("        {");
        foreach (var kind in Enum.GetValues<WeaponKind>())
        {
            var size = WeaponKits.DefaultSize(kind);
            var deployed = WeaponKits.Kit(kind, true);
            sb.AppendLine("            (");
            foreach (var stowed in new[] { true, false })
            {
                var kit = WeaponKits.Kit(kind, !stowed);
                var parts = kit.Parts.Where(Collides).ToArray();
                float rest = stowed ? 0f : WeaponKits.DefaultElevation(kind);
                sb.AppendLine($"                new(\"{kind}\", new({F(size.X)}, {F(size.Y)}, {F(size.Z)}), new({F(kit.Pivots.Turret.X)}, {F(kit.Pivots.Turret.Y)}), " +
                              $"new({F(kit.Pivots.GunTrunnionXZ.X)}, {F(kit.Pivots.GunTrunnionXZ.Y)}), new VehicleKitPart[]");
                sb.AppendLine("                {");
                for (int i = 0; i < parts.Length; i++)
                {
                    var p = parts[i];
                    byte id = WeaponIdOf(deployed, i);
                    string role = p.Role switch { PartRole.Turret => "KitRole.Turret", PartRole.Gun => "KitRole.Gun", _ => "KitRole.Hull" };
                    sb.AppendLine($"                    new({id}, KitShape.{p.Shape}, new({F(p.Centre.X)}, {F(p.Centre.Y)}, {F(p.Centre.Z)}), " +
                                  $"new({F(p.Extent.X)}, {F(p.Extent.Y)}, {F(p.Extent.Z)}), KitAxis.{p.Axis}, {F(p.PitchDegrees)}, {F(p.YawDegrees)}, {role}),   // {WeaponPart.NameOf(id)}");
                }
                sb.AppendLine("                }" + (rest != 0f ? $", RestElevation: {F(rest)}" : "") + ")" + (stowed ? "," : ""));
            }
            sb.AppendLine("            ),");
        }
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        File.WriteAllText(Path.Combine(root, "FDP", "Toolkits", "Fdp.Toolkits", "Physics", "BodyParts", "VehicleBody.WeaponKits.g.cs"), sb.ToString());
    }

    private static void Generate()
    {
        // ⭐ CE-3215 — round-trip: the AH-64's kit is COMPUTED (its own metres ÷ its box), so 5 decimals were not ui's parts exactly
        static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture) + "f";
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
            CheckAircraftIds(family, parts);   // ⭐ CE-3215 — a map that no longer fits the kit is not written
            // the main hull box: the largest hull-id box — where the engine region lies (S2-4); an aircraft has none
            int engine = -1;
            float best = 0f;
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i];
                float v = p.Extent.X * p.Extent.Y * p.Extent.Z;
                if (!AircraftIds.ContainsKey(family) && p.Shape == PartShape.Box && IdOf(p) == VehiclePart.Hull && v > best) { best = v; engine = i; }
            }
            sb.AppendLine($"            new(\"{family}\", new({F(size.X)}, {F(size.Y)}, {F(size.Z)}), new({F(kit.Pivots.Turret.X)}, {F(kit.Pivots.Turret.Y)}), " +
                          $"new({F(kit.Pivots.GunTrunnionXZ.X)}, {F(kit.Pivots.GunTrunnionXZ.Y)}), new VehicleKitPart[]");
            sb.AppendLine("            {");
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i];
                byte id = IdOf(family, i, p);
                string role = p.Role switch { PartRole.Turret => "KitRole.Turret", PartRole.Gun => "KitRole.Gun", _ => "KitRole.Hull" };
                sb.AppendLine($"                new({id}, KitShape.{p.Shape}, new({F(p.Centre.X)}, {F(p.Centre.Y)}, {F(p.Centre.Z)}), " +
                              $"new({F(p.Extent.X)}, {F(p.Extent.Y)}, {F(p.Extent.Z)}), KitAxis.{p.Axis}, {F(p.PitchDegrees)}, {F(p.YawDegrees)}, {role}" +
                              (i == engine ? ", EngineBox: true" : "") + $"),   // {(AircraftIds.ContainsKey(family) ? AircraftPart.NameOf(id) : VehiclePart.NameOf(id))}");
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
        GenerateWeapons(dir!);   // ⭐ CE-3214
    }
}
