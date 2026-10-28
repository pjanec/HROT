using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Vis3D;
using Hrot.Common.Orders;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐ CE-1158 (docs/DESIGN_Entity_Control.md §8h U-1…U-5, O-1, §8h.6; handoff <c>HANDOFF_Use_Key_To_UI.md</c>) — the station's half of
/// the FPS use key: <c>E</c> counted and sent with its verb and target, the hold-E list, the prompt from the ONE rule the brain decides
/// with, the brain's verdict for our own press only, the weapons a man controls on his keys, the Drive / Get out hand-over, and one Fps
/// unit per station.
/// </summary>
public sealed partial class PossessionStationTests
{
    /// <summary>A picker that answers a unit 1 m ahead (network id <paramref name="id"/>).</summary>
    private sealed class Ahead(long id) : IPicker3D
    {
        public long Id { get; set; } = id;
        public PickFilter Kind => PickFilter.EntityParts;
        public bool TryPick(Vector3 origin, Vector3 direction, float maxDistance, out PickResult hit)
        {
            hit = new PickResult(origin + direction, PickKind.Entity, 1f, Id);
            return Id != 0;
        }
    }

    /// <summary>A world with the rule's components and a TKB, a soldier at the origin held by this station (network id 4242).</summary>
    private sealed class UseWorld : IDisposable
    {
        public readonly EntityRepository W = new();
        public readonly NetworkEntityMap Map = new();
        private readonly Fdp.Toolkit.Tkb.TkbDatabase _db = new();
        private readonly System.Collections.Generic.Dictionary<string, TkbTemplate> _types = new();
        private long _nextType = 9000;
        public readonly Entity Me;

        public UseWorld()
        {
            foreach (var register in new Action[]
            {
                () => W.RegisterComponent<SimTransform>(), () => W.RegisterComponent<PossessedBy>(), () => W.RegisterComponent<WeaponAmmo>(),
                () => W.RegisterComponent<Health>(), () => W.RegisterComponent<PassengerBuffer>(), () => W.RegisterComponent<CarrierSeats>(),
                () => W.RegisterComponent<CarrierStations>(), () => W.RegisterComponent<OpenPassengerCabin>(), () => W.RegisterComponent<PersonCarrier>(),
                () => W.RegisterComponent<IsEmbarkedTag>(), () => W.RegisterComponent<IsAssistingTag>(), () => W.RegisterComponent<InjuryThresholds>(),
                () => W.RegisterComponent<ActorCapabilityState>(), () => W.RegisterComponent<WeaponSystemParams>(), () => W.RegisterComponent<WeaponSystemState>(),
                () => W.RegisterComponent<DoorState>(), () => W.RegisterComponent<Fdp.Toolkit.Motion.Locomotion>(), () => W.RegisterComponent<TkbIdentity>(),
                () => W.RegisterComponent<Fdp.Core.CommandHierarchy.UnitEntity>(), () => W.RegisterComponent<OperatorUseStatus>(),
            }) register();
            if (!W.IsComponentTypeRegistered<EntityInfo>()) W.RegisterComponent<EntityInfo>();
            W.RegisterEvent<Fdp.Toolkit.Combat.Events.DamageAssessedEvent>();
            W.SetSingletonManaged(Map);
            W.SetSingletonManaged<ITkbDatabase>(_db);
            Me = Soldier(Vector3.Zero, 100f, 4242);
            W.AddComponent(Me, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });
        }

        public void Dispose() => W.Dispose();

        public Entity OfType(string name, Vector3 at, long netId, CarrierDefDto? carrier = null, WeaponSystemDto? weapon = null, InjuryDefDto? injury = null)
        {
            var e = W.CreateEntity();
            W.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            if (!_types.TryGetValue(name, out var t))   // one template per type name (the database refuses a second)
            {
                t = new TkbTemplate(name, ++_nextType);
                if (carrier != null) t.AddDescriptor(carrier);
                if (weapon != null) t.AddDescriptor(weapon);
                if (injury != null) t.AddDescriptor(injury);
                _db.Register(t);
                _types[name] = t;
            }
            W.AddComponent(e, new TkbIdentity { TkbType = t.TkbType });
            new Fdp.Toolkit.Behavior.Translators.CarrierTkbTranslator().Inject(W, e, t);
            new WeaponSystemTkbTranslator().Inject(W, e, t);
            new Fdp.Toolkit.Combat.Translators.InjuryTkbTranslator().Inject(W, e, t);
            if (netId != 0) Map.Register(netId, e);
            return e;
        }

        public Entity Soldier(Vector3 at, float health, long netId)
        {
            var e = OfType("Rifleman", at, netId, CarrierDefDto.PersonCarrier(), injury: new InjuryDefDto());
            W.AddComponent(e, new Health { Current = health, Max = 100f });
            W.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot | ActorCapabilities.CanInteract });
            return e;
        }

        public Entity Vehicle(Vector3 at, long netId) => OfType("BMP-2", at, netId, new CarrierDefDto
        {
            Seats = { new SeatDto { Role = SeatRole.Driver }, new SeatDto { Role = SeatRole.Passenger } },
        });

        public Entity Tripod(Vector3 at, long netId) => OfType("ZU-23-2", at, netId,
            new CarrierDefDto { Seats = { new SeatDto { Role = SeatRole.Gunner } } }, new WeaponSystemDto { NeedsOperator = true });

        public Entity Door(Vector3 at, TerrainDoorState state, long netId)
        {
            var d = W.CreateEntity();
            W.AddComponent(d, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            W.AddComponent(d, new DoorState { State = state });
            Map.Register(netId, d);
            return d;
        }

        /// <summary>The brain's possession bookkeeping, as <c>PossessionSystem</c> does it: requests (with their mode, a new epoch), then releases.</summary>
        /// <param name="swap">False right after a frame was read (the bus already swapped the station's publishes in).</param>
        public void ApplyPossession(bool swap = true)
        {
            if (swap) W.Bus.SwapBuffers();
            if (W.Bus.IsRegistered<PossessRequestEvent>())
                foreach (var req in W.Bus.Read<PossessRequestEvent>().ToArray())
                {
                    uint epoch = W.HasComponent<PossessedBy>(req.Unit) ? W.GetComponentRO<PossessedBy>(req.Unit).Epoch + 1 : 1;
                    var held = new PossessedBy { StationNodeId = req.StationNodeId, Mode = req.Mode, Epoch = epoch };
                    if (W.HasComponent<PossessedBy>(req.Unit)) W.SetComponent(req.Unit, held);
                    else W.AddComponent(req.Unit, held);
                }
            if (W.Bus.IsRegistered<ReleasePossessionEvent>())
                foreach (var rel in W.Bus.Read<ReleasePossessionEvent>().ToArray())
                    if (W.HasComponent<PossessedBy>(rel.Unit)) W.RemoveComponent<PossessedBy>(rel.Unit);
        }
    }

    private static (PossessionStation Station, MapViewSwitch Switch, Ahead Crosshair) Station(UseWorld u, long aimAt)
    {
        var (_, sw, _) = Map();
        var crosshair = new Ahead(aimAt);
        sw.Camera3D.Pickers.Add(crosshair);
        var station = new PossessionStation(sw, () => u.W);
        station.Update(0.016f);
        sw.Camera3D.Update(0.016f);
        return (station, sw, crosshair);
    }

    private static OperatorControlFrameEvent Frame(UseWorld u, MapViewSwitch sw, Input input)
    {
        sw.Camera3D.HandleInput(input);
        u.W.Bus.SwapBuffers();
        return u.W.Bus.Read<OperatorControlFrameEvent>().ToArray().Last();
    }

    private static Input Key(Raylib_cs.KeyboardKey key, bool pressed = true, bool down = true)
    {
        var input = new Input();
        if (pressed) input.Pressed.Add((int)key);
        if (down) input.Keys.Add((int)key);
        return input;
    }

    /// <summary>⭐ U-1, U-2 (§8e D-1) — a TAP of <c>E</c> counts one press, sent with the primary verb (0) and what the crosshair rested on;
    /// a frame without a press keeps the counter, so a lost frame loses nothing.</summary>
    [Fact]
    public void CE1158_ATapOfE_CountsOnePress_WithThePrimaryVerb_AndTheTargetUnderTheCrosshair()
    {
        using var u = new UseWorld();
        u.Door(new Vector3(1f, 0f, 0f), TerrainDoorState.Closed, 77);
        var (_, sw, _) = Station(u, 77);

        Assert.Equal(0, Frame(u, sw, Key(Raylib_cs.KeyboardKey.F)).UseSeq);     // pressed: not yet (a hold may follow)
        var f = Frame(u, sw, new Input());                                       // let go: one press
        Assert.Equal((1, 0, 77L), (f.UseSeq, f.UseVerb, f.UseTarget));
        Assert.Equal(1, Frame(u, sw, new Input()).UseSeq);                       // the counter holds
        Frame(u, sw, Key(Raylib_cs.KeyboardKey.F));
        Assert.Equal(2, Frame(u, sw, new Input()).UseSeq);
    }

    /// <summary>⭐ O-1 — holding <c>E</c> opens the list of the target's verbs by the crosshair; while it is open 1-4 pick a VERB (not a weapon),
    /// and letting go sends the picked one.</summary>
    [Fact]
    public void CE1158_HoldingE_OpensTheList_1to4PickAVerb_NotAWeapon_AndLettingGoSendsIt()
    {
        using var u = new UseWorld();
        u.Door(new Vector3(1f, 0f, 0f), TerrainDoorState.Closed, 77);   // Open door · Lock door · Breach door
        var ammo = new WeaponAmmo { Count = 2 };
        ammo.Set(0, new WeaponState { Ammo = 30, MaxAmmo = 30 });
        ammo.Set(1, new WeaponState { Ammo = 5, MaxAmmo = 5 });
        u.W.AddComponent(u.Me, ammo);
        var (station, sw, _) = Station(u, 77);
        var driver = Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);

        Frame(u, sw, Key(Raylib_cs.KeyboardKey.F));
        Assert.False(driver.UseListOpen);
        station.Update(FpsCameraDriver.HoldSeconds + 0.05f);
        Assert.True(driver.UseListOpen);
        var hold = Key(Raylib_cs.KeyboardKey.F, pressed: false);
        hold.Pressed.Add((int)Raylib_cs.KeyboardKey.Three);
        var f = Frame(u, sw, hold);
        Assert.Equal((0, 0), (f.UseSeq, f.Mount));                               // nothing sent yet; 3 did not pick weapon 3
        var lines = driver.UseLines(u.W).Select(l => l.Text).ToArray();
        Assert.Equal(new[] { "  1  Open door", "  2  Lock door", "> 3  Breach door" }, lines);

        f = Frame(u, sw, new Input());
        Assert.Equal((1, 2, 77L), (f.UseSeq, f.UseVerb, f.UseTarget));
        Assert.False(driver.UseListOpen);
    }

    /// <summary>⭐ U-3, §8h.6 ① — the prompt is the RULE's primary action on this node's own copy of the world, named as the HUD says it,
    /// and greyed with the rule's reason: a door, a man down, a vehicle, a standalone weapon.</summary>
    [Fact]
    public void CE1158_ThePrompt_IsTheRulesPrimary_ForADoorAManDownAVehicleAndAWeapon_GreyedWithItsReason()
    {
        using var u = new UseWorld();
        string Prompt(Entity target) => PossessionStation.Hud.Prompt(u.W, OperatorUseActions.For(u.W, u.Me, target)[0]);

        Assert.Equal("F  Open door", Prompt(u.Door(new Vector3(1f, 0f, 0f), TerrainDoorState.Closed, 70)));
        Assert.Equal("F  Close door", Prompt(u.Door(new Vector3(1f, 0f, 0f), TerrainDoorState.Open, 71)));
        Assert.Equal("F  Open door (locked)", Prompt(u.Door(new Vector3(1f, 0f, 0f), TerrainDoorState.Locked, 72)));
        Assert.Equal("F  Open door (too far)", Prompt(u.Door(new Vector3(DoorRules.ReachMetres + 1f, 0f, 0f), TerrainDoorState.Closed, 73)));
        Assert.Equal("F  Drag", Prompt(u.Soldier(new Vector3(1f, 0f, 0f), 10f, 74)));
        Assert.Equal("F  Drag (not down)", Prompt(u.Soldier(new Vector3(1f, 0f, 0f), 90f, 75)));
        Assert.Equal("F  Drive BMP-2", Prompt(u.Vehicle(new Vector3(2f, 0f, 0f), 76)));
        Assert.Equal("F  Man ZU-23-2", Prompt(u.Tripod(new Vector3(1f, 0f, 0f), 77)));
        Assert.Equal("F  Drive BMP-2 (too far)", Prompt(u.Vehicle(new Vector3(OperatorUseActions.VehicleReach + 2f, 0f, 0f), 78)));

        // drawn by the crosshair, in the colour of what it says
        var (_, sw, crosshair) = Station(u, 73);
        var driver = Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);
        Frame(u, sw, new Input());
        var far = Assert.Single(driver.UseLines(u.W));
        Assert.Equal(("F  Open door (too far)", PossessionStation.Hud.UseGreyed), (far.Text, far.Colour));
        crosshair.Id = 70;
        sw.Camera3D.Update(0.016f);
        Frame(u, sw, new Input());
        var near = Assert.Single(driver.UseLines(u.W));
        Assert.Equal(("F  Open door", PossessionStation.Hud.UseReady), (near.Text, near.Colour));
        crosshair.Id = 0;
        sw.Camera3D.Update(0.016f);
        Frame(u, sw, new Input());
        Assert.Empty(driver.UseLines(u.W));                                      // nothing to do: no prompt
    }

    /// <summary>⭐ §8h.6 ② — the brain's verdict shows only when its <c>UseSeq</c> is OUR last press, only once it is done or refused, and
    /// for <see cref="FpsCameraDriver.VerdictSeconds"/>.</summary>
    [Fact]
    public void CE1158_TheVerdictLine_OnlyForOurOwnPress_OnceDoneOrRefused_ForTwoSeconds()
    {
        using var u = new UseWorld();
        u.Door(new Vector3(1f, 0f, 0f), TerrainDoorState.Closed, 77);
        var (station, sw, crosshair) = Station(u, 77);
        var driver = Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);
        u.W.AddComponent(u.Me, new OperatorUseStatus { UseSeq = 1, Outcome = UseOutcome.Refused, Reason = UseReason.OutOfReach });
        crosshair.Id = 0;
        sw.Camera3D.Update(0.016f);
        Frame(u, sw, new Input());
        Assert.Empty(driver.UseLines(u.W));                                      // a status before any press of ours: not ours

        crosshair.Id = 77;
        sw.Camera3D.Update(0.016f);
        Frame(u, sw, Key(Raylib_cs.KeyboardKey.F));
        Assert.Equal((1, 77L), (Frame(u, sw, new Input()) is var pressed ? (pressed.UseSeq, pressed.UseTarget) : default));   // our press 1
        crosshair.Id = 0;
        sw.Camera3D.Update(0.016f);
        Frame(u, sw, new Input());                                               // looking away: no prompt under the verdict
        u.W.SetComponent(u.Me, new OperatorUseStatus { UseSeq = 1, Outcome = UseOutcome.Running });
        Assert.Empty(driver.UseLines(u.W));                                      // running: nothing extra
        u.W.SetComponent(u.Me, new OperatorUseStatus { UseSeq = 1, Outcome = UseOutcome.Refused, Reason = UseReason.OutOfReach });
        var verdict = Assert.Single(driver.UseLines(u.W));
        Assert.Equal(("Open door: too far", PossessionStation.Hud.UseRefused), (verdict.Text, verdict.Colour));
        u.W.SetComponent(u.Me, new OperatorUseStatus { UseSeq = 0, Outcome = UseOutcome.Done });
        Assert.Empty(driver.UseLines(u.W));                                      // another press's answer
        u.W.SetComponent(u.Me, new OperatorUseStatus { UseSeq = 1, Outcome = UseOutcome.Done });
        Assert.Equal("Open door: done", Assert.Single(driver.UseLines(u.W)).Text);
        station.Update(FpsCameraDriver.VerdictSeconds + 0.1f);
        Assert.Empty(driver.UseLines(u.W));                                      // gone after two seconds
    }

    /// <summary>⭐ U-5 (module graph GAP1) — a man in a standalone weapon's Gunner seat has it on his keys after his own weapons: the HUD
    /// lists it and its key sends its mount index (32+k), the index the operator's fire path already takes.</summary>
    [Fact]
    public void CE1158_AMannedWeapon_JoinsTheKeys_AfterHisOwn_AsMount32()
    {
        using var u = new UseWorld();
        var rifle = new WeaponAmmo { Count = 1 };
        rifle.Set(0, new WeaponState { Ammo = 30, MaxAmmo = 30 });
        u.W.AddComponent(u.Me, rifle);
        var zu = u.Tripod(new Vector3(1f, 0f, 0f), 77);
        var gun = new WeaponAmmo { Count = 1 };
        gun.Set(0, new WeaponState { Ammo = 100, MaxAmmo = 100 });
        u.W.AddComponent(zu, gun);
        Span<int> slots = stackalloc int[PossessionStation.Hud.MaxSlots];
        Assert.Equal(1, PossessionStation.Hud.Slots(u.W, u.Me, slots));         // not manned yet: his rifle only

        Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(u.W, u.Me, zu, SeatRole.Gunner));
        Assert.Equal(2, PossessionStation.Hud.Slots(u.W, u.Me, slots));
        Assert.Equal(WeaponSystems.ControlledMountBase, slots[1]);
        var (_, sw, _) = Station(u, 0);
        Assert.Equal(WeaponSystems.ControlledMountBase, Frame(u, sw, new Input()).Mount);   // ⭐ CE-1307 — a gunner starts on his gun
        Assert.Equal(0, Frame(u, sw, Key(Raylib_cs.KeyboardKey.One, down: false)).Mount);   // 1: his rifle, and it stays
        Assert.Equal(0, Frame(u, sw, new Input()).Mount);
        Assert.Equal(WeaponSystems.ControlledMountBase, Frame(u, sw, Key(Raylib_cs.KeyboardKey.Two, down: false)).Mount);
        Assert.Contains("> 2 WEAPON  100/100", PossessionStation.Hud.Lines(u.W, u.Me, WeaponSystems.ControlledMountBase, false, false));
        Assert.Equal(0, Frame(u, sw, Key(Raylib_cs.KeyboardKey.One, down: false)).Mount);   // 1: his own rifle again
        Assert.Equal(0, Frame(u, sw, new Input { MouseWheelMove = 1f }).Mount);   // ⭐ R-1034 — the wheel is the camera, never the weapon
    }

    /// <summary>⭐ U-4, the driving sequence — Drive: once the passenger list shows him in the driver seat the station RIDES the soldier, and
    /// only once he rides possesses the vehicle (possessing it first would release him); the view stays in first person throughout. Get out
    /// (<c>E</c> in the vehicle): back into him — which releases the vehicle — and his first frame carries the Get-out press.</summary>
    [Fact]
    public void CE1158_Drive_RidesTheSoldier_ThenDrivesTheVehicle_AndGetOut_HandsBackToHim_WhoseFirstFrameGetsOut()
    {
        using var u = new UseWorld();
        var bmp = u.Vehicle(new Vector3(2f, 0f, 0f), 88);
        var (station, sw, _) = Station(u, 88);
        Assert.Equal(u.Me, station.Unit);

        Frame(u, sw, Key(Raylib_cs.KeyboardKey.F));
        var f = Frame(u, sw, new Input());
        Assert.Equal((1, 0, 88L), (f.UseSeq, f.UseVerb, f.UseTarget));
        Assert.Equal(PossessionStation.HandOverStep.AwaitBoard, station.Step);
        station.Update(0.016f);
        u.ApplyPossession();
        Assert.Equal(PossessionMode.Fps, u.W.GetComponentRO<PossessedBy>(u.Me).Mode);   // not aboard yet: nothing moves

        Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(u.W, u.Me, bmp, SeatRole.Driver));   // the brain boarded him
        station.Update(0.016f);
        Assert.Equal(PossessionStation.HandOverStep.AwaitRiding, station.Step);
        station.Update(0.016f);                                                  // a frame before his Riding replicates (a network lags)
        Assert.Equal(PossessionStation.HandOverStep.AwaitRiding, station.Step);  // the vehicle waits — possessing it now would release him
        u.ApplyPossession();
        Assert.Equal(PossessionMode.Riding, u.W.GetComponentRO<PossessedBy>(u.Me).Mode);
        Assert.False(u.W.HasComponent<PossessedBy>(bmp));
        station.Update(0.016f);                                                  // he rides: now the vehicle
        Assert.Equal(PossessionStation.HandOverStep.AwaitVehicle, station.Step);
        Assert.True(sw.Is3D);                                                    // between the two: still first person
        u.ApplyPossession();
        Assert.Equal(PossessionMode.Riding, u.W.GetComponentRO<PossessedBy>(u.Me).Mode);   // the vehicle's possession kept him
        station.Update(0.016f);
        station.Update(0.016f);
        Assert.Equal((bmp, PossessionStation.HandOverStep.None), (station.Unit, station.Step));
        Assert.Equal(u.Me, PossessionStation.RiderOf(u.W, bmp));
        sw.Camera3D.Update(0.016f);

        Frame(u, sw, Key(Raylib_cs.KeyboardKey.F));                              // Get out
        Frame(u, sw, new Input());
        u.ApplyPossession(swap: false);
        Assert.Equal(PossessionMode.Fps, u.W.GetComponentRO<PossessedBy>(u.Me).Mode);
        Assert.False(u.W.HasComponent<PossessedBy>(bmp));                       // one Fps unit per station: the vehicle let go
        station.Update(0.016f);
        Assert.Equal(u.Me, station.Unit);
        Assert.True(sw.Is3D);
        sw.Camera3D.Update(0.016f);
        f = Frame(u, sw, new Input());
        Assert.Equal((1, 0, 88L), (f.UseSeq, f.UseVerb, f.UseTarget));          // his Get out, on his first frame
    }

    /// <summary>⭐ Module graph GAP2 — a new Fps possession releases this station's other Fps unit (the station drives one unit), a Riding one is
    /// kept, and <see cref="PossessionStation.HeldHere"/> drives the newest Fps unit and never a rider.</summary>
    [Fact]
    public void CE1158_ASecondFpsPossess_LeavesOneHeld_ARiderIsKept_AndHeldHereSkipsRiders()
    {
        using var u = new UseWorld();
        var other = u.Soldier(new Vector3(5f, 0f, 0f), 100f, 4343);
        var third = u.Soldier(new Vector3(6f, 0f, 0f), 100f, 4444);
        var orders = new EntityOrders(u.W, u.Map, geo: null);

        Assert.True(orders.Possess(4343).Accepted);
        u.ApplyPossession();
        Assert.False(u.W.HasComponent<PossessedBy>(u.Me));
        Assert.Equal(other, PossessionStation.HeldHere(u.W, out long id));
        Assert.Equal(4343L, id);

        Assert.True(orders.Possess(4343, PossessionMode.Riding).Accepted);
        u.ApplyPossession();                                                     // ⚠ he must RIDE before the next Fps possess, or it releases him
        Assert.True(orders.Possess(4444).Accepted);
        u.ApplyPossession();
        Assert.Equal(PossessionMode.Riding, u.W.GetComponentRO<PossessedBy>(other).Mode);   // the rider stays held
        Assert.Equal(third, PossessionStation.HeldHere(u.W, out _));

        u.W.SetComponent(third, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Riding, Epoch = 9 });
        Assert.Equal(Entity.Null, PossessionStation.HeldHere(u.W, out _));     // only riders: nothing to drive
    }

    /// <summary>
    /// ⭐⭐ <c>R-4096</c> S1 (docs/DESIGN_Entity_Control.md C-3′) — the STATION answers a possess of a unit ANOTHER station holds at once,
    /// from the replicated <c>PossessedBy</c>: <see cref="OrderRefusal.Possessed"/> naming the holder (the HTTP route's 409), for Fps and
    /// for Riding; no request goes out and nothing of this station's own is released. A free unit is still possessed (S6), and the unit
    /// this station holds itself may be re-possessed (its own mode change). ✅ Red-proof: the <c>StationClaims.ForPossess</c> switch
    /// removed from <c>EntityOrders.Possess</c> (the old C-3) ⇒ the first assert fails and a request goes out.
    /// </summary>
    [Fact]
    public void R4096_PossessingAUnitAnotherStationHolds_IsRefused_NothingGoesOut_NothingOfMineIsReleased()
    {
        using var u = new UseWorld();
        var theirs = u.Soldier(new Vector3(5f, 0f, 0f), 100f, 4343);
        u.W.AddComponent(theirs, new PossessedBy { StationNodeId = 77, Mode = PossessionMode.Fps, Epoch = 1 });
        u.Soldier(new Vector3(6f, 0f, 0f), 100f, 4444);
        var orders = new EntityOrders(u.W, u.Map, geo: null);

        var refused = orders.Possess(4343);
        Assert.Equal(OrderRefusal.Possessed, refused.Refusal);
        Assert.Contains("station 77", refused.Detail);
        Assert.Equal(OrderRefusal.Possessed, orders.Possess(4343, PossessionMode.Riding).Refusal);
        u.W.Bus.SwapBuffers();
        Assert.Equal(0, u.W.Bus.IsRegistered<PossessRequestEvent>() ? u.W.Bus.Read<PossessRequestEvent>().ToArray().Length : 0);   // no request
        Assert.Equal(0, u.W.Bus.IsRegistered<ReleasePossessionEvent>() ? u.W.Bus.Read<ReleasePossessionEvent>().ToArray().Length : 0);   // nothing let go
        Assert.Equal(77L, orders.HolderOf(4343));

        Assert.True(orders.Possess(4242, PossessionMode.Riding).Accepted);      // my own unit: my own mode change
        Assert.True(orders.Possess(4444).Accepted);                              // a free unit: as ever
    }

    /// <summary>
    /// ⭐ <c>R-4096</c> — a vehicle that carries a player of ANOTHER side than this station's is refused (<see cref="OrderRefusal.WrongSide"/>,
    /// naming the other station); one carrying a player of its OWN side is possessed. ✅ Red-proof: the side comparison dropped.
    /// </summary>
    [Fact]
    public void R4096_PossessingAVehicleAnEnemyPlayerSitsIn_IsRefusedWrongSide_AFriendsIsNot()
    {
        using var u = new UseWorld();
        u.W.AddComponent(u.Me, new EntityInfo { ForceId = ForceId.Friend });      // this station is a blue player
        Entity Rider(long netId, ForceId side, long station, Entity vehicle)
        {
            var r = u.Soldier(new Vector3(8f, 1f, 0f), 100f, netId);
            u.W.AddComponent(r, new EntityInfo { ForceId = side });
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(u.W, r, vehicle));
            u.W.AddComponent(r, new PossessedBy { StationNodeId = station, Mode = PossessionMode.Fps, Epoch = 1 });
            return r;
        }
        var redTank = u.Vehicle(new Vector3(8f, 0f, 0f), 5001);
        Rider(5002, ForceId.Hostile, 77, redTank);
        var blueTruck = u.Vehicle(new Vector3(20f, 0f, 0f), 5003);
        Rider(5004, ForceId.Friend, 88, blueTruck);
        var orders = new EntityOrders(u.W, u.Map, geo: null);

        var refused = orders.Possess(5001);
        Assert.Equal(OrderRefusal.WrongSide, refused.Refusal);
        Assert.Contains("station 77", refused.Detail);
        Assert.True(orders.Possess(5003).Accepted);
        Assert.NotEqual("", PossessionStation.Hud.ReasonText(UseReason.OtherSide));   // and the HUD has words for a boarding refused the same way
    }
}
