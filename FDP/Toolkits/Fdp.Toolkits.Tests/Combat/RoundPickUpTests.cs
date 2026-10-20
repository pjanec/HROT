using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Combat.Translators;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Tests.Combat
{
    /// <summary>
    /// ⭐⭐ <c>CE-3235</c> (B-13, AW-8 / AW-9; docs/DESIGN_Civilians_And_Crowd_Control.md §7.10) — a stone LIES where it stopped for its time,
    /// then goes without a burst; a man asks for a lying round, the node that flies it grants it to ONE man and removes it; the grant fills
    /// his hands; a live round keeps its fuze in his hand and, run out, is thrown at his own feet.
    /// </summary>
    public sealed class RoundPickUpTests : IDisposable
    {
        private const long CivilianType = 9401, StoneAmmo = 9490, GrenadeAmmo = 9491, BulletAmmo = 9492, CanisterAmmo = 9493, HatchetAmmo = 9494;
        private const float Dt = 1f / 60f;

        private readonly EntityRepository _world = new();
        private readonly TkbDatabase _db = new();
        private readonly RoundPickUpSystem _pickUp = new();
        private uint _tick = 1;

        public RoundPickUpTests()
        {
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<SimVelocity>();
            _world.RegisterComponent<WeaponState>();
            _world.RegisterComponent<WeaponMountInfo>();
            _world.RegisterComponent<PartMetadata>();
            _world.RegisterComponent<AmmoLoadout>();
            _world.RegisterComponent<TkbIdentity>();
            _world.RegisterComponent<EntityInfo>();
            _world.RegisterComponent<Health>();
            _world.RegisterComponent<BehaviorState>();
            _world.RegisterComponent<BallisticProjectile>();
            _world.RegisterComponent<PhysicsCollider>();
            _world.RegisterComponent<HeldFuze>();
            _world.RegisterEvent<WeaponFireIntent>();
            _world.RegisterEvent<PickUpRoundEvent>();
            _world.RegisterEvent<PickUpGrantEvent>();
            _world.RegisterEvent<RaycastRequestEvent>();
            _world.RegisterEvent<DetonationNotification>();
            _world.RegisterEvent<DamageAssessedEvent>();
            _world.SetSingleton(new GlobalTime { FrameNumber = _tick, TimeScale = 1f });

            var civilian = new TkbTemplate("Civilian", CivilianType);
            civilian.AddDescriptor(new VehicleParametersDto { VehicleClass = global::CarKinem.Core.VehicleClass.Pedestrian });
            _db.Register(civilian);
            _db.Register(Round("Stone", StoneAmmo, new WarheadDto { Kind = WarheadKind.Blunt, Indirect = true }, new HandItemDto { LiesSeconds = 60f }));
            _db.Register(Round("Grenade", GrenadeAmmo, new WarheadDto { Kind = WarheadKind.Fragmentation, BlastInjuryRadiusM = 5f, Indirect = true,
                                                                        Fuze = FuzeKind.Time, FuzeDelayS = 4.5f }, new HandItemDto()));
            _db.Register(Round("Bullet", BulletAmmo, null, null));
            _db.Register(Round("Gas canister", CanisterAmmo, new WarheadDto { Kind = WarheadKind.Irritant, EffectSeconds = 2f, CloudRadiusM = 8f,
                                                                              CloudSeconds = 90f, Indirect = true }, new HandItemDto()));
            _db.Register(Round("Hatchet", HatchetAmmo, Hatchet, HatchetLies));   // ⭐ CE-3247 — a thrown blade (the built-in's numbers)
            PersonHands.EnsureOnPersons(_db);
            _world.SetSingletonManaged<ITkbDatabase>(_db);
        }

        public void Dispose() => _world.Dispose();

        private static TkbTemplate Round(string name, long type, WarheadDto? warhead, HandItemDto? hand)
        {
            var t = new TkbTemplate(name, type);
            if (warhead != null) t.AddDescriptor(warhead);
            if (hand != null) t.AddDescriptor(hand);
            return t;
        }

        private Entity Man(Vector3 at)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _world.AddComponent(e, new TkbIdentity { TkbType = CivilianType });
            _world.AddComponent(e, new EntityInfo { ForceId = ForceId.Neutral });
            _world.AddComponent(e, new Health { Current = 100f, Max = 100f });
            _world.AddComponent(e, default(BehaviorState));
            _world.SetAuthority<BehaviorState>(e, true);   // his brain runs here
            _db.TryGetByType(CivilianType, out var t);
            new CombatTkbTranslator().Inject(_world, e, t!);
            return e;
        }

        /// <summary>A round as the flight leaves it: <paramref name="bits"/>, stopped at <paramref name="at"/>.</summary>
        private Entity Lying(long ammo, Vector3 at, byte bits, float fuze)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _world.AddComponent(e, new SimVelocity());
            _world.AddComponent(e, new BallisticProjectile
            {
                PreviousPosition = at, Muzzle = at, SpawnTick = 1, StoppedTick = 1, TerrainFlags = 1, Ammo = ammo, Warhead = bits, FuzeRemaining = fuze,
            });
            return e;
        }

        /// <summary>One frame: the bus swaps (last frame's events become readable), then the pick-up system.</summary>
        private void Frame()
        {
            _world.Bus.SwapBuffers();
            _pickUp.Execute(_world, Dt);
        }

        private void Ballistics(BallisticsSystem sys, float dt)
        {
            _world.SetSingleton(new GlobalTime { FrameNumber = ++_tick, TimeScale = 1f });
            _world.Bus.SwapBuffers();
            sys.Execute(_world, dt);
        }

        /// <summary>⭐ PU-1 — an inert hand item LINGERS (its lying time rides as the fuze); a round that bursts never does.
        /// ✅ Red-proof: the Lingers branch dropped from BitsOf ⇒ red.</summary>
        [Fact]
        public void CE3235_AnInertHandItem_Lingers_ARoundThatBursts_DoesNot()
        {
            byte stone = WarheadRound.BitsOf(new WarheadDto { Kind = WarheadKind.Blunt, Indirect = true }, new HandItemDto { LiesSeconds = 60f }, out float lies);
            Assert.True((stone & WarheadRound.Lingers) != 0);
            Assert.Equal(60f, lies);
            byte grenade = WarheadRound.BitsOf(new WarheadDto { Kind = WarheadKind.Fragmentation, BlastInjuryRadiusM = 5f, Fuze = FuzeKind.Time, FuzeDelayS = 4.5f },
                                               new HandItemDto { LiesSeconds = 60f }, out float fuze);
            Assert.Equal(0, grenade & WarheadRound.Lingers);
            Assert.Equal(4.5f, fuze);   // a live round keeps its own fuze
            Assert.Equal(0, WarheadRound.BitsOf(new WarheadDto { Kind = WarheadKind.Blunt }, null, out _) & WarheadRound.Lingers);   // not a hand item
        }

        /// <summary>⭐ PU-1 — a stopped stone stays through the grace, then LIES (Landed) for its time, then goes WITHOUT a burst; an ordinary
        /// stopped round still goes after the grace. ✅ Red-proof: the linger branch in BallisticsSystem dropped ⇒ the stone is gone at the
        /// grace ⇒ red.</summary>
        [Fact]
        public void CE3235_AStone_LiesItsTimeWhereItStopped_ThenGoesWithoutABurst()
        {
            var sys = new BallisticsSystem();
            var stone = Lying(StoneAmmo, new Vector3(5, 0, 0), WarheadRound.Arc | WarheadRound.Lingers, 60f);
            var bullet = Lying(BulletAmmo, new Vector3(9, 0, 0), 0, 0f);
            for (int i = 0; i < CombatConstants.StoppedRoundGraceTicks + 2; i++) Ballistics(sys, Dt);
            Assert.False(_world.IsAlive(bullet));
            Assert.True(_world.IsAlive(stone));
            Assert.True(RoundPickUp.IsLying(_world, stone));

            for (int i = 0; i < 59; i++) Ballistics(sys, 1f);
            Assert.True(_world.IsAlive(stone), "it lies its full minute");
            for (int i = 0; i < 2; i++) Ballistics(sys, 1f);
            Assert.False(_world.IsAlive(stone));
            _world.Bus.SwapBuffers();
            Assert.Empty(_world.Bus.Read<DetonationNotification>().ToArray());   // ⛔ a stone does not burst
        }

        /// <summary>⭐ PU-4/PU-5 — a man at a lying stone asks; the flier grants it and removes it; the grant fills his hands.
        /// ✅ Red-proof: the round left alive on a grant ⇒ red.</summary>
        [Fact]
        public void CE3235_AManAtALyingStone_AsksAndGetsIt_TheStoneIsGone_HisHandsHoldIt()
        {
            var man = Man(new Vector3(0, 0, 0));
            var stone = Lying(StoneAmmo, new Vector3(1, 0, 0), WarheadRound.Arc | WarheadRound.Lingers | WarheadRound.Landed, 50f);
            Assert.True(RoundPickUp.CanAsk(_world, man, stone));
            Assert.True(RoundPickUp.Ask(_world, man, stone));
            Frame();   // the flier grants and removes it
            Assert.False(_world.IsAlive(stone));
            Assert.Equal(1, _pickUp.Granted);
            Frame();   // the brain owner receives
            Assert.Equal(1, _pickUp.Received);
            Assert.Equal(1, PersonHands.Rounds(_world, man, StoneAmmo));
            Assert.False(_world.HasComponent<HeldFuze>(man));   // a stone is inert
        }

        /// <summary>
        /// ⭐ CE-1207 (docs/DESIGN_Entity_Control.md §8m PK-3, PK-4) — the OPERATOR's pick-up: the use rule offers "Pick up" on a lying stone in
        /// reach (too far ⇒ greyed), and <see cref="Fdp.Toolkit.Behavior.Executors.PickUpRoundExecutor"/> asks once, runs until the grant lands,
        /// then succeeds with the stone in his hands; a stone another man took first fails after its wait. ✅ Red-proof: Success on the ask
        /// (not on the hands) ⇒ the lost race succeeds.
        /// </summary>
        [Fact]
        public unsafe void CE1207_TheOperatorPicksUpALyingStone_ThroughTheUseRule_AndItsExecutor()
        {
            if (!_world.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.PersonCarrier>()) _world.RegisterComponent<Fdp.Toolkit.Behavior.Components.PersonCarrier>();
            var man = Man(new Vector3(0, 0, 0));
            _world.AddComponent(man, default(Fdp.Toolkit.Behavior.Components.PersonCarrier));
            var stone = Lying(StoneAmmo, new Vector3(1, 0, 0), WarheadRound.Arc | WarheadRound.Lingers | WarheadRound.Landed, 50f);
            var far = Lying(StoneAmmo, new Vector3(4, 0, 0), WarheadRound.Arc | WarheadRound.Lingers | WarheadRound.Landed, 50f);
            var offer = Fdp.Toolkit.Behavior.Possession.OperatorUseActions.For(_world, man, stone);
            Assert.Equal((Fdp.Toolkit.Behavior.Possession.UseVerbKind.PickUpRound, true, Fdp.Toolkit.Behavior.BehaviorConstants.ActionIdPickUpRound),
                         (offer[0].Verb, offer[0].Available, offer[0].ActionId));
            Assert.Equal(Fdp.Toolkit.Behavior.Possession.UseReason.OutOfReach, Fdp.Toolkit.Behavior.Possession.OperatorUseActions.For(_world, man, far)[0].Reason);

            var exec = new Fdp.Toolkit.Behavior.Executors.PickUpRoundExecutor();
            var channel = new Fdp.Toolkit.Behavior.Components.InteractionChannel();
            Fdp.Toolkit.Behavior.Possession.OperatorUseActions.WriteParams(offer[0], new Span<byte>(channel.Params, Fdp.Toolkit.Behavior.BehaviorConstants.ActionParamsByteSize));
            exec.OnEnter(man, ref channel, _world);
            exec.Execute(man, ref channel, _world, Dt);
            Assert.Equal(Fbt.NodeStatus.Running, channel.Status);   // asked, not yet held
            Frame(); Frame();
            exec.Execute(man, ref channel, _world, Dt);
            Assert.Equal(Fbt.NodeStatus.Success, channel.Status);
            Assert.Equal(1, PersonHands.Rounds(_world, man, StoneAmmo));

            // a stone someone else took first: the ask is made, no grant comes, the executor gives up
            var other = Man(new Vector3(2, 0, 0));
            var contested = Lying(StoneAmmo, new Vector3(1, 0.5f, 0), WarheadRound.Arc | WarheadRound.Lingers | WarheadRound.Landed, 50f);
            Assert.True(RoundPickUp.Ask(_world, other, contested));
            var lost = new Fdp.Toolkit.Behavior.Components.InteractionChannel();
            var take = Fdp.Toolkit.Behavior.Possession.OperatorUseActions.For(_world, man, contested)[0];
            Fdp.Toolkit.Behavior.Possession.OperatorUseActions.WriteParams(take, new Span<byte>(lost.Params, Fdp.Toolkit.Behavior.BehaviorConstants.ActionParamsByteSize));
            exec.OnEnter(man, ref lost, _world);
            exec.Execute(man, ref lost, _world, Dt);
            Frame(); Frame();
            for (int i = 0; i < 200 && lost.Status == Fbt.NodeStatus.Running; i++) exec.Execute(man, ref lost, _world, Dt);
            Assert.Equal(Fbt.NodeStatus.Failure, lost.Status);
            Assert.Equal(1, PersonHands.Rounds(_world, man, StoneAmmo));
        }

        /// <summary>⭐ PU-4 — TWO men ask for one stone in the same frame: ONE gets it. ✅ Red-proof: the round not removed on a grant ⇒ two
        /// grants ⇒ red.</summary>
        [Fact]
        public void CE3235_TwoMenReachForOneStone_OneGetsIt()
        {
            var a = Man(new Vector3(0, 0, 0));
            var b = Man(new Vector3(2, 0, 0));
            var stone = Lying(StoneAmmo, new Vector3(1, 0, 0), WarheadRound.Arc | WarheadRound.Lingers | WarheadRound.Landed, 50f);
            Assert.True(RoundPickUp.Ask(_world, a, stone));
            Assert.True(RoundPickUp.Ask(_world, b, stone));
            Frame();
            Frame();
            Assert.Equal(1, _pickUp.Granted);
            Assert.Equal(1, PersonHands.Rounds(_world, a, StoneAmmo) + PersonHands.Rounds(_world, b, StoneAmmo));
        }

        /// <summary>⭐ PU-5 — the flier's own check: out of reach, still flying, or not a hand item ⇒ no grant (an ask the asker should not
        /// have made is refused where the round is).</summary>
        [Fact]
        public void CE3235_OutOfReach_StillFlying_OrNotAHandItem_IsNotGranted()
        {
            var man = Man(new Vector3(0, 0, 0));
            var far = Lying(StoneAmmo, new Vector3(3, 0, 0), WarheadRound.Arc | WarheadRound.Lingers | WarheadRound.Landed, 50f);
            var flying = Lying(StoneAmmo, new Vector3(1, 0, 0), WarheadRound.Arc | WarheadRound.Lingers, 60f);
            var bullet = Lying(BulletAmmo, new Vector3(0, 1, 0), WarheadRound.Landed, 50f);
            Assert.False(RoundPickUp.CanAsk(_world, man, far));
            Assert.False(RoundPickUp.CanAsk(_world, man, flying));
            Assert.False(RoundPickUp.CanAsk(_world, man, bullet));
            foreach (var r in new[] { far, flying, bullet }) _world.Bus.Publish(new PickUpRoundEvent { Round = r, Man = man });   // asked anyway
            Frame();
            Frame();
            Assert.Equal(0, _pickUp.Granted);
            Assert.True(_world.IsAlive(far) && _world.IsAlive(flying) && _world.IsAlive(bullet));
        }

        /// <summary>⭐ PU-6 — a LIVE grenade picked up keeps its fuze in his hand; run out before he throws, it is thrown at his own feet with
        /// a near-zero fuze and leaves his hand. ✅ Red-proof: the grant's fuze dropped (0) ⇒ no HeldFuze ⇒ red.</summary>
        [Fact]
        public void CE3235_ALiveGrenadePickedUp_KeepsBurning_AndCooksOffAtHisFeet()
        {
            var man = Man(new Vector3(0, 0, 0));
            var grenade = Lying(GrenadeAmmo, new Vector3(1, 0, 0),
                WarheadRound.Area | WarheadRound.Arc | WarheadRound.TimeFuze | WarheadRound.Landed, 1.0f);
            Assert.True(RoundPickUp.Ask(_world, man, grenade));
            Frame();
            Frame();
            Assert.Equal(1, PersonHands.Rounds(_world, man, GrenadeAmmo));
            Assert.Equal(1.0f, RoundPickUp.FuzeLeft(_world, man, GrenadeAmmo), 3);

            var fuze = new HandsFuzeSystem();
            for (int i = 0; i < 70 && fuze.CookedOff == 0; i++) { _world.Bus.SwapBuffers(); fuze.Execute(_world, Dt); }
            Assert.Equal(1, fuze.CookedOff);
            Assert.False(_world.HasComponent<HeldFuze>(man));
            Assert.Equal(0, PersonHands.Rounds(_world, man, GrenadeAmmo));
            _world.Bus.SwapBuffers();
            var intent = Assert.Single(_world.Bus.Read<WeaponFireIntent>().ToArray());
            Assert.Equal((man, GrenadeAmmo, true), (intent.Shooter, intent.AmmoGuid, intent.AtPoint));
            Assert.Equal(PersonHands.MountIndex(_world, man), intent.WeaponIndex);
            Assert.Equal(HandsFuzeSystem.CookOffFuzeSeconds, intent.FuzeSeconds);
            Assert.True(Vector2.Distance(new Vector2(intent.TargetPoint.X, intent.TargetPoint.Y), Vector2.Zero) <= 1f, "at his feet");
        }

        /// <summary>⭐ PU-6 — thrown in time, the round leaves his hand with the fuze it had left (the intent carries it) and is no longer
        /// held. ✅ Red-proof: FuzeLeft returning 0 ⇒ red.</summary>
        [Fact]
        public void CE3235_AHeldLiveRound_IsThrownWithTheFuzeItHasLeft()
        {
            var man = Man(new Vector3(0, 0, 0));
            Assert.True(RoundPickUp.Receive(_world, new PickUpGrantEvent { Man = man, Ammo = GrenadeAmmo, Rounds = 1, FuzeSeconds = 2.25f }));
            Assert.Equal(2.25f, RoundPickUp.FuzeLeft(_world, man, GrenadeAmmo));
            Assert.Equal(0f, RoundPickUp.FuzeLeft(_world, man, StoneAmmo));   // another kind is not that round
            RoundPickUp.Thrown(_world, man, GrenadeAmmo);
            Assert.False(_world.HasComponent<HeldFuze>(man));
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3236</c> (B-14, §7.15 VC-5/VC-6) — a VENTING canister picked up takes its burn left into his hand (never as a fuze): it
        /// irritates him each second while it burns, and burnt out it is simply gone from his hands — no cook-off. ✅ Red-proofs: the grant's
        /// burn dropped ⇒ no burn in hand ⇒ red; the in-hand irritation removed ⇒ red; burnt out cooking off ⇒ red.
        /// </summary>
        [Fact]
        public void CE3236_AVentingCanister_PickedUp_BurnsInHisHand_IrritatesHim_AndGoesWhenOut()
        {
            var man = Man(new Vector3(0, 0, 0));
            var canister = Lying(CanisterAmmo, new Vector3(1, 0, 0),
                WarheadRound.Area | WarheadRound.Arc | WarheadRound.Vents | WarheadRound.Landed | WarheadRound.Lingers, 3.2f);
            Assert.True(RoundPickUp.Ask(_world, man, canister));
            Frame();
            Frame();
            Assert.False(_world.IsAlive(canister));
            Assert.Equal(1, PersonHands.Rounds(_world, man, CanisterAmmo));
            Assert.Equal(3.2f, RoundPickUp.BurnLeft(_world, man, CanisterAmmo), 3);
            Assert.Equal(0f, RoundPickUp.FuzeLeft(_world, man, CanisterAmmo));   // a burn, never a fuze

            var hands = new HandsFuzeSystem();
            for (int i = 0; i < 60 * 4 && _world.HasComponent<HeldFuze>(man); i++) { _world.Bus.SwapBuffers(); hands.Execute(_world, Dt); }
            Assert.Equal(3, hands.Irritated);                                   // one Irritant hit each whole second
            Assert.Equal((1L, 0L), (hands.BurntOut, hands.CookedOff));
            Assert.Equal(0, PersonHands.Rounds(_world, man, CanisterAmmo));     // empty: out of his hands
        }

        /// <summary>⭐ <c>CE-3236</c> (VC-7) — thrown in time, the canister leaves his hand and the intent carries its burn left.
        /// ✅ Red-proof: <c>BurnLeft</c> returning 0 ⇒ red.</summary>
        [Fact]
        public void CE3236_AHeldCanister_IsThrownWithTheBurnItHasLeft()
        {
            var man = Man(new Vector3(0, 0, 0));
            Assert.True(RoundPickUp.Receive(_world, new PickUpGrantEvent { Man = man, Ammo = CanisterAmmo, Rounds = 1, BurnSeconds = 41.5f }));
            Assert.Equal(41.5f, RoundPickUp.BurnLeft(_world, man, CanisterAmmo));
            RoundPickUp.Thrown(_world, man, CanisterAmmo);
            Assert.False(_world.HasComponent<HeldFuze>(man));
        }

        // ⭐ CE-3247 — a thrown hatchet as the built-in catalogue declares it (MunitionTkbCatalog.TkbThrowingHatchet): Kinetic, on the arc
        private static readonly WarheadDto Hatchet = new() { Kind = WarheadKind.Kinetic, Indirect = true };
        private static readonly HandItemDto HatchetLies = new() { LiesSeconds = 300f };

        /// <summary>
        /// ⭐⭐ <c>CE-3247</c> (civilians B-17, TC-1/TC-2, R-3016; docs/DESIGN_Civilians_And_Crowd_Control.md §7.17b) — a thrown HATCHET is a round
        /// on the stone's path: it flies the arc and lingers (no burst); a man on its line 8 m off is STRUCK — a direct hit carrying its damage,
        /// its munition and the thrower — and one thrown past him lies where it landed and is picked up again. ✅ Red-proofs: a Kinetic warhead
        /// made an Area round ⇒ no direct hit on his line ⇒ red; the hand item's lingering dropped ⇒ it is gone at the grace ⇒ red.
        /// </summary>
        [Fact]
        public void CE3247_AThrownHatchet_StrikesTheManOnItsLine_AndOneThatMisses_LiesAndIsPickedUpAgain()
        {
            byte bits = WarheadRound.BitsOf(Hatchet, HatchetLies, out float lies);
            Assert.True((bits & WarheadRound.Arc) != 0, "it flies the throw's arc");
            Assert.True((bits & WarheadRound.Lingers) != 0, "it lies where it lands");
            Assert.Equal(0, bits & WarheadRound.Area);   // a blade bursts nowhere: a direct hit only
            Assert.Equal(300f, lies);

            // ① the strike: thrown from (0,0) at a man 8 m off; the raycast of its flight meets him
            _world.RegisterEvent<RaycastResultEvent>();
            _world.RegisterEvent<HitEvent>();
            var thrower = Man(new Vector3(0, 0, 0));
            var victim = Man(new Vector3(8, 0, 0));
            var round = _world.CreateEntity();
            _world.AddComponent(round, new SimTransform { Position = new Vector3(4, 0, 1.6f), Rotation = Quaternion.Identity });
            _world.AddComponent(round, new SimVelocity { Linear = new Vector3(14f, 0f, 1f) });
            _world.AddComponent(round, new BallisticProjectile
            {
                PreviousPosition = new Vector3(0, 0, 1.5f), Muzzle = new Vector3(0, 0, 1.5f), SpawnTick = 1, TerrainFlags = 1, Ammo = HatchetAmmo,
                Warhead = bits, FuzeRemaining = lies, Damage = 35f, Penetration = 1.5f,
            });
            _world.Bus.Publish(new RaycastResultEvent { Hit = new RaycastHit
            {
                HasHit = 1, RayId = PhysicsConstants.PackBulletRayId(round), HitEntity = victim, IgnoreEntity = thrower,
                Start = new Vector3(4, 0, 1.6f), End = new Vector3(9, 0, 1.4f), T = 0.8f,
            } });
            _world.Bus.SwapBuffers();
            new Fdp.Toolkit.Physics.Systems.HitResolutionSystem().Execute(_world, Dt);
            _world.Bus.SwapBuffers();
            var hit = Assert.Single(_world.Bus.Read<DetonationNotification>().ToArray());
            Assert.Equal(victim, hit.Target);
            Assert.Equal(thrower, hit.Shooter);   // the source of the damage that follows (CE-3237)
            Assert.Equal(35f, hit.Damage);
            Assert.Equal(HatchetAmmo, hit.Ammo);

            // ② the miss: it stops on the ground past him, lies through the grace, and a man at it picks it up
            var sys = new BallisticsSystem();
            var missed = Lying(HatchetAmmo, new Vector3(12, 0, 0), bits, lies);
            for (int i = 0; i < CombatConstants.StoppedRoundGraceTicks + 2; i++) Ballistics(sys, Dt);
            Assert.True(_world.IsAlive(missed));
            Assert.True(RoundPickUp.IsLying(_world, missed));
            var picker = Man(new Vector3(11, 0, 0));
            Assert.True(RoundPickUp.Ask(_world, picker, missed));
            Frame();
            Frame();
            Assert.False(_world.IsAlive(missed));
            Assert.Equal(1, PersonHands.Rounds(_world, picker, HatchetAmmo));
        }
    }
}
