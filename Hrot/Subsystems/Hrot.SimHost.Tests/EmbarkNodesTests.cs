using System.Numerics;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Behavior.Translators;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Translators;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Interfaces;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>CE-1056</c> I-2d (📄 docs/DESIGN_Entity_Interactions.md §7.3) — the AI asks to board and to leave: the two shared nodes walk on
    /// the locomotion channel, put ONE action on the interaction channel, and forward its answer; the vehicle's owner
    /// (<see cref="EmbarkationSystem"/>) is still the only applier. One world; the interaction dispatcher's calls are made by hand.
    /// </summary>
    public class EmbarkNodesTests
    {
        private readonly EntityRepository _w = new();
        private readonly EmbarkationSystem _owner = new();

        public EmbarkNodesTests()
        {
            SimHostComponentRegistry.RegisterAll(_w);
            CognitiveComponentRegistry.RegisterAll(_w);
        }

        private Entity Vehicle(Vector3 at)
        {
            var v = _w.CreateEntity();
            _w.AddComponent(v, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(v, new PassengerBuffer());
            return v;
        }

        private Entity Soldier(Vector3 at)
        {
            var s = _w.CreateEntity();
            _w.AddComponent(s, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(s, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot | ActorCapabilities.CanInteract });
            _w.AddComponent(s, new LocomotionChannel());
            _w.AddComponent(s, new InteractionChannel());
            return s;
        }

        /// <summary>What the interaction dispatcher does for the action the node just put on the channel: enter, execute, let the
        /// owner apply the raised command next frame, execute again.</summary>
        private void Dispatch(Entity soldier, IActionExecutor<InteractionChannel> executor)
        {
            ref var ch = ref _w.GetComponentRW<InteractionChannel>(soldier);
            executor.OnEnter(soldier, ref ch, _w);
            executor.Execute(soldier, ref ch, _w, 0f);
            _w.Bus.SwapBuffers();
            _owner.Execute(_w, 0f);
            ref var ch2 = ref _w.GetComponentRW<InteractionChannel>(soldier);
            if (ch2.Status == NodeStatus.Running) executor.Execute(soldier, ref ch2, _w, 0f);
        }

        [Fact]
        public void CE1056_Embark_WalksToTheBoardingPoint_ThenBoardsThroughTheOwner()
        {
            var vehicle = Vehicle(new Vector3(50f, 0f, 0f));
            var soldier = Soldier(Vector3.Zero);
            var p = new EmbarkNodeParams { Vehicle = vehicle };

            Assert.Equal(NodeStatus.Running, EmbarkNodes.Embark(ref p, soldier, _w));
            var walk = _w.GetComponentRO<LocomotionChannel>(soldier);
            Assert.Equal(NavigationConstants.ActionIdMoveTo, walk.ActiveAction);
            Assert.Equal(0, _w.GetComponentRO<InteractionChannel>(soldier).ActiveAction);   // not asking yet: out of reach
            Assert.True(Vector3.Distance(Embarkation.BoardingPoint(_w, vehicle), new Vector3(50f, 0f, 0f)) > Embarkation.DefaultHullRadius);

            _w.GetComponentRW<SimTransform>(soldier).Position = Embarkation.BoardingPoint(_w, vehicle);   // the walk arrives
            Assert.Equal(NodeStatus.Running, EmbarkNodes.Embark(ref p, soldier, _w));
            Assert.Equal(0, _w.GetComponentRO<LocomotionChannel>(soldier).ActiveAction);                   // the walk handed back
            Assert.Equal(BehaviorConstants.ActionIdEmbark, _w.GetComponentRO<InteractionChannel>(soldier).ActiveAction);
            Assert.Equal(0, _w.GetComponentRO<PassengerBuffer>(vehicle).Count);                            // asked, not written

            Dispatch(soldier, new EmbarkExecutor());
            Assert.Equal(NodeStatus.Success, EmbarkNodes.Embark(ref p, soldier, _w));
            Assert.Equal(1, _w.GetComponentRO<PassengerBuffer>(vehicle).Count);
            Assert.True(_w.HasComponent<IsEmbarkedTag>(soldier));
            Assert.Equal(0, _w.GetComponentRO<InteractionChannel>(soldier).ActiveAction);                  // the channel handed back
            Assert.Equal(NodeStatus.Success, EmbarkNodes.Embark(ref p, soldier, _w));                      // already aboard: at once
        }

        /// <summary>⭐ WS-6 — a walk to the boarding point that ENDS out of reach (here: it fails) is walked again — at most
        /// <c>EmbarkNodes.MaxWalks</c> walks in all, then the boarding fails. 📐 Live <c>ua-weapon-systems</c>: the M2 gunner's first walk
        /// failed in his first second and <c>SetUpWeapon</c> ended for good. 🔴 Red-proof: the old "walk ended ⇒ Failure" and the first
        /// assertion in the loop fails.</summary>
        [Fact]
        public void WS6_Embark_AWalkThatEndsOutOfReach_WalksAgain_ThenGivesUp()
        {
            var vehicle = Vehicle(new Vector3(50f, 0f, 0f));
            var soldier = Soldier(Vector3.Zero);
            var p = new EmbarkNodeParams { Vehicle = vehicle };
            Assert.Equal(NodeStatus.Running, EmbarkNodes.Embark(ref p, soldier, _w));
            for (int walk = 1; walk < EmbarkNodes.MaxWalks; walk++)
            {
                _w.GetComponentRW<LocomotionChannel>(soldier).Status = NodeStatus.Failure;                 // the walk failed, nowhere near
                Assert.Equal(NodeStatus.Running, EmbarkNodes.Embark(ref p, soldier, _w));
                Assert.Equal(NavigationConstants.ActionIdMoveTo, _w.GetComponentRO<LocomotionChannel>(soldier).ActiveAction);   // walking again
                Assert.Equal(NodeStatus.Running, _w.GetComponentRO<LocomotionChannel>(soldier).Status);
            }
            _w.GetComponentRW<LocomotionChannel>(soldier).Status = NodeStatus.Failure;
            Assert.Equal(NodeStatus.Failure, EmbarkNodes.Embark(ref p, soldier, _w));                       // the last walk too: give up
        }

        /// <summary>
        /// ⭐ <c>CE-2176</c> (Weapon Systems §4b, W-B) — MANNING is boarding the Gunner seat. A two-man crew weapon (the 81 mm: a Gunner seat,
        /// then the loader's plain seat): a plain <c>Embark</c> takes the LOADER's seat and controls nothing; <c>ManWeapon</c> (the same node,
        /// <c>Role = Gunner</c>) takes seat 0 and is the weapon's one controller — the man a fire mission finds the tube through.
        /// 📐 Live (`ua-indirect-fire` ①): the gunners boarded seat 1, so <c>BatteryFireMissionPlay.Plan</c> logged "no indirect weapon".
        /// ✅ Red-proofs: the executor ignoring the role ⇒ the ManWeapon man in seat 1 ⇒ red; the resolver not setting Gunner ⇒ red.
        /// </summary>
        [Fact]
        public void CE2176_ManWeapon_TakesTheGunnerSeat_ControlsAndFiresTheWeapon_APlainEmbarkTakesTheLoadersSeat()
        {
            Entity Mortar()
            {
                var m = Vehicle(new Vector3(0f, 0f, 0f));
                var seats = new CarrierSeats { SeatCount = 2 };
                seats.Roles[0] = SeatRole.Gunner;
                seats.Roles[1] = SeatRole.Passenger;   // the loader
                _w.AddComponent(m, seats);
                _w.AddComponent(m, new WeaponSystemParams { NeedsOperator = 1 });
                return m;
            }
            var rider = Mortar();
            var plain = Soldier(Embarkation.BoardingPoint(_w, rider));
            var ride = new EmbarkNodeParams { Vehicle = rider };
            Assert.Equal(NodeStatus.Running, EmbarkNodes.Embark(ref ride, plain, _w));
            Dispatch(plain, new EmbarkExecutor());
            Assert.Equal(1, _w.GetComponentRO<IsEmbarkedTag>(plain).Seat);                     // the loader's seat
            Assert.True(WeaponSystems.ControllerOf(_w, rider).IsNull);                         // nobody mans it
            Assert.False(_w.GetComponentRO<ActorCapabilityState>(plain).Capabilities.HasFlag(ActorCapabilities.CanShoot));   // the loader fires nothing

            var tube = Mortar();
            var gunner = Soldier(Embarkation.BoardingPoint(_w, tube));
            var block = default(ManWeaponBehavior.ManWeaponBlackboard);
            ManWeaponBehavior.Resolve(default, ref block, _w, gunner);
            Assert.Equal(SeatRole.Gunner, block.Board.Role);
            var man = block.Board;
            man.Vehicle = tube;
            Assert.Equal(NodeStatus.Running, EmbarkNodes.Embark(ref man, gunner, _w));
            Dispatch(gunner, new EmbarkExecutor());
            Assert.Equal(NodeStatus.Success, EmbarkNodes.Embark(ref man, gunner, _w));
            Assert.Equal(0, _w.GetComponentRO<IsEmbarkedTag>(gunner).Seat);                    // the Gunner seat
            Assert.Equal(gunner, WeaponSystems.ControllerOf(_w, tube));                        // he controls it
            // ⭐ IF-4 ① (WS-3) — and he may FIRE it: the weapon dispatcher fails a weapon action of a man without CanShoot before its
            //   executor runs (measured live: the mortar section never fired). He still cannot walk off with the tube.
            var caps = _w.GetComponentRO<ActorCapabilityState>(gunner).Capabilities;
            Assert.True(caps.HasFlag(ActorCapabilities.CanShoot), $"the gunner keeps his fire: {caps}");
            Assert.False(caps.HasFlag(ActorCapabilities.CanMove));
        }

        [Fact]
        public void CE1056_Disembark_LeavesBesideTheHull_ThroughTheOwner_AndNotAboardIsSuccess()
        {
            var vehicle = Vehicle(new Vector3(20f, 0f, 0f));
            var soldier = Soldier(new Vector3(20f, 0f, 0f));
            Embarkation.TryEmbark(_w, soldier, vehicle);
            var p = new DisembarkNodeParams();

            Assert.Equal(NodeStatus.Running, EmbarkNodes.Disembark(ref p, soldier, _w));
            Assert.Equal(BehaviorConstants.ActionIdDisembark, _w.GetComponentRO<InteractionChannel>(soldier).ActiveAction);
            Assert.True(_w.HasComponent<IsEmbarkedTag>(soldier));                                           // asked, not written
            Dispatch(soldier, new DisembarkExecutor());
            Assert.Equal(NodeStatus.Success, EmbarkNodes.Disembark(ref p, soldier, _w));

            Assert.False(_w.HasComponent<IsEmbarkedTag>(soldier));
            Assert.Equal(0, _w.GetComponentRO<PassengerBuffer>(vehicle).Count);
            Assert.True(_w.GetComponentRO<ActorCapabilityState>(soldier).Capabilities.HasFlag(ActorCapabilities.CanMove));
            Assert.True(Vector3.Distance(_w.GetComponentRO<SimTransform>(soldier).Position, new Vector3(20f, 0f, 0f)) > Embarkation.DefaultHullRadius);

            var p2 = new DisembarkNodeParams();
            Assert.Equal(NodeStatus.Success, EmbarkNodes.Disembark(ref p2, soldier, _w));                   // not aboard: nothing to do
            Assert.Equal(0, _w.GetComponentRO<InteractionChannel>(soldier).ActiveAction);
        }

        /// <summary>⭐ CE-1118 H-1 (docs/designs/airborne/DESIGN_Airborne_Insertion.md §6.5 D-18) — the node carries HOW he leaves: a jump
        /// from door 1 with a landing point ends, through the executor's command and the owner, as his Descent with that point as its Target.
        /// ✅ Red-proof: the executor not copying the channel's params ⇒ a step-out beside the hull, no Descent ⇒ red.</summary>
        [Fact]
        public void CE1118_Disembark_CarriesTheExit_AndAJumpersLandingPoint_IntoHisDescent()
        {
            if (!_w.IsComponentTypeRegistered<Fdp.Toolkit.Motion.Descent>()) _w.RegisterComponent<Fdp.Toolkit.Motion.Descent>();
            var heli = Vehicle(new Vector3(0f, 0f, 400f));
            var man = Soldier(new Vector3(0f, 0f, 400f));
            Embarkation.TryEmbark(_w, man, heli);
            var p = new DisembarkNodeParams { Exit = Fdp.Toolkit.Motion.ExitKind.JumpOut, AnchorIndex = 1, Landing = new Vector3(300f, 20f, 0f) };

            Assert.Equal(NodeStatus.Running, EmbarkNodes.Disembark(ref p, man, _w));
            Dispatch(man, new DisembarkExecutor());
            Assert.Equal(NodeStatus.Success, EmbarkNodes.Disembark(ref p, man, _w));
            var d = _w.GetComponentRO<Fdp.Toolkit.Motion.Descent>(man);
            Assert.Equal((Fdp.Toolkit.Motion.DescentKind.StaticLine, (byte)1, new Vector3(300f, 20f, 0f)), (d.Kind, d.AnchorIndex, d.Target));
        }

        // ── CE-1118 H-3 (docs/designs/airborne/DESIGN_Airborne_Insertion.md §6.5 D-24, D-25) — the ejection and the downed pilot ───────

        /// <summary>⭐ D-24 — <c>Eject</c> starts the aircraft's interaction action 11 once and forwards the executor's answer: Running until
        /// the aircraft is abandoned (its Health at 0), then Success, and the channel is handed back. ✅ Red-proof: the node not forwarding
        /// the answer ⇒ Running for ever ⇒ red.</summary>
        [Fact]
        public void CE1118_Eject_StartsActionEleven_AndSucceedsWhenTheAircraftIsAbandoned()
        {
            var jet = Soldier(new Vector3(0f, 0f, 300f));
            _w.AddComponent(jet, new Fdp.Toolkit.Combat.Components.Health { Current = 100f, Max = 100f });
            var p = new EjectNodeParams();
            Assert.Equal(NodeStatus.Running, AirborneIndividualNodes.Eject(ref p, jet, _w));
            Assert.Equal(BehaviorConstants.ActionIdEject, _w.GetComponentRO<InteractionChannel>(jet).ActiveAction);
            var exec = new EjectExecutor();
            ref var ch = ref _w.GetComponentRW<InteractionChannel>(jet);
            exec.OnEnter(jet, ref ch, _w);
            exec.Execute(jet, ref ch, _w, 0f);
            Assert.Equal(NodeStatus.Running, AirborneIndividualNodes.Eject(ref p, jet, _w));        // the ejection asked, the jet still alive
            _w.GetComponentRW<Fdp.Toolkit.Combat.Components.Health>(jet).Current = 0f;               // EjectionSystem: the pilot out, the jet ended
            ref var ch2 = ref _w.GetComponentRW<InteractionChannel>(jet);
            exec.Execute(jet, ref ch2, _w, 0f);
            Assert.Equal(NodeStatus.Success, AirborneIndividualNodes.Eject(ref p, jet, _w));
            Assert.Equal(0, _w.GetComponentRO<InteractionChannel>(jet).ActiveAction);                // handed back
        }

        /// <summary>⭐ D-25 ★ — the downed pilot waits while he is coming down, then gets AWAY: straight from the nearest remembered live
        /// enemy to a point 120 m from it; already far, or knowing no enemy, he needs no move. ✅ Red-proof: the move toward the enemy
        /// (the sign flipped) ⇒ the point 20 m from it ⇒ red.</summary>
        [Fact]
        public unsafe void CE1118_EvadeAndSignal_WaitsForTheGround_ThenGetsAwayFromTheNearestKnownEnemy()
        {
            if (!_w.IsComponentTypeRegistered<Fdp.Toolkit.Motion.Descent>()) _w.RegisterComponent<Fdp.Toolkit.Motion.Descent>();
            var pilot = Soldier(new Vector3(100f, 0f, 0f));
            _w.AddComponent(pilot, new Fdp.Toolkit.Motion.Descent { Kind = Fdp.Toolkit.Motion.DescentKind.Ejection, Phase = Fdp.Toolkit.Motion.DescentPhase.Landed });
            Assert.Equal(NodeStatus.Running, AirborneIndividualNodes.WaitForTheGround(pilot, _w));     // still in his harness
            _w.RemoveComponent<Fdp.Toolkit.Motion.Descent>(pilot);
            Assert.Equal(NodeStatus.Success, AirborneIndividualNodes.WaitForTheGround(pilot, _w));

            var p = new GetAwayParams { AwayMetres = 120f, Speed = 3f };
            Assert.Equal(NodeStatus.Success, AirborneIndividualNodes.GetAway(ref p, pilot, _w));      // knows nobody: nothing to get away from

            var enemy = _w.CreateEntity();
            _w.AddComponent(enemy, new SimTransform { Position = new Vector3(60f, 0f, 0f), Rotation = Quaternion.Identity });
            var mem = new Fdp.Toolkit.Perception.Components.TargetMemory { Count = 1 };
            mem.EntityIds[0] = (long)enemy.PackedValue;
            mem.PositionsX[0] = 60f; mem.PositionsY[0] = 0f;
            _w.AddComponent(pilot, mem);
            Assert.Equal(NodeStatus.Running, AirborneIndividualNodes.GetAway(ref p, pilot, _w));
            var move = _w.GetComponentRO<LocomotionChannel>(pilot);
            Assert.Equal(Fdp.Toolkit.Navigation.NavigationConstants.ActionIdMoveTo, move.ActiveAction);
            Assert.Equal(new Vector3(180f, 0f, 0f), p.To);                                            // ★ 120 m from the enemy, away from him

            var far = new GetAwayParams { AwayMetres = 30f };
            Assert.Equal(NodeStatus.Success, AirborneIndividualNodes.GetAway(ref far, pilot, _w));     // already 40 m off: no move
        }

        // ── CE-2152 C-b (C6, R-2011; docs/DESIGN_Entity_Interactions.md §7.4.10) — the CARRIER's pick-up and put-down ──────────────

        /// <summary>A soldier of a type declaring the person carrier and the injury band, through the real translators.</summary>
        private Entity Man(Vector3 at, float health)
        {
            var s = Soldier(at);
            _w.AddComponent(s, new Health { Current = health, Max = 100f });
            var t = new TkbTemplate("Rifleman", 4545L);
            t.AddDescriptor(CarrierDefDto.PersonCarrier());
            t.AddDescriptor(new InjuryDefDto());
            new CarrierTkbTranslator().Inject(_w, s, t);
            new InjuryTkbTranslator().Inject(_w, s, t);
            CombatLife.ApplyHitCapabilities(_w, s, in _w.GetComponentRO<Health>(s));
            return s;
        }

        private const ActorCapabilities MoveAndFire = ActorCapabilities.CanMove | ActorCapabilities.CanShoot;

        /// <summary>⭐ C6 — PickUp walks to the man who is down, hands the walk back in reach, asks the owner on the interaction channel, and
        /// answers Success once he is carried by this unit, dragged. ✅ Red-proof: PickUp not stopping its walk ⇒ the locomotion channel
        /// still holds the MoveTo ⇒ red.</summary>
        [Fact]
        public void CE2152_PickUp_WalksToTheCasualty_ThenTakesHimThroughTheOwner()
        {
            var carrier = Man(Vector3.Zero, 100f);
            var down = Man(new Vector3(10f, 0f, 0f), 20f);
            var p = new PickUpNodeParams { Casualty = down, Mode = CarryMode.Dragged };

            Assert.Equal(NodeStatus.Running, CarryNodes.PickUp(ref p, carrier, _w));
            Assert.Equal(NavigationConstants.ActionIdMoveTo, _w.GetComponentRO<LocomotionChannel>(carrier).ActiveAction);
            Assert.Equal(0, _w.GetComponentRO<InteractionChannel>(carrier).ActiveAction);              // not asking yet: out of reach

            _w.GetComponentRW<SimTransform>(carrier).Position = new Vector3(9f, 0f, 0f);              // the walk arrives
            Assert.Equal(NodeStatus.Running, CarryNodes.PickUp(ref p, carrier, _w));
            Assert.Equal(0, _w.GetComponentRO<LocomotionChannel>(carrier).ActiveAction);              // the walk handed back
            Assert.Equal(BehaviorConstants.ActionIdPickUp, _w.GetComponentRO<InteractionChannel>(carrier).ActiveAction);
            Assert.False(_w.HasComponent<IsEmbarkedTag>(down));                                        // asked, not written

            Dispatch(carrier, new PickUpExecutor());
            Assert.Equal(NodeStatus.Success, CarryNodes.PickUp(ref p, carrier, _w));
            Assert.Equal(carrier, _w.GetComponentRO<IsEmbarkedTag>(down).VehicleEntity);
            Assert.Equal(CarryMode.Dragged, _w.GetComponentRO<IsEmbarkedTag>(down).Mode);
            Assert.Equal(ActorCapabilities.CanMove, _w.GetComponentRO<ActorCapabilityState>(carrier).Capabilities & MoveAndFire);   // C7
            Assert.Equal(0, _w.GetComponentRO<InteractionChannel>(carrier).ActiveAction);             // the channel handed back
        }

        /// <summary>⭐ <c>CE-2152</c> R-e — the walk to him ends just short of reach (📐 measured live: the medic stopped 1.7 m from the man,
        /// whose body had been nudged): the node walks to him again rather than giving up — at most <c>MaxWalks</c> times in all, then it
        /// fails. 🔴 Red before: the first walk that ended out of reach failed the pick-up.</summary>
        [Fact]
        public void CE2152_PickUp_AWalkThatEndsJustShort_WalksAgain_ThenGivesUp()
        {
            var carrier = Man(Vector3.Zero, 100f);
            var down = Man(new Vector3(10f, 0f, 0f), 20f);
            var p = new PickUpNodeParams { Casualty = down, Mode = CarryMode.Dragged };
            Assert.Equal(NodeStatus.Running, CarryNodes.PickUp(ref p, carrier, _w));
            for (int walk = 1; walk < CarryNodes.MaxWalks; walk++)
            {
                _w.GetComponentRW<SimTransform>(carrier).Position = new Vector3(8f, 0f, 0f);              // 2 m short of him …
                _w.GetComponentRW<LocomotionChannel>(carrier).Status = NodeStatus.Success;                // … and the walk says it arrived
                Assert.Equal(NodeStatus.Running, CarryNodes.PickUp(ref p, carrier, _w));
                Assert.Equal(NavigationConstants.ActionIdMoveTo, _w.GetComponentRO<LocomotionChannel>(carrier).ActiveAction);   // walking again
                Assert.Equal(NodeStatus.Running, _w.GetComponentRO<LocomotionChannel>(carrier).Status);
            }
            _w.GetComponentRW<LocomotionChannel>(carrier).Status = NodeStatus.Success;
            Assert.Equal(NodeStatus.Failure, CarryNodes.PickUp(ref p, carrier, _w));                      // the last walk too: give up
        }

        /// <summary>⭐ C-b — the carry end to end, node by node as the CarryCasualty tree runs them: pick him up, the plain move to the point
        /// starts AFRESH (not read as already arrived), put him down where he is held — beside the point, still down — and the carrier gets
        /// his fire back. ✅ Red-proofs: LeavePoint using the hull column for a person ⇒ he lands ~1 m to the carrier's right ⇒ red;
        /// PickUp not stopping its walk ⇒ the move node answers Success at once ⇒ red.</summary>
        [Fact]
        public void CE2152_TheCarrierDragsTheCasualtyToAPoint_AndPutsHimDownThere()
        {
            var carrier = Man(new Vector3(9f, 0f, 0f), 100f);
            var down = Man(new Vector3(10f, 0f, 0f), 20f);
            var take = new PickUpNodeParams { Casualty = down, Mode = CarryMode.Dragged };
            _w.GetComponentRW<LocomotionChannel>(carrier).ActiveAction = NavigationConstants.ActionIdMoveTo;   // a walk that arrived …
            _w.GetComponentRW<LocomotionChannel>(carrier).Status = NodeStatus.Success;                          // … and said so
            take.Walking = 1;
            Assert.Equal(NodeStatus.Running, CarryNodes.PickUp(ref take, carrier, _w));
            Dispatch(carrier, new PickUpExecutor());
            Assert.Equal(NodeStatus.Success, CarryNodes.PickUp(ref take, carrier, _w));

            var bring = new CgfNodes.MoveToLocationParams { X = 30f, Y = 0f, Speed = 3f, ArrivalRadius = 1.5f };
            Assert.Equal(NodeStatus.Running, CgfNodes.Action_WriteMoveToChannel(ref bring, carrier, _w));   // a NEW move, not the old Success

            // the drag arrives: the carrier at the point, facing +X; the casualty held 1 m behind him (EmbarkedStateSystem)
            _w.GetComponentRW<SimTransform>(carrier).Position = new Vector3(30f, 0f, 0f);
            new EmbarkedStateSystem().Execute(_w, 0.016f);
            Assert.Equal(29f, _w.GetComponentRO<SimTransform>(down).Position.X, 3);

            // ⭐ CE-2173 slice 3c — "keep him" (CarryCasualty keep / SquadRecover): Success, and he stays on his carrier
            var keep = new PutDownNodeParams { Skip = 1 };
            Assert.Equal(NodeStatus.Success, CarryNodes.PutDown(ref keep, carrier, _w));
            Assert.Equal(carrier, _w.GetComponentRO<IsEmbarkedTag>(down).VehicleEntity);
            Assert.NotEqual(BehaviorConstants.ActionIdPutDown, _w.GetComponentRO<InteractionChannel>(carrier).ActiveAction);

            var leave = new PutDownNodeParams();
            Assert.Equal(NodeStatus.Running, CarryNodes.PutDown(ref leave, carrier, _w));
            Assert.Equal(BehaviorConstants.ActionIdPutDown, _w.GetComponentRO<InteractionChannel>(carrier).ActiveAction);
            Dispatch(carrier, new PutDownExecutor());
            Assert.Equal(NodeStatus.Success, CarryNodes.PutDown(ref leave, carrier, _w));

            Assert.False(_w.HasComponent<IsEmbarkedTag>(down));
            Assert.Equal(new Vector3(29f, 0f, 0f), _w.GetComponentRO<SimTransform>(down).Position);          // C-b1: where he was held
            Assert.Equal((ActorCapabilities)0, _w.GetComponentRO<ActorCapabilityState>(down).Capabilities & MoveAndFire);   // still down
            Assert.Equal(MoveAndFire, _w.GetComponentRO<ActorCapabilityState>(carrier).Capabilities & MoveAndFire);         // C7: fire back
            var again = new PutDownNodeParams();
            Assert.Equal(NodeStatus.Success, CarryNodes.PutDown(ref again, carrier, _w));                     // carrying nobody: at once
        }

        /// <summary>⭐ C3 / C-b4 — a man carried by someone else fails at once; a man who is NOT down is refused by the rules, and the
        /// carrier's pick-up fails when no answer comes. ✅ Red-proof: PickUp treating "carried by another" like "not yet carried" ⇒ it
        /// walks to him ⇒ red.</summary>
        [Fact]
        public void CE2152_PickUp_FailsForAManCarriedByAnother_AndForAManWhoIsNotDown()
        {
            var a = Man(new Vector3(0f, 5f, 0f), 100f);
            var b = Man(Vector3.Zero, 100f);
            var down = Man(new Vector3(1f, 0f, 0f), 20f);
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(_w, down, a, mode: CarryMode.Dragged));
            var p = new PickUpNodeParams { Casualty = down };
            Assert.Equal(NodeStatus.Failure, CarryNodes.PickUp(ref p, b, _w));
            Assert.Equal(0, _w.GetComponentRO<LocomotionChannel>(b).ActiveAction);

            var healthy = Man(new Vector3(1f, 1f, 0f), 90f);
            var q = new PickUpNodeParams { Casualty = healthy };
            Assert.Equal(NodeStatus.Running, CarryNodes.PickUp(ref q, b, _w));
            var exec = new PickUpExecutor();
            Dispatch(b, exec);
            Assert.False(_w.HasComponent<IsEmbarkedTag>(healthy));                                        // the rules refused
            ref var ch = ref _w.GetComponentRW<InteractionChannel>(b);
            exec.Execute(b, ref ch, _w, PickUpExecutor.ResultTimeoutSeconds + 1f);
            Assert.Equal(NodeStatus.Failure, CarryNodes.PickUp(ref q, b, _w));
        }

        [Fact]
        public void CE1056_Embark_AGoneVehicle_Fails_AndTheBoardingPointLiesWithinTheBoardingRange()
        {
            var soldier = Soldier(Vector3.Zero);
            var gone = new EmbarkNodeParams { Vehicle = Entity.Null };
            Assert.Equal(NodeStatus.Failure, EmbarkNodes.Embark(ref gone, soldier, _w));

            // the executor's range test agrees with where the walk stops: the boarding point lies within BoardingRange
            var vehicle = Vehicle(new Vector3(5f, 5f, 0f));
            Assert.True(Vector3.Distance(Embarkation.BoardingPoint(_w, vehicle), new Vector3(5f, 5f, 0f)) < Embarkation.BoardingRange(_w, vehicle));
        }
    

        // ── CE-2173 slice 3 (B10; docs/designs/air-attack/DESIGN_Air_Operations.md §9) — a carried man is SEATED, not boarded on his carrier ──

        /// <summary>⭐ <c>CE-2173</c> — LoadCasualty walks the carrier to the vehicle with the man carried, puts him down beside it, then boards
        /// HIM (the embark action with <c>Passenger</c> = the man): he ends in the vehicle's own seat, nobody carries him, and the carrier
        /// is free to board. ✅ Red-proofs: boarding the carrier instead (Passenger left null) ⇒ the man is never aboard the vehicle ⇒ red;
        /// skipping the put-down ⇒ the man is still on his carrier's list when he is asked aboard ⇒ red.</summary>
        [Fact]
        public void CE2173_LoadCasualty_PutsTheCarriedManDown_AndSeatsHimInTheVehicle()
        {
            var vehicle = Vehicle(new Vector3(30f, 0f, 0f));
            var carrier = Man(new Vector3(9f, 0f, 0f), 100f);
            var down = Man(new Vector3(10f, 0f, 0f), 20f);
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(_w, down, carrier, mode: CarryMode.Carried));
            var p = new LoadCasualtyNodeParams { Vehicle = vehicle };

            Assert.Equal(NodeStatus.Running, CarryNodes.LoadCasualty(ref p, carrier, _w));
            Assert.Equal(NavigationConstants.ActionIdMoveTo, _w.GetComponentRO<LocomotionChannel>(carrier).ActiveAction);   // walks, carrying
            Assert.Equal(0, _w.GetComponentRO<InteractionChannel>(carrier).ActiveAction);

            _w.GetComponentRW<SimTransform>(carrier).Position = Embarkation.BoardingPoint(_w, vehicle);       // the walk arrives
            Assert.Equal(NodeStatus.Running, CarryNodes.LoadCasualty(ref p, carrier, _w));
            Assert.Equal(BehaviorConstants.ActionIdPutDown, _w.GetComponentRO<InteractionChannel>(carrier).ActiveAction);   // first: down
            Dispatch(carrier, new PutDownExecutor());
            Assert.True(Embarkation.LoadOf(_w, carrier).IsNull);
            // ⭐ CE-2225 — he lies a body-length from his carrier, on the far side from the vehicle: OUTSIDE the boarding range himself
            //   (live ua-air-rescue). The loader is in range, so the loading goes on.
            var away = Vector3.Normalize(Embarkation.BoardingPoint(_w, vehicle) - _w.GetComponentRO<SimTransform>(vehicle).Position);
            _w.GetComponentRW<SimTransform>(down).Position = Embarkation.BoardingPoint(_w, vehicle) + away * 2.5f;
            Assert.True(Vector3.Distance(_w.GetComponentRO<SimTransform>(down).Position, _w.GetComponentRO<SimTransform>(vehicle).Position)
                        > Embarkation.BoardingRange(_w, vehicle));

            Assert.Equal(NodeStatus.Running, CarryNodes.LoadCasualty(ref p, carrier, _w));
            Assert.Equal(BehaviorConstants.ActionIdEmbark, _w.GetComponentRO<InteractionChannel>(carrier).ActiveAction);    // then: seat him
            Dispatch(carrier, new EmbarkExecutor());
            Assert.Equal(NodeStatus.Success, CarryNodes.LoadCasualty(ref p, carrier, _w));

            Assert.Equal(vehicle, _w.GetComponentRO<IsEmbarkedTag>(down).VehicleEntity);                      // in the vehicle's seat
            Assert.Equal(CarryMode.Seated, _w.GetComponentRO<IsEmbarkedTag>(down).Mode);
            Assert.Equal(1, _w.GetComponentRO<PassengerBuffer>(vehicle).Count);
            Assert.False(_w.HasComponent<IsEmbarkedTag>(carrier));                                            // the carrier is still out
            Assert.Equal(0, _w.GetComponentRO<InteractionChannel>(carrier).ActiveAction);                     // the channel handed back

            var none = new LoadCasualtyNodeParams { Vehicle = vehicle };
            Assert.Equal(NodeStatus.Success, CarryNodes.LoadCasualty(ref none, Man(Vector3.Zero, 100f), _w));  // carrying nobody: done
        }

        // ── CE-1154 (docs/DESIGN_Entity_Control.md §8h U-1, U-2, U-6, U-7, §8h.6) — the operator's USE key on the brain ───────────────

        private Fdp.Toolkit.Replication.Services.NetworkEntityMap NetMap()
        {
            if (!_w.HasSingletonManaged<Fdp.Toolkit.Replication.Services.NetworkEntityMap>())
                _w.SetSingletonManaged(new Fdp.Toolkit.Replication.Services.NetworkEntityMap());
            return _w.GetSingletonManaged<Fdp.Toolkit.Replication.Services.NetworkEntityMap>()!;
        }

        private long NetIdOf(Entity e)
        {
            long id = 7000 + e.Index;
            if (!NetMap().TryGetEntity(id, out _)) NetMap().Register(id, e);
            return id;
        }

        /// <summary>A possessed operator: a soldier with a navigation intent (the operator behaviour drives it) and a station frame.</summary>
        private Entity Operator(Vector3 at)
        {
            var s = Man(at, 100f);
            _w.AddComponent(s, new NavigationIntent());
            // the possession registers the frame component on the brain host when it first holds a unit (PossessionSystem)
            if (!_w.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Possession.OperatorInput>()) _w.RegisterComponent<Fdp.Toolkit.Behavior.Possession.OperatorInput>();
            _w.AddComponent(s, new Fdp.Toolkit.Behavior.Possession.OperatorInput { Seq = 1 });
            return s;
        }

        private Entity DoorAt(Vector3 at, Fdp.Toolkit.Terrain.TerrainDoorState state)
        {
            var d = _w.CreateEntity();
            _w.AddComponent(d, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(d, new Fdp.Toolkit.Terrain.DoorState { State = state });
            return d;
        }

        /// <summary>The station's next frame: a new <c>Seq</c>; a use press bumps <c>UseSeq</c> and names the target.</summary>
        private void Press(Entity op, Entity target, byte verb = 0)
        {
            ref var input = ref _w.GetComponentRW<Fdp.Toolkit.Behavior.Possession.OperatorInput>(op);
            input.Seq++;
            unchecked { input.UseSeq++; }
            input.UseVerb = verb;
            input.UseTarget = target.IsNull ? 0 : NetIdOf(target);
        }

        private void Tick(Entity op, ref OperatorControlBehavior.ManualControlState ws)
        {
            var p = default(OperatorControlBehavior.ManualControlParams);
            OperatorControlBehavior.ManualControl(ref p, ref ws, op, _w);
        }

        private Fdp.Toolkit.Behavior.Possession.OperatorUseStatus StatusOf(Entity op) => _w.GetComponentRO<Fdp.Toolkit.Behavior.Possession.OperatorUseStatus>(op);

        /// <summary>What the interaction dispatcher and the door's owner do for a door action: enter, spend its time, raise the one
        /// command, the owner applies it next frame, the executor reads the replicated state back.</summary>
        private void DispatchDoor(Entity op, Fdp.Toolkit.Terrain.DoorVerb verb)
        {
            var exec = new DoorActionExecutor(verb);
            ref var ch = ref _w.GetComponentRW<InteractionChannel>(op);
            exec.OnEnter(op, ref ch, _w);
            exec.Execute(op, ref ch, _w, Fdp.Toolkit.Terrain.DoorRules.ActionSeconds(verb) + 0.1f);
            _w.Bus.SwapBuffers();
            new Fdp.Toolkit.Terrain.DoorCommandSystem().Execute(_w, 0.016f);
            ref var ch2 = ref _w.GetComponentRW<InteractionChannel>(op);
            if (ch2.Status == NodeStatus.Running) exec.Execute(op, ref ch2, _w, 0.016f);
        }

        /// <summary>⭐ <c>CE-1154</c> (U-7) — one E on a closed door within reach opens it, a second closes it; the brain's verdict is
        /// Running when the action starts and Done when it ends, each for the press it answers, and the channel is handed back between.
        /// ✅ Red-proof: OperateUse not called from ManualControl ⇒ no action on the channel ⇒ red.</summary>
        [Fact]
        public void CE1154_OneUseOpensAClosedDoor_ASecondClosesIt_AndTheVerdictFollowsEachPress()
        {
            var op = Operator(Vector3.Zero);
            var door = DoorAt(new Vector3(1f, 0f, 0f), Fdp.Toolkit.Terrain.TerrainDoorState.Closed);
            var ws = default(OperatorControlBehavior.ManualControlState);
            Tick(op, ref ws);
            Assert.Equal(0, _w.GetComponentRO<InteractionChannel>(op).ActiveAction);              // no press yet

            Press(op, door);
            Tick(op, ref ws);
            Assert.Equal(BehaviorConstants.ActionIdOpenDoor, _w.GetComponentRO<InteractionChannel>(op).ActiveAction);
            Assert.Equal((ws.UseSeq, BehaviorConstants.ActionIdOpenDoor, Fdp.Toolkit.Behavior.Possession.UseOutcome.Running),
                (StatusOf(op).UseSeq, StatusOf(op).ActionId, StatusOf(op).Outcome));
            DispatchDoor(op, Fdp.Toolkit.Terrain.DoorVerb.Open);
            Tick(op, ref ws);
            Assert.Equal(Fdp.Toolkit.Terrain.TerrainDoorState.Open, _w.GetComponentRO<Fdp.Toolkit.Terrain.DoorState>(door).State);
            Assert.Equal(Fdp.Toolkit.Behavior.Possession.UseOutcome.Done, StatusOf(op).Outcome);
            Assert.Equal(0, _w.GetComponentRO<InteractionChannel>(op).ActiveAction);              // handed back

            Tick(op, ref ws);
            Assert.Equal(0, _w.GetComponentRO<InteractionChannel>(op).ActiveAction);              // a frame with no new press does nothing

            Press(op, door);                                                                     // the toggle: open ⇒ close
            Tick(op, ref ws);
            Assert.Equal(BehaviorConstants.ActionIdCloseDoor, _w.GetComponentRO<InteractionChannel>(op).ActiveAction);
            DispatchDoor(op, Fdp.Toolkit.Terrain.DoorVerb.Close);
            Tick(op, ref ws);
            Assert.Equal(Fdp.Toolkit.Terrain.TerrainDoorState.Closed, _w.GetComponentRO<Fdp.Toolkit.Terrain.DoorState>(door).State);
            Assert.Equal((ws.UseSeq, Fdp.Toolkit.Behavior.Possession.UseOutcome.Done), (StatusOf(op).UseSeq, StatusOf(op).Outcome));
        }

        /// <summary>⭐ <c>CE-1154</c> (U-3) — out of reach the brain REFUSES with the rule's reason and starts nothing: no walk-to, the
        /// door unchanged; a locked door says Locked; a verb past the target's list says NothingToDo. ✅ Red-proof: starting the action
        /// without checking <c>Available</c> ⇒ the channel holds Open door ⇒ red.</summary>
        [Fact]
        public void CE1154_OutOfReach_IsRefusedWithItsReason_AndNothingStarts()
        {
            var op = Operator(Vector3.Zero);
            var far = DoorAt(new Vector3(Fdp.Toolkit.Terrain.DoorRules.ReachMetres + 2f, 0f, 0f), Fdp.Toolkit.Terrain.TerrainDoorState.Closed);
            var ws = default(OperatorControlBehavior.ManualControlState);
            Press(op, far);
            Tick(op, ref ws);
            Assert.Equal((Fdp.Toolkit.Behavior.Possession.UseOutcome.Refused, Fdp.Toolkit.Behavior.Possession.UseReason.OutOfReach),
                (StatusOf(op).Outcome, StatusOf(op).Reason));
            Assert.Equal(0, _w.GetComponentRO<InteractionChannel>(op).ActiveAction);
            Assert.Equal(0, _w.GetComponentRO<LocomotionChannel>(op).ActiveAction);              // no walk-to
            Assert.Equal(Fdp.Toolkit.Terrain.TerrainDoorState.Closed, _w.GetComponentRO<Fdp.Toolkit.Terrain.DoorState>(far).State);

            var locked = DoorAt(new Vector3(1f, 0f, 0f), Fdp.Toolkit.Terrain.TerrainDoorState.Locked);
            Press(op, locked);
            Tick(op, ref ws);
            Assert.Equal(Fdp.Toolkit.Behavior.Possession.UseReason.Locked, StatusOf(op).Reason);

            Press(op, locked, verb: 7);                                                         // past the list (Open, Unlock, Breach)
            Tick(op, ref ws);
            Assert.Equal((Fdp.Toolkit.Behavior.Possession.UseOutcome.Refused, Fdp.Toolkit.Behavior.Possession.UseReason.NothingToDo),
                (StatusOf(op).Outcome, StatusOf(op).Reason));
            Assert.Equal(0, _w.GetComponentRO<InteractionChannel>(op).ActiveAction);
        }

        /// <summary>⭐ <c>CE-1154</c> (U-6) — E on a man who is down drags him (the rule's primary), E again — whatever the crosshair rests
        /// on — puts him down. ✅ Red-proof: the params not written ⇒ the pick-up names no casualty ⇒ the executor fails ⇒ red.</summary>
        [Fact]
        public void CE1154_UseOnAManDown_DragsHim_AndUseAgain_PutsHimDown()
        {
            var op = Operator(Vector3.Zero);
            var down = Man(new Vector3(1f, 0f, 0f), 20f);
            var ws = default(OperatorControlBehavior.ManualControlState);

            Press(op, down);
            Tick(op, ref ws);
            Assert.Equal(BehaviorConstants.ActionIdPickUp, _w.GetComponentRO<InteractionChannel>(op).ActiveAction);
            Dispatch(op, new PickUpExecutor());
            Tick(op, ref ws);
            Assert.Equal(Fdp.Toolkit.Behavior.Possession.UseOutcome.Done, StatusOf(op).Outcome);
            Assert.Equal(op, _w.GetComponentRO<IsEmbarkedTag>(down).VehicleEntity);
            Assert.Equal(CarryMode.Dragged, _w.GetComponentRO<IsEmbarkedTag>(down).Mode);

            Press(op, Entity.Null);                                                              // the crosshair on nothing
            Tick(op, ref ws);
            Assert.Equal(BehaviorConstants.ActionIdPutDown, _w.GetComponentRO<InteractionChannel>(op).ActiveAction);
            Dispatch(op, new PutDownExecutor());
            Tick(op, ref ws);
            Assert.Equal(Fdp.Toolkit.Behavior.Possession.UseOutcome.Done, StatusOf(op).Outcome);
            Assert.False(_w.HasComponent<IsEmbarkedTag>(down));
        }

        /// <summary>⭐ <c>CE-1154</c> — a new station's frames start again from 1 and its use counter from 0 (the trigger's rule): its
        /// first press is acted on even though the counter equals one this brain already acted on. ✅ Red-proof: no reset on a stale
        /// <c>Seq</c> ⇒ the press reads as old ⇒ nothing starts ⇒ red.</summary>
        [Fact]
        public void CE1154_ANewStationsFirstPress_IsActedOn_AfterTheCounterReset()
        {
            var op = Operator(Vector3.Zero);
            var door = DoorAt(new Vector3(1f, 0f, 0f), Fdp.Toolkit.Terrain.TerrainDoorState.Closed);
            var ws = default(OperatorControlBehavior.ManualControlState);
            ref var input = ref _w.GetComponentRW<Fdp.Toolkit.Behavior.Possession.OperatorInput>(op);
            input.Seq = 40; input.UseSeq = 1; input.UseTarget = NetIdOf(door);
            Tick(op, ref ws);                                                                    // the old station's press 1
            DispatchDoor(op, Fdp.Toolkit.Terrain.DoorVerb.Open);
            Tick(op, ref ws);
            Assert.Equal(Fdp.Toolkit.Terrain.TerrainDoorState.Open, _w.GetComponentRO<Fdp.Toolkit.Terrain.DoorState>(door).State);

            ref var fresh = ref _w.GetComponentRW<Fdp.Toolkit.Behavior.Possession.OperatorInput>(op);
            fresh.Seq = 1; fresh.UseSeq = 0;                                                     // a new station: frame 1, no press yet
            Tick(op, ref ws);
            Assert.Equal(0, _w.GetComponentRO<InteractionChannel>(op).ActiveAction);
            Press(op, door);                                                                     // its press 1 (Seq 2)
            Tick(op, ref ws);
            Assert.Equal(BehaviorConstants.ActionIdCloseDoor, _w.GetComponentRO<InteractionChannel>(op).ActiveAction);
        }

        // ── CE-1155 (docs/DESIGN_Entity_Control.md §8h U-5; Weapon Systems §6 WS-6) — E on a standalone weapon: man it, fire it ──────

        private const long KornetType = 115501;

        /// <summary>A standalone ATGM on a tripod: its own Gunner seat, it needs an operator, its type's one mount is an ATGM.</summary>
        private Entity Kornet(Vector3 at)
        {
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var t = new Fdp.Interfaces.TkbTemplate("Kornet", KornetType);
            t.AddDescriptor(new WeaponSuiteDto { Mounts = { new WeaponMountDto { Category = WeaponCategory.Atgm, TurretIndex = -1 } } });
            db.Register(t);
            _w.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            var k = Vehicle(at);
            var seats = new CarrierSeats { SeatCount = 1 };
            seats.Roles[0] = SeatRole.Gunner;
            _w.AddComponent(k, seats);
            _w.AddComponent(k, new WeaponSystemParams { NeedsOperator = 1 });
            _w.AddComponent(k, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = KornetType });
            _w.AddComponent(k, new WeaponState { Ammo = 2 });
            return k;
        }

        /// <summary>⭐ <c>CE-1155</c> — E on a standalone weapon takes its GUNNER seat (the rule's "Man", through the embark executor and the
        /// owner), and he then controls it; possession stays on him. 📐 Measured gap, then fixed: his weapon keys name the controlled
        /// mount (32+k) — the executor already fires the weapon through it (WS-3), but <c>OperateWeapon</c> read that index on the MAN, so
        /// the weapon's type was lost: an ATGM fired by sight with a held trigger repeating. Now the weapon's own mount decides — it LOCKS one
        /// round per press. ✅ Red-proof: <c>OperatedMount</c> returning the man ⇒ the shot is a SIGHT shot (Pending 2) ⇒ red.</summary>
        [Fact]
        public unsafe void CE1155_UseOnAStandaloneWeapon_MansItsGunnerSeat_AndItsControlledMountFiresAsThatWeapon()
        {
            var op = Operator(Vector3.Zero);
            _w.AddComponent(op, new WeaponChannel());
            var kornet = Kornet(new Vector3(1f, 0f, 0f));
            var ws = default(OperatorControlBehavior.ManualControlState);

            Press(op, kornet);
            Tick(op, ref ws);
            Assert.Equal(BehaviorConstants.ActionIdEmbark, _w.GetComponentRO<InteractionChannel>(op).ActiveAction);
            Dispatch(op, new EmbarkExecutor());
            Tick(op, ref ws);
            Assert.Equal(Fdp.Toolkit.Behavior.Possession.UseOutcome.Done, StatusOf(op).Outcome);
            Assert.Equal(0, _w.GetComponentRO<IsEmbarkedTag>(op).Seat);                          // the Gunner seat
            Assert.Equal(op, WeaponSystems.ControllerOf(_w, kornet));                             // he controls it
            Assert.Equal(kornet, WeaponSystems.ControlledAt(_w, op, WeaponSystems.ControlledMountBase));

            // his weapon keys select the controlled mount; one trigger press, held
            ref var input = ref _w.GetComponentRW<Fdp.Toolkit.Behavior.Possession.OperatorInput>(op);
            input.Seq++; input.Mount = (byte)WeaponSystems.ControlledMountBase; input.ShotSeq = 1; input.Trigger = 1;
            input.AimPoint = new Vector3(300f, 0f, 0f);
            Tick(op, ref ws);
            Assert.Equal((byte)1, ws.Pending);                                                    // an ATGM LOCKS (not a sight shot)
            var p = System.Runtime.CompilerServices.Unsafe.As<byte, Fdp.Toolkit.Combat.Executors.AimAndFireParams>(ref _w.GetComponentRW<WeaponChannel>(op).Params[0]);
            Assert.Equal(WeaponSystems.ControlledMountBase, (int)p.Mount);

            ref var cancel = ref _w.GetComponentRW<Fdp.Toolkit.Behavior.Possession.OperatorInput>(op);
            cancel.Seq++; cancel.CancelSeq = 1;                                                   // RMB drops the lock; the trigger stays held
            Tick(op, ref ws);
            p = System.Runtime.CompilerServices.Unsafe.As<byte, Fdp.Toolkit.Combat.Executors.AimAndFireParams>(ref _w.GetComponentRW<WeaponChannel>(op).Params[0]);
            Assert.Equal((byte)0, p.Trigger);                                                     // an ATGM fires one round per press

            Press(op, Entity.Null);                                                               // E again, aboard: get out
            Tick(op, ref ws);
            Assert.Equal(BehaviorConstants.ActionIdDisembark, _w.GetComponentRO<InteractionChannel>(op).ActiveAction);
        }
    }
}
