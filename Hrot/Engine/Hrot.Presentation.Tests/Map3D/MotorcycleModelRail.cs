using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Hrot.Core.Tkb;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Common;
using Hrot.UI.Common.Map3D;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐ <c>CE-4621</c> (motorcycle, docs/designs/vehicle-tactics/DESIGN_Motorcycle.md §2a, §6 M-1v; mockup <c>motorcycle-visuals.svg</c>; 🔒 user, 2026-11-15: <i>"the UI might take the visual part
/// of it"</i>) — the motorcycle's 3-D model: the family and its kit (TWO wheels that roll, a tank, a long seat), the bank into a turn (a drawing only, 0 B), the two seated riders
/// (<see cref="SeatPose.Ride"/>: the driver on the bars, a pillion holding on) and the pillion FIRING — his upper body turned to the aim, the rifle shouldered — on the real frame.
/// ✅ Type 115 / class <c>Motorcycle</c> / the seats are backend's (CE-4621 M-1 / M-2, merged): the rail draws the REAL type through the real catalogue. ⛔ The frame tests SKIP without a display:
/// <c>xvfb-run -a dotnet test … --filter FullyQualifiedName~MotorcycleModelRail</c>. 🔴 Red-proof: the bank removed from the layer fails the bank rail; the twist removed from
/// <c>SeatedMotion</c> fails the firing-pose rail.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class MotorcycleModelRail
{
    /// <summary>⭐ Backend's REAL type 115 (CE-4621 M-1 / M-2: DIS 1.1.0.80.1, class <c>Motorcycle</c>, seat 0 the rider / Driver — fires only stopped, an arc −120…120 —, seat 1 the pillion, a firing seat 30…330), through the real catalogue.</summary>
    internal const long StandIn = TkbEntityTypes.Motorcycle;
    private static readonly Vector3 Size = new(2.1f, 0.8f, 1.2f);

    internal static Fdp.Toolkit.Tkb.TkbDatabase WithStandIn() => Map3DTests.BuiltIn();

    // ── the kit ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheMotorcycle_HasItsOwnFamily_KitAndSize_TwoWheelsThatRoll_NoTurret_AndAPickerGlyphWithArt()
    {
        Assert.Same(ShapeKits.Motorcycle, ShapeKits.For(VisualFamily.Motorcycle));
        Assert.Equal(Size, ShapeKits.DefaultSize(VisualFamily.Motorcycle));
        var parts = ShapeKits.Motorcycle.Parts;
        Assert.InRange(parts.Length, 25, 60);
        Assert.Equal(2, parts.Count(p => p.Role == PartRole.WheelSpoke));      // TWO wheels (the car kits draw four)
        Assert.Equal(2, parts.Count(p => p.Shape == PartShape.Cylinder && p.Axis == PartAxis.Y));
        Assert.False(ShapeKits.Motorcycle.IsArticulated, "no turret");
        Assert.NotEqual(EntityBodyLayer3D.DefaultColour(VisualFamily.Unknown), EntityBodyLayer3D.DefaultColour(VisualFamily.Motorcycle));
        Assert.Equal("_motorcycle", VisualFamilies.IconName(VisualFamily.Motorcycle));
        string repo = RepoRoot();
        Assert.True(File.Exists(Path.Combine(repo, "assets", "icons", "_motorcycle.png")), "the picker glyph's art (rendered from src/_motorcycle.svg)");
        Assert.True(File.Exists(Path.Combine(repo, "assets", "icons", "src", "_motorcycle.svg")));
    }

    [Fact]
    public void TheKit_LiesInsideItsBox_OnTheGround_TheFrontWheelAheadOfTheRear_TheBarsTopMost()
    {
        float minX = float.MaxValue, maxX = float.MinValue, maxY = 0f, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var part in ShapeKits.Motorcycle.Parts)
        {
            var m = part.BodyTransform(Size);
            for (int i = 0; i < 8; i++)
            {
                var local = part.Shape == PartShape.Box
                    ? new Vector3((i & 1) - 0.5f, ((i >> 1) & 1) - 0.5f, ((i >> 2) & 1) - 0.5f)
                    : new Vector3((i & 1) - 0.5f, ((i >> 1) & 1) - 0.5f, (i >> 2) & 1);
                var w = Vector3.Transform(local, m);
                minX = MathF.Min(minX, w.X); maxX = MathF.Max(maxX, w.X); maxY = MathF.Max(maxY, MathF.Abs(w.Y));
                minZ = MathF.Min(minZ, w.Z); maxZ = MathF.Max(maxZ, w.Z);
            }
        }
        Assert.InRange(maxX, 1.0f, 1.1f);      // the front tyre
        Assert.InRange(minX, -1.15f, -0.95f);  // the rack / rear tyre
        Assert.InRange(maxY, 0.3f, 0.45f);     // the bars' grips
        Assert.InRange(minZ, -0.05f, 0.05f);   // on its wheels
        Assert.InRange(maxZ, 1.0f, 1.25f);     // the bars
        var wheels = ShapeKits.Motorcycle.Parts.Where(p => p.Role == PartRole.WheelSpoke).Select(p => p.BodyTransform(Size).Translation).OrderBy(t => t.X).ToArray();
        Assert.InRange(wheels[1].X - wheels[0].X, 1.3f, 1.5f);   // a 1.4 m wheelbase
    }

    /// <summary>⭐ CE-4621 — backend's class <c>Motorcycle</c> (DIS <c>1.1.*.80</c>, type 115) maps to the family, and the REAL type's family, size, seats and kit are the bike's: the kit's metres ARE the TKB's metres, both seats are astride, the hips are where the kit's seat is.</summary>
    [Fact]
    public void TheClass_MapsToTheFamily_AndTheRealType115IsTheBike_ItsSeatsAstride_ItsHipsOnTheKitsSeat()
    {
        Assert.Equal(VisualFamily.Motorcycle, VisualFamilies.FamilyOfClass["Motorcycle"]);
        var bike = Map3DTests.BuiltIn().GetByType(TkbEntityTypes.Motorcycle)!;
        Assert.Equal(VisualFamily.Motorcycle, VisualFamilies.Of(bike));
        var tkb = Map3DTests.BuiltIn();
        using var world = new EntityRepository();
        var look = new EntityBodyLayer3D(() => world, () => tkb, (w, e) => ArticulationPose.Neutral).LookOf(TkbEntityTypes.Motorcycle, tkb);
        Assert.Equal(VisualFamily.Motorcycle, look.Family);
        Assert.InRange(look.Size.X, Size.X - 0.01f, Size.X + 0.01f); Assert.InRange(look.Size.Y, Size.Y - 0.01f, Size.Y + 0.01f);
        Assert.InRange(look.Size.Z, Size.Z - 0.1f, Size.Z + 0.1f);   // the TKB's box wins: the kit's metres are the TKB's metres (its 1.2 m height may come from the render model)
        var seats = bike.GetDescriptor<CarrierDefDto>()!.Seats;
        Assert.Equal(2, seats.Count);
        Assert.All(seats, s => Assert.Equal(SeatPose.Ride, s.Pose));
        // the kit's seat carries them: the rider's hips (z 0.75) sit on the low seat (top 0.705), the pillion's (z 0.85) on the raised pad (top 0.805)
        float TopOfSeatAt(float x) => ShapeKits.Motorcycle.Parts
            .Where(p => p.Colour == ColourRole.Dark && p.Shape == PartShape.Box && MathF.Abs(p.Extent.Y * Size.Y - 0.28f) < 0.01f && MathF.Abs((p.Centre.X - x / Size.X) * Size.X) < 0.3f)
            .Max(p => (p.Centre.Z + p.Extent.Z / 2f) * Size.Z);
        Assert.InRange(seats[0].Z - TopOfSeatAt(seats[0].X), 0.0f, 0.12f);
        Assert.InRange(seats[1].Z - TopOfSeatAt(seats[1].X), 0.0f, 0.12f);
    }

    // ── the motion: the wheels roll, a turn banks the body ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheMotion_WheelsRollOnTheBikesRadius_AStraightRunDoesNotBank_ATurnBanksIntoItBothWays_AndCapsAtMaxBank()
    {
        var t = new MotionTracker();
        var e = new Entity(1, 1);
        double sim = 0;
        MotionPose Step(Vector3 pos, float yaw) { sim += 0.05; return t.Advance(e, pos, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw), VisualFamily.Motorcycle, Size, sim, false); }
        float roll0 = Step(Vector3.Zero, 0f).WheelRoll;
        float roll1 = Step(new Vector3(0.31f, 0f, 0f), 0f).WheelRoll;
        Assert.InRange(Wrapped(roll1 - roll0), 0.95f, 1.05f);            // 0.31 m driven = one radian
        for (int i = 0; i < 40; i++) Assert.Equal(0f, Step(new Vector3(1f + i * 0.5f, 0f, 0f), 0f).Bank);   // straight at 10 m/s: upright
        // a LEFT turn (the heading grows) at 10 m/s, 0.4 rad/s: atan(v·ω/g) = 0.39 rad, the top toward the LEFT = a negative turn about +x
        float yaw = 0f; var pos = new Vector3(30f, 0f, 0f); float bank = 0f;
        for (int i = 0; i < 60; i++) { yaw += 0.4f * 0.05f; pos += new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0f) * 0.5f; bank = Step(pos, yaw).Bank; }
        Assert.InRange(bank, -0.42f, -0.36f);
        Assert.Equal(bank, t.BankOf(e));
        // and a RIGHT turn leans the other way
        for (int i = 0; i < 80; i++) { yaw -= 0.4f * 0.05f; pos += new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0f) * 0.5f; bank = Step(pos, yaw).Bank; }
        Assert.InRange(bank, 0.36f, 0.42f);
        // a violent turn is capped (the picture never lies flat)
        for (int i = 0; i < 80; i++) { yaw += 2.0f * 0.05f; pos += new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0f) * 0.5f; bank = Step(pos, yaw).Bank; }
        Assert.Equal(-MotionTracker.MaxBank, bank, 2);
        Assert.Equal(0f, new MotionTracker().BankOf(new Entity(9, 1)));   // never seen: upright
    }

    [Fact]
    public void TheMotion_AllocatesNothing_InTheSteadyState()
    {
        var t = new MotionTracker();
        var e = new Entity(1, 1);
        double sim = 0;
        for (int i = 0; i < 200; i++) { sim += 0.016; t.Advance(e, new Vector3(i * 0.1f, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, i * 0.01f), VisualFamily.Motorcycle, Size, sim, false, sim); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++) { sim += 0.016; t.Advance(e, new Vector3(i * 0.1f, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, i * 0.01f), VisualFamily.Motorcycle, Size, sim, false, sim); _ = t.BankOf(e); }
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);   // ⭐ HOT PATH — 0 B
    }

    // ── the riders ──────────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheRiders_AreTheSeatedFigures_PartForPart_TheDriverOnTheBars_APillionHoldingOn_TheFiringPillionsRifleShouldered()
    {
        foreach (bool soldier in new[] { true, false })
        {
            int n = BlockFigure.Seated(false, soldier).Length;
            Assert.Equal(n, BlockFigure.Riding(true, soldier).Length);       // the same parts in the same order: outfit, blood and rails read it like every figure
            Assert.Equal(n, BlockFigure.Riding(false, soldier).Length);
            Assert.Equal(n, BlockFigure.RidingAiming(soldier).Length);
        }
        // astride: the feet are wide apart (a seated man's are 0.22 m) and the hips are the origin
        var legs = BlockFigure.Riding(true, true).Take(4).Select(p => p.Centre.Y).ToArray();
        Assert.True(MathF.Abs(legs[0]) > 0.1f / 0.55f && MathF.Abs(legs[2]) > 0.1f / 0.55f, "a thigh each side");
        // a firing pillion: the rifle level along +x at shoulder height, the driver's slung across his back (not forward)
        var aimed = BlockFigure.RidingAiming(true)[BlockFigure.WeaponIndex];
        var slung = BlockFigure.Riding(true, true)[BlockFigure.WeaponIndex];
        Assert.True(aimed.Extent.X * 0.35f > 0.6f && MathF.Abs(aimed.PitchDegrees) < 6f && MathF.Abs(aimed.YawDegrees) < 1f, "shouldered, level, pointing forward of the figure");
        Assert.True(MathF.Abs(slung.YawDegrees) > 60f, $"slung across the back, not pointing forward: yaw {slung.YawDegrees}");
        // the driver leans to the bars, a pillion a little (the layer turns these into the motion pose)
        Assert.True(BlockFigure.DriverLean > BlockFigure.PillionLean && BlockFigure.PillionLean > 0f);
    }

    [Fact]
    public void TheTurnToTheAim_IsInTheSeatsFrame_ForwardZero_ASideNinety_TheRearOneEighty_LimitedToTheDrawingsMaximum()
    {
        Assert.True(EntityBodyLayer3D.AimInSeat(new Vector3(20f, 0f, 0.53f), out float fwd, out _));
        Assert.Equal(0f, fwd, 3);
        Assert.True(EntityBodyLayer3D.AimInSeat(new Vector3(0f, 10f, 0.53f), out float left, out _));
        Assert.Equal(MathF.PI / 2f, left, 3);                                      // the left: counter-clockwise
        Assert.True(EntityBodyLayer3D.AimInSeat(new Vector3(0f, -10f, 0.53f), out float right, out _));
        Assert.Equal(-MathF.PI / 2f, right, 3);
        Assert.True(EntityBodyLayer3D.AimInSeat(new Vector3(-10f, 0.001f, 0.53f), out float rear, out _));
        Assert.Equal(EntityBodyLayer3D.MaxSeatTwist, rear, 3);                     // dead astern: the drawing's limit (160°), the seat's arc decides what he may fire at
        Assert.True(EntityBodyLayer3D.AimInSeat(new Vector3(10f, 0f, 5.53f), out _, out float up));
        Assert.InRange(up, 0.4f, EntityBodyLayer3D.MaxAimPitch + 0.001f);
        Assert.False(EntityBodyLayer3D.AimInSeat(new Vector3(0.02f, 0.02f, 0.5f), out _, out _), "no aim at one's own hips");
    }

    /// <summary>⭐ The carriage classifier: a man in a Ride seat is SEATED (the 3-D map draws exposed riders), the seat is the stand-in's, and ⛔ a non-firing seat (the driver's) is never a firing seat.</summary>
    [Fact]
    public void TheCarriage_OfARiderOnTheBike_IsSeated_AtTheSeatsPlace_InTheRidePose()
    {
        using var world = new EntityRepository();
        world.RegisterComponent<SimTransform>(); world.RegisterComponent<TkbIdentity>(); world.RegisterComponent<IsEmbarkedTag>(); world.RegisterComponent<PassengerBuffer>();
        var db = WithStandIn();
        var bike = world.CreateEntity();
        world.AddComponent(bike, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
        world.AddComponent(bike, new TkbIdentity { TkbType = (TkbTypeId)StandIn });
        Entity Rider(byte seat)
        {
            var m = world.CreateEntity();
            world.AddComponent(m, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            world.AddComponent(m, new TkbIdentity { TkbType = (TkbTypeId)TkbEntityTypes.Infantry_Rifleman });
            world.AddComponent(m, new IsEmbarkedTag { VehicleEntity = bike, Exposed = 1, Seat = seat });
            return m;
        }
        var driver = Carriage.Of(world, Rider(0), db); var pillion = Carriage.Of(world, Rider(1), db);
        Assert.Equal((CarriageKind.Seated, SeatPose.Ride, SeatRole.Driver), (driver.Kind, driver.Seat!.Pose, driver.Seat.Role));
        Assert.Equal((CarriageKind.Seated, SeatPose.Ride, SeatRole.Passenger), (pillion.Kind, pillion.Seat!.Pose, pillion.Seat.Role));
        Assert.True(pillion.Seat.X < driver.Seat.X, "the pillion sits behind");
        Assert.True(driver.Seat.FiresStoppedOnly && driver.Seat.TraverseMinDeg != null, "the rider: an arc, fires only stopped (PF-4)");
        Assert.True(pillion.Seat.TraverseMinDeg != null && !pillion.Seat.FiresStoppedOnly, "the pillion: a firing seat");
    }

    // ── ⭐ U-3: the pillion in first person — the HUD cue outside the seat's arc ───────────────────────────────────────────────────

    [Fact]
    public void TheArc_IsReadFromMinRoundToMax_CounterClockwise_ThroughPlusMinus180_AndAFullStationArcStillMeansTheFront()
    {
        // both sides and the rear: written 70 … 290, or 70 … -70 (through ±180°) — one reading
        foreach (var (min, max) in new[] { (70f, 290f), (70f, -70f) })
        {
            Assert.True(PossessionStation.InArc(90f, min, max)); Assert.True(PossessionStation.InArc(180f, min, max)); Assert.True(PossessionStation.InArc(-180f, min, max));
            Assert.True(PossessionStation.InArc(-90f, min, max)); Assert.True(PossessionStation.InArc(135f, min, max)); Assert.True(PossessionStation.InArc(-135f, min, max));
            Assert.False(PossessionStation.InArc(0f, min, max)); Assert.False(PossessionStation.InArc(45f, min, max)); Assert.False(PossessionStation.InArc(-45f, min, max));
        }
        Assert.True(PossessionStation.InArc(0f, -120f, 120f)); Assert.False(PossessionStation.InArc(180f, -120f, 120f));   // a station's forward arc, as before
        Assert.True(PossessionStation.InArc(170f, 110f, -110f)); Assert.False(PossessionStation.InArc(90f, 110f, -110f));  // a rear-only arc through ±180°
    }

    [Fact]
    public void TheCue_OutsideSeatArc_FiresForAPillionLookingAhead_NotBehindOrAside_NeverForTheDriver_AndFollowsTheBikesHeading_ZeroAlloc()
    {
        using var world = new EntityRepository();
        world.RegisterComponent<SimTransform>(); world.RegisterComponent<TkbIdentity>(); world.RegisterComponent<IsEmbarkedTag>(); world.RegisterComponent<PassengerBuffer>();
        var db = WithStandIn();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
        var bike = world.CreateEntity();
        world.AddComponent(bike, new SimTransform { Position = new Vector3(100f, 50f, 0f), Rotation = Quaternion.Identity });
        world.AddComponent(bike, new TkbIdentity { TkbType = (TkbTypeId)StandIn });
        Entity Rider(byte seat)
        {
            var m = world.CreateEntity();
            world.AddComponent(m, new SimTransform { Position = new Vector3(100f, 50f, 0f), Rotation = Quaternion.Identity });
            world.AddComponent(m, new TkbIdentity { TkbType = (TkbTypeId)TkbEntityTypes.Infantry_Rifleman });
            world.AddComponent(m, new IsEmbarkedTag { VehicleEntity = bike, Exposed = 1, Seat = seat });
            return m;
        }
        var driver = Rider(0); var pillion = Rider(1);
        var at = new Vector3(100f, 50f, 1f);
        Assert.True(PossessionStation.OutsideSeatArc(world, pillion, at + new Vector3(30f, 0f, 0f)), "dead ahead: across the rider — out of arc");
        Assert.True(PossessionStation.OutsideSeatArc(world, pillion, at + new Vector3(30f, 8f, 0f)), "15° ahead-left: out of arc");
        Assert.False(PossessionStation.OutsideSeatArc(world, pillion, at + new Vector3(20f, 20f, 0f)), "45°: in (the arc is 30° … 330°)");
        Assert.False(PossessionStation.OutsideSeatArc(world, pillion, at + new Vector3(0f, 30f, 0f)), "left: in");
        Assert.False(PossessionStation.OutsideSeatArc(world, pillion, at + new Vector3(0f, -30f, 0f)), "right: in");
        Assert.False(PossessionStation.OutsideSeatArc(world, pillion, at + new Vector3(-30f, 0f, 0f)), "behind: in");
        Assert.False(PossessionStation.OutsideSeatArc(world, driver, at + new Vector3(30f, 0f, 0f)), "the rider's arc is the front and sides (−120° … 120°): ahead is in");
        Assert.True(PossessionStation.OutsideSeatArc(world, driver, at + new Vector3(-30f, 0f, 0f)), "behind the rider: out of his arc");
        // the bike turns right 90° (heading −y): what was behind is now to its left-rear... and "ahead" of the world is its left side
        world.SetComponent(bike, new SimTransform { Position = new Vector3(100f, 50f, 0f), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI / 2f) });
        Assert.False(PossessionStation.OutsideSeatArc(world, pillion, at + new Vector3(30f, 0f, 0f)), "world +x is the turned bike's LEFT: in");
        Assert.True(PossessionStation.OutsideSeatArc(world, pillion, at + new Vector3(0f, -30f, 0f)), "world -y is the turned bike's front: out");
        // a man on foot: never
        var foot = world.CreateEntity();
        world.AddComponent(foot, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
        Assert.False(PossessionStation.OutsideSeatArc(world, foot, new Vector3(30f, 0f, 0f)));
        for (int i = 0; i < 5000; i++) PossessionStation.OutsideSeatArc(world, pillion, at + new Vector3(30f, 0f, 0f));   // warm (the tiered JIT too)
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++) PossessionStation.OutsideSeatArc(world, pillion, at + new Vector3(30f, 0f, 0f));
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);   // ⭐ per-frame HUD: 0 B
    }

    /// <summary>⭐ U-3 / U-4 — what a possessed rider sees from: the camera's eye is the SEAT's place (CE-1210 <c>SeatSwitch.TryEye</c>, nothing bike-specific): the driver's over the bars, the pillion's behind him, both on the bike's pose.</summary>
    [Fact]
    public void TheEye_OfAPossessedRider_IsHisSeatsPlaceOnTheBikesPose_TheDriversAheadOfThePillions()
    {
        using var world = new EntityRepository();
        world.RegisterComponent<SimTransform>(); world.RegisterComponent<TkbIdentity>(); world.RegisterComponent<IsEmbarkedTag>(); world.RegisterComponent<PassengerBuffer>();
        var db = WithStandIn();
        var bike = world.CreateEntity();
        world.AddComponent(bike, new SimTransform { Position = new Vector3(100f, 50f, 3f), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f) });   // facing +y
        world.AddComponent(bike, new TkbIdentity { TkbType = (TkbTypeId)StandIn });
        Entity Rider(byte seat)
        {
            var m = world.CreateEntity();
            world.AddComponent(m, new IsEmbarkedTag { VehicleEntity = bike, Exposed = 1, Seat = seat });
            return m;
        }
        Assert.True(SeatSwitch.TryEye(world, Rider(0), db, out var driver));
        Assert.True(SeatSwitch.TryEye(world, Rider(1), db, out var pillion));
        Assert.True(driver.Y > pillion.Y + 0.4f, $"facing +y: the driver is ahead of the pillion ({driver} / {pillion})");
        Assert.InRange(driver.X, 99.9f, 100.1f);
        Assert.InRange(driver.Z, 3f + 0.75f + 0.5f, 3f + 0.75f + 0.9f);   // the seat's z + his eyes above his hips
    }

    /// <summary>⭐ The fire arc on the maps (mockup panels C, D): the selected bike's pillion seat draws an amber wedge, its edges and arc, from the seat's place; the driver's seat (no arc) and a station draw nothing.</summary>
    [Fact]
    public void TheSeatArcGizmo_DrawsTheWedgeOfAFiringSeat_FromHisPlace_NotTheDriversSeat_AndFollowsTheHeading()
    {
        using var world = new EntityRepository();
        world.RegisterComponent<SimTransform>(); world.RegisterComponent<TkbIdentity>();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(WithStandIn());
        var bike = world.CreateEntity();
        world.AddComponent(bike, new SimTransform { Position = new Vector3(100f, 50f, 0f), Rotation = Quaternion.Identity });
        world.AddComponent(bike, new TkbIdentity { TkbType = (TkbTypeId)StandIn });
        var draw = new ArcDraw();
        new Hrot.ScenarioEditor.Gizmos.SeatArcGizmo().Draw(world, bike, draw);
        // the rider's arc (−120 … 120 = 240°: two edges + 24 segments) then the pillion's (30 … 330 = 300°: two edges + 30 segments)
        Assert.Equal(26 + 32, draw.Lines.Count);
        var pillionOrigin = new Vector3(100f - 0.45f, 50f, 0f);
        var pillion = draw.Lines.Skip(26).ToArray();
        Assert.All(pillion.Take(2), l => Assert.Equal(pillionOrigin, l.From));
        Assert.True(pillion[0].To.X > pillionOrigin.X && pillion[0].To.Y > 50f, "30° is left and ahead");
        Assert.True(pillion[1].To.X > pillionOrigin.X && pillion[1].To.Y < 50f, "330° is right and ahead");
        Assert.All(pillion.Skip(2), l => Assert.InRange(Vector3.Distance(l.From, pillionOrigin), SeatArcGizmoRange - 0.1f, SeatArcGizmoRange + 0.1f));
        // the bike turned 180°: the pillion's wedge turns with it (its rear, the middle of the arc, is now toward +x)
        world.SetComponent(bike, new SimTransform { Position = new Vector3(100f, 50f, 0f), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI) });
        var turned = new ArcDraw();
        new Hrot.ScenarioEditor.Gizmos.SeatArcGizmo().Draw(world, bike, turned);
        Assert.True(turned.Lines.Skip(26 + 2).Average(l => l.To.X) > 100f + 3f, "the arc of the rear now lies to +x");
    }

    private const float SeatArcGizmoRange = Hrot.ScenarioEditor.Gizmos.SeatArcGizmo.Range;

    /// <summary>Records the lines a gizmo draws; the layer is always on.</summary>
    private sealed class ArcDraw : Fdp.Toolkit.Diagnostics.Gizmos.IDebugDrawBuilder
    {
        public readonly List<(Vector3 From, Vector3 To)> Lines = new();
        public bool IsLayerActive(byte layer) => true;
        public void EmitRaw(in DebugPrimitive prim) { }
        public void DrawContextMenuBinding(int networkId, string menuJson) { }
        public void DrawLine(Vector3 s, Vector3 e, Rgba32 c, float t = 1f, SizeMode m = SizeMode.ScreenPixels, PipelineTarget tg = PipelineTarget.All, byte l = 0, LineStyle style = LineStyle.Solid) => Lines.Add((s, e));
        public void DrawLineGradient(Vector3 s, Vector3 e, Rgba32 sc, Rgba32 ec, float t = 1f, SizeMode m = SizeMode.ScreenPixels, PipelineTarget tg = PipelineTarget.All, byte l = 0, LineStyle style = LineStyle.Solid) { }
        public void DrawSphere(Vector3 c, float r, Rgba32 col, float thickness = 0f, SizeMode sm2 = SizeMode.WorldMeters, PipelineTarget tg = PipelineTarget.All, byte l = 0, Rgba32 fillColor = default, LineStyle style = LineStyle.Solid) { }
        public void DrawArrow(Vector3 f, Vector3 t, Rgba32 c, float h = 1f, byte l = 0) { }
        public void DrawText(float x, float y, Fdp.Core.FixedString32 t, Rgba32 c, CoordinateSpace sp = CoordinateSpace.World, byte l = 0, float fontSizePx = 0f, float lineOffsetPx = 0f, GizmoTextStyle style = GizmoTextStyle.Plain) { }
        public void DrawTextLong(float x, float y, string t, Rgba32 c, CoordinateSpace sp = CoordinateSpace.World, byte l = 0, float fontSizePx = 0f, float lineOffsetPx = 0f, GizmoTextStyle style = GizmoTextStyle.Plain) { }
        public void DrawEntityBadge(Entity e, Fdp.Core.FixedString32 rt, PipelineTarget tg = PipelineTarget.All) { }
        public void DrawEntityLocal(int a, Vector3 ls, Vector3 le, Rgba32 c, float t = 1f, byte l = 0) { }
        public void DrawEntityLocalInteractive(int a, Vector3 ls, Vector3 le, Rgba32 c, ushort sid, float t = 1f, byte l = 0) { }
    }

    // ── the real 3-D frame ────────────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public void TheMotorcycle_IsDrawn_ByTheRealFrame_TwoUp_ThePillionTurnsToHisAim_AndABikeInATurnLeans()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);
        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>(); world.RegisterComponent<TkbIdentity>(); world.RegisterComponent<IsEmbarkedTag>(); world.RegisterComponent<WeaponPosture>();
        var tkb = WithStandIn();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        double clock = 0.0;
        void Clock(double dt) { clock += dt; world.SetSingletonUnmanaged(new GlobalTime { TotalTime = clock }); }
        Clock(0.0);
        var query = WorldQuery.Of(world)!;
        var spot = Map3DFrameRail.FindOpenGround(terrain, query);
        float G(float x, float y) => query.GroundHeightAt(x, y);
        float ground = G(spot.X, spot.Y);
        var bike = world.CreateEntity();
        void Put(Entity e, Vector3 p, float yaw) => world.SetComponent(e, new SimTransform { Position = p, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw) });
        world.AddComponent(bike, new SimTransform { Position = new Vector3(spot.X, spot.Y, ground), Rotation = Quaternion.Identity });
        world.AddComponent(bike, new TkbIdentity { TkbType = (TkbTypeId)StandIn });
        Entity Rider(byte seat)
        {
            var m = world.CreateEntity();
            world.AddComponent(m, new SimTransform { Position = new Vector3(spot.X, spot.Y, ground), Rotation = Quaternion.Identity });
            world.AddComponent(m, new TkbIdentity { TkbType = (TkbTypeId)TkbEntityTypes.Infantry_Rifleman });
            world.AddComponent(m, new IsEmbarkedTag { VehicleEntity = bike, Exposed = 1, Seat = seat });
            return m;
        }
        var driver = Rider(0); var pillion = Rider(1);
        var target = world.CreateEntity();   // a man to the bike's rear-left, 12 m off: inside the arc
        world.AddComponent(target, new SimTransform { Position = new Vector3(spot.X - 8f, spot.Y + 8f, G(spot.X - 8f, spot.Y + 8f)), Rotation = Quaternion.Identity });
        world.AddComponent(target, new TkbIdentity { TkbType = (TkbTypeId)TkbEntityTypes.Infantry_Rifleman });
        world.AddComponent(pillion, new WeaponPosture { Kind = WeaponPostureKind.AtEase });
        var pose = ArticulationPose.Neutral;
        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var bodies = new EntityBodyLayer3D(() => world, () => tkb, (w, e) => pose);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => G(x, y);
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        sw.Set(true, animate: false);
        void Look(float lookUp, float dist, float yaw, float pitch)
            => sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X, spot.Y, ground + lookUp), dist, yaw, pitch);
        void Render() { frame.Step(canvas.Draw); frame.Step(canvas.Draw); }
        Pixels Screen(string? save = null) { Render(); if (save != null) frame.Screenshot(Path.Combine(shots, save)); return ScreenOf(); }
        const float Side = MathF.PI * 0.5f;

        // two up, riding, the pillion's rifle at ease
        Look(0.9f, 6.5f, Side, -0.08f);
        var riding = Screen("moto-1-side.png");
        Assert.Equal(4, bodies.BodiesDrawn);                    // the bike, its two riders, the target
        Assert.Equal(0, bodies.PillionsAimed);
        Look(0.9f, 6.0f, Side + 0.7f, -0.25f);
        Screen("moto-2-front34.png");
        Look(0.9f, 6.0f, Side - 0.7f, -0.25f);
        Screen("moto-2b-rear34.png");
        Look(0.9f, 6.0f, Side - 0.7f, -0.25f);
        var atEase = Screen();

        // the pillion's weapon brain has the rifle up at the target: his upper body turns to it (rear-left, ~135°)
        world.SetComponent(pillion, new WeaponPosture { Kind = WeaponPostureKind.Aimed, Target = target });
        for (int i = 0; i < 8; i++) { Clock(0.1); Render(); }   // the turn eases in (0.12 s constant)
        var firing = Screen("moto-3-pillion-fires-rear34.png");
        Assert.Equal(1, bodies.PillionsAimed);                 // the pillion only: the driver's seat has no arc
        Assert.True(Diff(atEase, firing) > 1500, $"the pillion's pose changed on screen: {Diff(atEase, firing)} px");
        Look(0.9f, 6.0f, Side, -0.1f);
        Screen("moto-3b-pillion-fires-side.png");
        Look(1.2f, 7.0f, Side + 2.2f, -0.45f);
        Screen("moto-3c-pillion-fires-top34.png");
        // the target moves FORWARD of the bike, outside the arc... the drawing still turns (the arc is the sim's refusal, U-3's cue) but never beyond the limit — and drops the aim at rest
        world.SetComponent(pillion, new WeaponPosture { Kind = WeaponPostureKind.AtEase });
        for (int i = 0; i < 12; i++) { Clock(0.1); Render(); }
        Look(0.9f, 6.0f, Side - 0.7f, -0.25f);
        Assert.True(Diff(atEase, Screen()) < 150, "eased back to the at-ease picture");

        // a bike in a left turn (10 m/s, 0.5 rad/s: ~0.5 rad of lean) is drawn leaning; stopped on the same spot it stands upright again: the frames differ
        float yaw = 0f; var at = new Vector3(spot.X, spot.Y, ground);
        void Place(Vector3 p, float h) { Put(bike, p, h); Put(driver, p, h); Put(pillion, p, h); }
        void LookAtBike(float dist, float yawCam, float pitchCam)
            => sw.Camera3D.Pose = new CameraPose(new Vector3(at.X, at.Y, G(at.X, at.Y) + 0.9f), dist, yawCam, pitchCam);
        for (int i = 0; i < 50; i++)
        {
            Clock(0.05); yaw += 0.5f * 0.05f;
            at += new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0f) * 0.5f; at.Z = G(at.X, at.Y);
            Place(at, yaw);
            LookAtBike(7f, yaw + MathF.PI + 0.35f, -0.05f);   // from ahead, a little to the side
            Render();
        }
        var turning = Screen("moto-4-banked-front.png");
        for (int i = 0; i < 40; i++) { Clock(0.05); Place(at, yaw); LookAtBike(7f, yaw + MathF.PI + 0.35f, -0.05f); Render(); }   // stopped: the bank eases off (0.15 s)
        var upright = Screen("moto-4b-upright-front.png");
        // the gallery picture (the picker's thumbnail), to the scratchpad only: the bike and its two riders alone, at rest, from the front three-quarter
        foreach (var gone in new[] { target }) world.DestroyEntity(gone);
        Place(new Vector3(spot.X, spot.Y, ground), 0f);
        for (int i = 0; i < 40; i++) { Clock(0.05); Render(); }
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X, spot.Y, ground + 0.85f), 4.6f, Side + 0.75f, -0.18f);
        Screen("moto-6-thumb.png");
        Assert.True(Diff(upright, turning) > 800, $"a turning bike is drawn banked (differs from the upright one by {Diff(upright, turning)} px)");
    }

    // ── pixels ────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record Pixels(Color[] Colours, int W, int H);

    private static Pixels ScreenOf()
    {
        var image = Raylib.LoadImageFromScreen();
        try
        {
            var all = new Color[image.Width * image.Height];
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++) all[y * image.Width + x] = Raylib.GetImageColor(image, x, y);
            return new Pixels(all, image.Width, image.Height);
        }
        finally { Raylib.UnloadImage(image); }
    }

    private static int Diff(Pixels a, Pixels b)
    {
        int n = 0;
        for (int i = 0; i < a.Colours.Length; i++)
        {
            var p = a.Colours[i]; var q = b.Colours[i];
            if (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B) > 6) n++;
        }
        return n;
    }

    private static float Wrapped(float d)
    {
        d %= MathF.PI * 2f;
        if (d < 0) d += MathF.PI * 2f;
        return d > MathF.PI * 1.5f ? d - MathF.PI * 2f : d;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "assets", "icons"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root (assets/icons)");
    }
}
