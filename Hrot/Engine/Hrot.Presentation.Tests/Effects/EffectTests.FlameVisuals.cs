using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Tkb;
using Hrot.UI.Common.Effects;
using Xunit;

namespace Hrot.Presentation.Tests.Effects;

/// <summary>
/// ⭐⭐ <c>CE-4635</c> / <c>CE-4637</c> (flame weapons, docs/designs/weapons/DESIGN_Flame_Weapons.md §7; handoff HANDOFF_Backend_To_Ui_Flame_Visuals) — the STATE as a second source
/// of effect items (<see cref="EffectLayer.ResolveStateInto"/>): every burning <c>FireState</c> spot (U-2), every lit flame jet (U-1), every burning man (U-3), every
/// <c>BurnedArea</c> mark (U-4). Each picture is drawn where §6 says AND is gone when it is out (A-1); the steady frame allocates nothing (A-2). The GL frames of the same pictures:
/// <c>FlameVisualsFrameRail</c>. 🔴 Red-proofs: see each test.
/// </summary>
public sealed partial class EffectTests
{
    private const long FlameTank = 0x6B11, FlameFuel = 0x6B12;

    private static (EntityRepository World, ITkbDatabase Tkb, EffectLayer Layer) FlameWorld()
    {
        var (world, tkb) = World();
        foreach (var t in new[] { typeof(FireState), typeof(WeaponPosture), typeof(WeaponAmmo), typeof(EffectState), typeof(GroundHeatMarks) })
        {
            if (t == typeof(FireState)) { if (!world.IsComponentTypeRegistered<FireState>()) world.RegisterComponent<FireState>(); }
            else if (t == typeof(WeaponPosture)) { if (!world.IsComponentTypeRegistered<WeaponPosture>()) world.RegisterComponent<WeaponPosture>(); }
            else if (t == typeof(WeaponAmmo)) { if (!world.IsComponentTypeRegistered<WeaponAmmo>()) world.RegisterComponent<WeaponAmmo>(); }
            else if (t == typeof(EffectState)) { if (!world.IsComponentTypeRegistered<EffectState>()) world.RegisterComponent<EffectState>(); }
            else if (!world.IsComponentTypeRegistered<GroundHeatMarks>()) world.RegisterComponent<GroundHeatMarks>();
        }
        var db = (TkbDatabase)tkb;
        var tank = new TkbTemplate("test flame tank", FlameTank);
        tank.AddDescriptor(new WeaponSuiteDto { Mounts = { new WeaponMountDto { Category = WeaponCategory.Flamethrower, AmmoGuid = FlameFuel, InitialAmmunition = 100 } } });
        db.Register(tank);
        var fuel = new TkbTemplate("test flame fuel", FlameFuel);
        fuel.AddDescriptor(new WarheadDto { Kind = WarheadKind.Incendiary, FlameRangeM = 60f, FlameHalfAngleDeg = 10f, FlameIntensity = 0.8f, BurnSeconds = 30f });
        db.Register(fuel);
        return (world, tkb, new EffectLayer(() => world, () => tkb));
    }

    private static FireState Spots(params (float X, float Y, float Z, float Radius, double Since, float Seconds, float Intensity)[] spots)
    {
        var fs = new FireState();
        foreach (var s in spots) Assert.True(fs.TryAdd(new FireSpot(s.X, s.Y, s.Z, s.Radius, (SimInstant)s.Since, s.Seconds, s.Intensity), 0.0));
        return fs;
    }

    private static Entity Holder(EntityRepository world, Vector3 at, float yaw, FireState fire)
    {
        var e = world.CreateEntity();
        world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw) });
        world.AddComponent(e, fire);
        return e;
    }

    private static Entity Flamer(EntityRepository world, Vector3 at, double lastShot, double burstStart, Vector3 point)
    {
        var e = world.CreateEntity();
        world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
        world.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)FlameTank });
        world.AddComponent(e, new WeaponPosture { Kind = WeaponPostureKind.Aimed, Target = Entity.Null, Point = point, ActiveMount = 0 });
        world.AddComponent(e, new WeaponAmmo { Count = 1, Load0 = FlameFuel, LastShotAt0 = lastShot, BurstStartedAt0 = burstStart });
        return e;
    }

    private static float Lateral(Vector3 p, Vector3 from, Vector3 dir) { var d = p - from; return (d - dir * Vector3.Dot(d, dir)).Length(); }

    /// <summary>The effect type of the jet is in the TKB catalogue with the FlameJet kind (U-5).</summary>
    [Fact]
    public void Flame_U5_TheJetHasItsDefaultVisual_InTheCatalogue()
    {
        var look = EffectTkbCatalog.VisualOf(null, EffectTkbCatalog.FlameJet);
        Assert.NotNull(look);
        Assert.Equal(EffectKind.FlameJet, look!.Kind);
        Assert.True(look.Size > 0f);
    }

    /// <summary>⭐ U-2 — a VEHICLE's spots are in ITS frame: turned by its rotation, then offset by its position; each burning spot is one Fire plus a small Smoke, and a spot
    /// is gone the moment it is out while the others burn on; none left, none drawn. 🔴 Red-proof: the rotation dropped from <c>HolderPoint</c> ⇒ the spots stay unturned.</summary>
    [Fact]
    public void Flame_U2_AVehiclesSpots_AreInItsHullFrame_AndEachIsGoneWhenItsOut()
    {
        var (world, _, layer) = FlameWorld();
        float yaw = MathF.PI / 2f;
        Holder(world, new Vector3(100f, 50f, 2f), yaw,
               Spots((2f, 0f, 1f, 2f, 0.0, 100f, 1f), (-2f, 0f, 1f, 2f, 0.0, 100f, 1f), (0f, 1.5f, 1.5f, 1.5f, 0.0, 5f, 0.6f)));
        At(world, 10.0);
        var items = State(layer);
        var fires = items.Where(r => r.Look.Kind == EffectKind.Fire).ToList();
        Assert.Equal(2, fires.Count);                                    // the third burned 5 s, it is out at 10
        Assert.Equal(2, items.Count(r => r.Look.Kind == EffectKind.Smoke));
        // a +90° turn sends hull (x, y) to world (−y, x): (2, 0) → (100, 52); (−2, 0) → (100, 48)
        Assert.Contains(fires, f => MathF.Abs(f.At.X - 100f) < 1e-3f && MathF.Abs(f.At.Y - 52f) < 1e-3f && MathF.Abs(f.At.Z - 3f) < 1e-3f);
        Assert.Contains(fires, f => MathF.Abs(f.At.X - 100f) < 1e-3f && MathF.Abs(f.At.Y - 48f) < 1e-3f);
        Assert.All(fires, f => Assert.Equal(10f, f.Age, 3));
        Assert.All(items.Where(r => r.Look.Kind == EffectKind.Smoke), s => Assert.True(EffectLayer.SizeOf(s) < EffectLayer.SizeOf(fires[0])));

        At(world, 4.0);
        Assert.Equal(3, State(layer).Count(r => r.Look.Kind == EffectKind.Fire));   // all three burn at 4 s
        At(world, 101.0);
        Assert.Empty(State(layer));                                      // all out ⇒ nothing
    }

    /// <summary>⭐ U-2 — a building's damage record and a terrain CELL's record entity hold spots in their record's frame (an offset); a field of cell records draws every spot;
    /// a spot not lit yet draws nothing.</summary>
    [Fact]
    public void Flame_U2_ABuildingRecordAndACellRecord_DrawTheirSpotsInTheRecordsFrame_AndAreGoneWhenOut()
    {
        var (world, _, layer) = FlameWorld();
        Holder(world, new Vector3(300f, 200f, 0f), 0f, Spots((4f, 1f, 3f, 3f, 0.0, 60f, 1f)));                              // a building's storey
        Holder(world, new Vector3(500f, 500f, 0f), 0f, Spots((0f, 0f, 0f, 6f, 0.0, 40f, 1f), (7f, -2f, 0f, 5f, 0.0, 40f, 0.7f), (-6f, 3f, 0f, 5f, 20.0, 40f, 1f)));   // a napalm cell
        At(world, 10.0);
        var fires = State(layer).Where(r => r.Look.Kind == EffectKind.Fire).ToList();
        Assert.Equal(3, fires.Count);                                    // the third (lit at 20 s) is not yet
        Assert.Contains(fires, f => f.At == new Vector3(304f, 201f, 3f));
        Assert.Contains(fires, f => f.At == new Vector3(500f, 500f, 0f));
        Assert.Contains(fires, f => f.At == new Vector3(507f, 498f, 0f));
        At(world, 30.0);
        Assert.Equal(4, State(layer).Count(r => r.Look.Kind == EffectKind.Fire));   // the building's and the cell's first two burn on, the third is lit now
        At(world, 70.0);
        Assert.Empty(State(layer));
    }

    /// <summary>⭐ U-1 — a LIT jet (≤ 0.25 s after the last fuel shot) is one FlameJet item from the muzzle toward the aim point, the munition's reach long at most, its
    /// puffs all inside the cone; the head of the stream races out over the first 0.2 s; out ⇒ nothing. 🔴 Red-proof: the cone half-angle ignored ⇒ the puffs leave the cone.</summary>
    [Fact]
    public void Flame_U1_ALitJet_RunsFromTheMuzzleTowardTheAimPoint_InsideItsCone_AndIsGoneWhenOut()
    {
        var (world, _, layer) = FlameWorld();
        var aim = new Vector3(40f, 5f, 0f);
        var unit = Flamer(world, new Vector3(0f, 0f, 0f), lastShot: 100.0, burstStart: 99.0, point: aim);
        At(world, 100.1);
        var jet = Assert.Single(State(layer), r => r.Look.Kind == EffectKind.FlameJet);
        var muzzle = jet.At;
        var dir = Vector3.Normalize(aim - muzzle);
        Assert.Equal(Vector3.Distance(muzzle, aim), Vector3.Distance(muzzle, jet.Toward), 2);   // the aim point is nearer than the 60 m reach
        Assert.Equal(0.8f, jet.Opacity, 3);                              // the munition's intensity
        Assert.Equal(10f * MathF.PI / 180f, jet.HalfAngle, 4);
        Assert.Equal(1.1f, jet.Age, 3);                                  // 100.1 − burst start 99.0
        var puffs = EffectLayer.Puffs(jet);
        Assert.Equal(EffectLayer.JetPuffs, puffs.Count);
        float tan = MathF.Tan(jet.HalfAngle);
        float length = Vector3.Distance(muzzle, aim);
        foreach (var p in puffs)
        {
            float along = Vector3.Dot(p.At - muzzle, dir);
            Assert.InRange(along, -0.01f, length + 0.01f);
            Assert.True(Lateral(p.At, muzzle, dir) <= along * tan + 0.01f, $"a puff left the cone: {Lateral(p.At, muzzle, dir)} at {along}");
        }
        Assert.Equal(puffs, EffectLayer.Puffs(jet));                    // stateless
        // the head races out: at 0.02 s into the burst the stream is far shorter than at 0.3 s
        float Reach(float age) => EffectLayer.Puffs(jet with { Age = age }).Max(p => Vector3.Dot(p.At - muzzle, dir));
        Assert.True(Reach(0.02f) < Reach(0.3f) * 0.5f, $"{Reach(0.02f)} vs {Reach(0.3f)}");

        At(world, 100.0 + FlameJets.LitSeconds + 0.01);                  // no fuel spent lately: out
        Assert.DoesNotContain(State(layer), r => r.Look.Kind == EffectKind.FlameJet);
        // a jet at a far aim point is the munition's reach long, never longer
        world.SetComponent(unit, new WeaponPosture { Kind = WeaponPostureKind.Aimed, Target = Entity.Null, Point = new Vector3(500f, 0f, 0f), ActiveMount = 0 });
        At(world, 100.1);
        var far = Assert.Single(State(layer), r => r.Look.Kind == EffectKind.FlameJet);
        Assert.Equal(60f, Vector3.Distance(far.At, far.Toward), 2);
    }

    /// <summary>⭐ U-3 — a man on fire (<c>EffectState.BurningUntil</c> via <c>PersonLook.IsBurning</c>) has ONE body-sized fire (slot 7 of his seed, no smoke); gone the moment it is out;
    /// another man (no state) has none.</summary>
    [Fact]
    public void Flame_U3_ABurningMan_HasOneFireOnHisBody_AndItIsGoneWhenHeIsOut()
    {
        var (world, _, layer) = FlameWorld();
        var man = Unit(world, Hrot.Map.Common.TkbEntityTypes.Infantry_Rifleman, new Vector3(30f, 40f, 0f));
        world.AddComponent(man, new EffectState { BurningUntil = (SimInstant)20.0 });
        var other = Unit(world, Hrot.Map.Common.TkbEntityTypes.Infantry_Rifleman, new Vector3(35f, 40f, 0f));
        world.AddComponent(other, new EffectState());
        At(world, 10.0);
        Assert.True(Hrot.Look.People.PersonLook.IsBurning(world, man, 10.0));
        Assert.False(Hrot.Look.People.PersonLook.IsBurning(world, other, 10.0));
        var items = State(layer);
        var fire = Assert.Single(items);
        Assert.Equal(EffectKind.Fire, fire.Look.Kind);
        Assert.Equal(man.Index * 8 + EffectLayer.ManFireSlot, fire.Seed);
        Assert.Equal(30f, fire.At.X, 3); Assert.Equal(40f, fire.At.Y, 3);
        Assert.InRange(EffectLayer.SizeOf(fire), 0.6f, 2.4f);           // body sized
        At(world, 20.0);
        Assert.Empty(State(layer));                                      // BurningUntil reached: out
    }

    /// <summary>⭐ U-4 — a <c>BurnedArea</c> mark is a dark decal rectangle (length × width, at its heading) UNDER the impact decals; another kind of mark and an empty record
    /// draw nothing. 🔴 Red-proof: the kind filter dropped ⇒ the vehicle-stand mark is drawn too.</summary>
    [Fact]
    public void Flame_U4_ABurnedAreaMark_IsADarkRectangleUnderTheDecals_OtherMarksAreNot()
    {
        var (world, _, layer) = FlameWorld();
        var rec = world.CreateEntity();
        var marks = new GroundHeatMarks { CellX = 3, CellY = 4, Count = 2 };
        marks.Set(0, new HeatMark(HeatMarkKind.BurnedArea, new Vector2(120f, 80f), 0.5f, 30f, 12f, HeatMarkEnd.NoHotEnd, (SimInstant)0.0, (SimInstant)5.0, 0f, ThermalPowerClass.NoPowerClass));
        marks.Set(1, new HeatMark(HeatMarkKind.VehicleStand, new Vector2(10f, 10f), 0f, 6f, 3f, HeatMarkEnd.Front, (SimInstant)0.0, (SimInstant)90.0, 100f, ThermalPowerClass.Tank));
        world.AddComponent(rec, marks);
        At(world, 100.0);
        var scorch = Assert.Single(State(layer));
        Assert.Equal(EffectKind.Decal, scorch.Look.Kind);
        Assert.Equal(new Vector2(30f, 12f), scorch.Rect);
        Assert.Equal(0.5f, scorch.Heading, 4);
        Assert.Equal(new Vector2(120f, 80f), new Vector2(scorch.At.X, scorch.At.Y));
        var (a, b, c, d) = EffectLayer.RectCorners(scorch.At, scorch.Rect, scorch.Heading);
        var f = new Vector2(MathF.Cos(0.5f), MathF.Sin(0.5f)) * 15f; var l = new Vector2(-MathF.Sin(0.5f), MathF.Cos(0.5f)) * 6f;
        Assert.Equal(new Vector2(120f, 80f) + f + l, a); Assert.Equal(new Vector2(120f, 80f) - f - l, c);
        Assert.Equal(30f, Vector2.Distance(a, b), 3); Assert.Equal(12f, Vector2.Distance(b, c), 3);

        var round = new EffectLayer.Resolved(EffectTkbCatalog.VisualOf(null, EffectTkbCatalog.DecalMedium)!, Vector3.Zero, Vector3.Zero, 0f, 1f, default);
        var list = new List<EffectLayer.Resolved> { round, scorch };
        EffectLayer.SortForDraw(list);
        Assert.Equal(new Vector2(30f, 12f), list[0].Rect);               // the scorch first: the impact decal lies over it

        marks.Count = 0; world.SetComponent(rec, marks);
        Assert.Empty(State(layer));                                      // no marks ⇒ nothing
    }

    /// <summary>
    /// ⭐ A-2 (R-2084) — a STEADY frame of the state source allocates NOTHING: a lit jet, a vehicle's three spots, a cell's spots, a burning man, a scorch mark — resolving them
    /// into the reused list, sorting for the draw and generating every item's puffs (what the 2-D and 3-D passes then hand to Raylib). 🔴 Red-proof: a <c>new List</c> per
    /// call in <c>ResolveStateInto</c> (or a <c>Look with { … }</c> copy per item) ⇒ bytes &gt; 0.
    /// </summary>
    [Fact]
    public void Flame_A2_ASteadyFrameOfTheStateSource_AllocatesNothing()
    {
        var (world, _, layer) = FlameWorld();
        Flamer(world, new Vector3(0f, 0f, 0f), lastShot: 100.0, burstStart: 99.5, point: new Vector3(30f, 0f, 0f));
        Holder(world, new Vector3(100f, 50f, 2f), 0.7f, Spots((2f, 0f, 1f, 2f, 0.0, 600f, 1f), (-2f, 0f, 1f, 2f, 0.0, 600f, 1f), (0f, 1.5f, 1.5f, 1.5f, 0.0, 600f, 0.6f)));
        Holder(world, new Vector3(300f, 300f, 0f), 0f, Spots((0f, 0f, 0f, 6f, 0.0, 600f, 1f), (7f, -2f, 0f, 5f, 0.0, 600f, 0.7f)));
        var man = Unit(world, Hrot.Map.Common.TkbEntityTypes.Infantry_Rifleman, new Vector3(30f, 40f, 0f));
        world.AddComponent(man, new EffectState { BurningUntil = (SimInstant)900.0 });
        var rec = world.CreateEntity();
        var marks = new GroundHeatMarks { Count = 1 };
        marks.Set(0, new HeatMark(HeatMarkKind.BurnedArea, new Vector2(120f, 80f), 0.5f, 30f, 12f, HeatMarkEnd.NoHotEnd, (SimInstant)0.0, (SimInstant)5.0, 0f, ThermalPowerClass.NoPowerClass));
        world.AddComponent(rec, marks);

        var list = new List<EffectLayer.Resolved>(64);
        var puffs = new List<EffectLayer.Puff>(64);
        void Frame()
        {
            list.Clear();
            layer.ResolveStateInto(list);
            EffectLayer.SortForDraw(list);
            for (int i = 0; i < list.Count; i++) EffectLayer.PuffsInto(list[i], puffs);
        }
        At(world, 100.1); Frame(); Frame();                              // warm: the queries, the looks, the lists' capacity
        Assert.True(list.Count >= 12, $"every source is present: {list.Count}");
        At(world, 100.2);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20; i++) Frame();
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
