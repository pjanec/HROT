using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fbt;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Terrain;
using Hrot.AI.Behaviors.Brains;
using Hrot.CGF.Configuration;
using Hrot.Core;
using Hrot.Map.Common;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4693</c> (bridge laying BL-S3 ④ ⑤ ⑥; 📄 docs/designs/engineering/DESIGN_Bridge_Laying.md §12) — the engineer's brain (<see cref="BridgeLayingBehaviors"/>) and the
    /// mover (<see cref="BridgeWorkSystem"/>) on one world: a river (water y 60–72) across the map, the assault bridge layer (TKB 118, the real catalogue) south of it.
    /// The hull is faked as in <see cref="ClearObstacleBehaviorTests"/> (a MoveTo arrives the next frame); the creation request is played back as the ELM would — an
    /// ACTIVE deck (terrain's bake already committed).
    /// </summary>
    public sealed unsafe class BridgeLayingBehaviorTests : System.IDisposable
    {
        private const float Dt = 1f;
        private readonly EntityRepository _w = new();
        private readonly ITkbDatabase _tkb = HrotEnvironment.CreateTkb();
        private readonly BridgeWorkSystem _work = new();
        private readonly Entity _eng;
        private readonly List<Vector3> _moves = new();
        private readonly List<EntityCreationRequest> _asked = new();
        private uint _moveInst;
        private double _time;
        private long _frame;

        public BridgeLayingBehaviorTests() : this(12f) { }

        private BridgeLayingBehaviorTests(float riverWidth)
        {
            SimHostComponentRegistry.RegisterAll(_w);
            CognitiveComponentRegistry.RegisterAll(_w);
            foreach (var reg in new System.Action[]
            {
                () => { if (!_w.IsComponentTypeRegistered<StaticObstacle>()) _w.RegisterComponent<StaticObstacle>(); },
                () => { if (!_w.IsComponentTypeRegistered<ObstacleShape>()) _w.RegisterComponent<ObstacleShape>(); },
                () => { if (!_w.IsComponentTypeRegistered<BridgeCarried>()) _w.RegisterComponent<BridgeCarried>(); },
                () => { if (!_w.IsComponentTypeRegistered<TkbIdentity>()) _w.RegisterComponent<TkbIdentity>(); },
                () => { if (!_w.IsComponentTypeRegistered<NetworkIdentity>()) _w.RegisterComponent<NetworkIdentity>(); },
            }) reg();
            if (!_w.Bus.IsRegisteredManaged<EntityCreationRequest>()) _w.RegisterManagedEvent<EntityCreationRequest>();
            if (!_w.Bus.IsRegisteredManaged<DestroyEntityCommand>()) _w.RegisterManagedEvent<DestroyEntityCommand>();
            _w.SetSingletonManaged(_tkb);
            _w.SetSingletonManaged(River(riverWidth));
            _eng = _w.CreateEntity();
            _w.AddComponent(_eng, new SimTransform { Position = new Vector3(80f, 20f, 0f), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, System.MathF.PI / 2f) });
            _w.AddComponent(_eng, new TkbIdentity { TkbType = (TkbTypeId)TkbEntityTypes.Engineer_AssaultBridgeLayer });
            _w.AddComponent(_eng, new BehaviorState());
            _w.AddComponent(_eng, new LocomotionChannel());
            Assert.True(_tkb.TryGetByType(TkbEntityTypes.Engineer_AssaultBridgeLayer, out var engineer));
            new BridgeLayerTkbTranslator().Inject(_w, _eng, engineer);
        }

        public void Dispose() => _w.Dispose();

        /// <summary>basic-desert-like flat ground, a river y 60 → 60 + <paramref name="width"/> across the whole map.</summary>
        private static TerrainWorld River(float width)
        {
            string y1 = (60f + width).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return TerrainWorldParser.Parse("{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[-50,-50,300,300],\"groundZ\":0},\"features\":[" +
                "{\"type\":\"Feature\",\"properties\":{\"kind\":\"surface\",\"surface\":\"water\"},\"geometry\":{\"type\":\"Polygon\",\"coordinates\":[[[-50,60],[300,60],[300," + y1 + "],[-50," + y1 + "],[-50,60]]]}}]}", "river");
        }

        private BridgeCarried Carried => _w.GetComponentRO<BridgeCarried>(_eng);

        /// <summary>One frame: the brain's tick, the hull, the mover, then the ELM playing back a creation request as an ACTIVE deck.</summary>
        private NodeStatus Step(System.Func<NodeStatus> brain)
        {
            _w.SetSingleton(new GlobalTime { TotalTime = _time, DeltaTime = Dt, TimeScale = 1f, FrameNumber = ++_frame });
            _w.SetSimulationTime((float)_time);
            var status = brain();
            _w.FlushCommandBuffers();
            Body();
            _work.Execute(_w, Dt);
            ((EntityCommandBuffer)((ISimulationView)_w).GetCommandBuffer()).Playback(_w);
            _w.Bus.SwapBuffers();
            foreach (var r in ((ISimulationView)_w).ReadManagedEvents<EntityCreationRequest>()) { _asked.Add(r); Spawn(r); }
            _time += Dt;
            return status;
        }

        private void Spawn(EntityCreationRequest r)
        {
            var e = _w.CreateEntity();
            foreach (var c in r.InitialComponents)
            {
                if (c is SimTransform t) _w.AddComponent(e, t);
                else if (c is ObstacleShape s) _w.AddComponent(e, s);
            }
            _w.AddComponent(e, new StaticObstacle { Material = new FixedString32("steel-plate"), Kind = ObstacleKind.WalkableDeck });
            _w.AddComponent(e, new TkbIdentity { TkbType = r.TkbType });
            _w.AddComponent(e, new NetworkIdentity { Value = 4693 });
        }

        private void Body()
        {
            ref var loco = ref _w.GetComponentRW<LocomotionChannel>(_eng);
            if (loco.ActiveAction != NavigationConstants.ActionIdMoveTo || loco.Status != NodeStatus.Running) return;
            if (loco.ActionInstanceId != _moveInst)
            {
                _moveInst = loco.ActionInstanceId;
                fixed (byte* src = loco.Params) _moves.Add(((MoveToParams*)src)->Destination);
                return;
            }
            _w.GetComponentRW<SimTransform>(_eng).Position = _moves[^1];
            loco.Status = NodeStatus.Success;
        }

        private (BridgeLayingBehaviors.BridgeArgs Args, BridgeLayingBehaviors.BridgeWork Work) LayOrder(Vector3 near, Vector3 far)
        {
            var b = new BridgeLayingBehaviors.BridgeBlackboard();
            BridgeLayingBehaviors.ResolveAssaultLay(new Hrot.AI.Behaviors.Orders.LayBridgeOrder { NearBank = near, FarBank = far }, ref b, _w, _eng);
            return (b.Order, b.State);
        }

        /// <summary>
        /// ⭐⭐⭐ LAY: from 40 m south the engineer plans a deck centred on the 12 m river (the gap + a metre's bearing each bank, ≤ the 26 m span), drives through the approach
        /// to its stand on the line, sets ITS OWN order (Lay, the banks); the mover holds it for the Wolverine's 300 s, asks for ONE bridge (TKB 8825, the planned pose
        /// and length), and the brain — seeing the deck open there — counts the span off and succeeds. 🔴 Red-proof: the mover's seconds check removed (lays at once) ⇒
        /// the request before 300 s ⇒ red.
        /// </summary>
        [Fact]
        public void CE4693_TheEngineer_DrivesToItsStand_TheMoverLaysAfterBuildSeconds_TheBrainSeesItOpen()
        {
            var (a, ws) = LayOrder(new Vector3(80f, 50f, 0f), new Vector3(80f, 82f, 0f));
            Assert.Equal(NodeStatus.Running, Step(() => BridgeLayingBehaviors.Lay(ref a, ref ws, _eng, _w)));
            Assert.Equal(BridgeRefusal.NotRefused, Carried.Refusal);
            Assert.InRange(Carried.GapM, 11.5f, 12.6f);
            Assert.Equal(66f, ws.Centre.Y, 0);

            NodeStatus status = NodeStatus.Running;
            int laidAt = -1;
            for (int i = 0; i < 400 && status == NodeStatus.Running; i++)
            {
                status = Step(() => BridgeLayingBehaviors.Lay(ref a, ref ws, _eng, _w));
                if (laidAt < 0 && _asked.Count > 0) laidAt = i;
            }
            Assert.Equal(NodeStatus.Success, status);
            Assert.Equal(2, _moves.Count);                                    // approach, then the stand — aligned with the deck
            Assert.True(_moves[0].Y < _moves[1].Y && _moves[1].Y < 52f, "the hull stands south of the near ramp's foot");
            var r = Assert.Single(_asked);
            Assert.Equal(TkbEntityTypes.Obstacle_AssaultBridge, (long)r.TkbType);
            var shape = r.InitialComponents.OfType<ObstacleShape>().Single();
            Assert.InRange(shape.Length, 13.5f, 14.6f);                       // 12 m of water + 1 m on each bank (BL-20), not the whole 26 m
            var pose = r.InitialComponents.OfType<SimTransform>().Single();
            Assert.Equal(80f, pose.Position.X, 1);
            Assert.Equal(66f, pose.Position.Y, 0);
            Assert.Equal(System.MathF.PI / 2f, TerrainObstacles.Yaw(pose.Rotation), 2);
            Assert.InRange(laidAt, 300, 306);                                  // ≥ the Wolverine's 300 s at the stand
            Assert.Equal(0, Carried.Count);
            Assert.Equal(BridgeOrder.None, Carried.Order);
            Assert.Equal(1, _work.Laid);
        }

        /// <summary>⭐⭐ BL-11 — over a river wider than its 24 m gap the order is REFUSED at once, with its reason on the engineer's own component (machine-checkable) and
        /// nothing driven or laid. 🔴 Red-proof: the gap check removed ⇒ the play runs.</summary>
        [Fact]
        public void CE4693_AGapWiderThanTheSpan_IsRefusedWithItsReason_NothingLaid()
        {
            using var wide = new BridgeLayingBehaviorTests(30f);
            var (a, ws) = wide.LayOrder(new Vector3(80f, 50f, 0f), new Vector3(80f, 100f, 0f));
            Assert.Equal(NodeStatus.Failure, wide.Step(() => BridgeLayingBehaviors.Lay(ref a, ref ws, wide._eng, wide._w)));
            Assert.Equal(BridgeRefusal.GapTooWide, wide.Carried.Refusal);
            Assert.InRange(wide.Carried.GapM, 29.5f, 30.6f);
            Assert.Equal(1, wide.Carried.Count);
            Assert.Empty(wide._moves);
            for (int i = 0; i < 320; i++) wide.Step(() => NodeStatus.Running);
            Assert.Empty(wide._asked);
            Assert.StartsWith("the gap is 30.", BridgeLaying.Say(BridgeRefusal.GapTooWide, wide.Carried.GapM, 24f));
            Assert.Contains("wider than the 24.0 m", BridgeLaying.Say(BridgeRefusal.GapTooWide, wide.Carried.GapM, 24f));
        }

        /// <summary>⭐ BL-11 — a bank in the water, or two points with no water between them, is refused with its own reason.</summary>
        [Theory]
        [InlineData(80f, 64f, 80f, 90f, BridgeRefusal.NearBankInWater)]
        [InlineData(80f, 50f, 80f, 64f, BridgeRefusal.FarBankInWater)]
        [InlineData(80f, 10f, 80f, 40f, BridgeRefusal.NoWater)]
        public void CE4693_ABankInTheWater_OrNoWater_IsRefused(float nx, float ny, float fx, float fy, BridgeRefusal why)
        {
            var (a, ws) = LayOrder(new Vector3(nx, ny, 0f), new Vector3(fx, fy, 0f));
            Assert.Equal(NodeStatus.Failure, Step(() => BridgeLayingBehaviors.Lay(ref a, ref ws, _eng, _w)));
            Assert.Equal(why, Carried.Refusal);
        }

        /// <summary>
        /// ⭐⭐ RECOVER, then the count is back: a laid deck at (80, 66); the engineer drives to the NEARER end's stand, sets Recover with the bridge; the mover holds it the
        /// Wolverine's 600 s and ends the bridge through the ONE network destroy (never Health 0 — BL-18); the brain sees it gone and carries the span again. An order that
        /// names no bridge takes the nearest of the type it carries.
        /// </summary>
        [Fact]
        public void CE4693_Recover_DrivesToTheNearerEnd_TheMoverRemovesIt_TheSpanIsBackOnBoard()
        {
            _w.SetComponent(_eng, Carried with { Count = 0 });
            Spawn(new EntityCreationRequest
            {
                TkbType = (TkbTypeId)TkbEntityTypes.Obstacle_AssaultBridge,
                InitialComponents = new List<object>
                {
                    new SimTransform { Position = new Vector3(80f, 66f, 0f), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, System.MathF.PI / 2f) },
                    new ObstacleShape { Length = 14f, Width = 4f, Height = 1.2f },
                },
            });
            var bridge = Entity.Null;
            foreach (var e in _w.Query().With<StaticObstacle>().Build()) bridge = e;
            var b = new BridgeLayingBehaviors.BridgeBlackboard();
            BridgeLayingBehaviors.ResolveAssaultRecover(new Hrot.AI.Behaviors.Orders.RecoverBridgeOrder { Bridge = new Fdp.Toolkit.Replication.EntityLink(new Fdp.Toolkit.Replication.EntityRef(4693)) }, ref b, _w, _eng);
            Assert.Equal(bridge, b.Order.Bridge);
            var unnamed = new BridgeLayingBehaviors.BridgeBlackboard();   // ⭐ no bridge named (one laid during the run has no authored name): the nearest of its type
            BridgeLayingBehaviors.ResolveAssaultRecover(new Hrot.AI.Behaviors.Orders.RecoverBridgeOrder(), ref unnamed, _w, _eng);
            Assert.Equal(bridge, unnamed.Order.Bridge);
            var (a, ws) = (b.Order, b.State);
            NodeStatus status = NodeStatus.Running;
            int i = 0;
            for (; i < 700 && status == NodeStatus.Running; i++) status = Step(() => BridgeLayingBehaviors.Recover(ref a, ref ws, _eng, _w));
            Assert.Equal(NodeStatus.Success, status);
            Assert.True(_moves[^1].Y < 52f, "the south end: the nearer one");
            Assert.InRange(i, 600, 610);
            Assert.False(_w.IsAlive(bridge) && _w.GetLifecycleState(bridge) != EntityLifecycle.TearDown);
            Assert.Equal(1, Carried.Count);
            Assert.Equal(BridgeOrder.None, Carried.Order);
            Assert.Equal(1, _work.Recovered);
        }

        /// <summary>
        /// ⭐⭐ A bridge DEMOLISHED while the engineer takes it back is LOST, never counted back on board (📌 bl-river-crossing: a recovery ordered as the
        /// sapper's charge dropped the span counted it back). 🔴 Red-proof: count the span back whenever the bridge goes ⇒ Count 1 ⇒ red.
        /// </summary>
        [Fact]
        public void CE4693_ABridgeDemolishedWhileItIsRecovered_IsLost_NotCountedBack()
        {
            if (!_w.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>()) _w.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            _w.SetComponent(_eng, Carried with { Count = 0 });
            Spawn(new EntityCreationRequest
            {
                TkbType = (TkbTypeId)TkbEntityTypes.Obstacle_AssaultBridge,
                InitialComponents = new List<object>
                {
                    new SimTransform { Position = new Vector3(80f, 66f, 0f), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, System.MathF.PI / 2f) },
                    new ObstacleShape { Length = 14f, Width = 4f, Height = 1.2f },
                },
            });
            var bridge = Entity.Null;
            foreach (var e in _w.Query().With<StaticObstacle>().Build()) bridge = e;
            _w.AddComponent(bridge, new Fdp.Toolkit.Combat.Components.Health { Current = 600f, Max = 600f });
            var b = new BridgeLayingBehaviors.BridgeBlackboard();
            BridgeLayingBehaviors.ResolveAssaultRecover(new Hrot.AI.Behaviors.Orders.RecoverBridgeOrder(), ref b, _w, _eng);
            var (a, ws) = (b.Order, b.State);
            for (int i = 0; i < 20 && ws.Step != BridgeLayingBehaviors.Step.Work; i++) Step(() => BridgeLayingBehaviors.Recover(ref a, ref ws, _eng, _w));
            Assert.Equal(BridgeLayingBehaviors.Step.Work, ws.Step);
            Assert.Equal(BridgeOrder.Recover, Carried.Order);

            // the charge: Health 0, then the obstacle's owner removes it (ObstacleRemovalSystem) a frame later
            _w.SetComponent(bridge, new Fdp.Toolkit.Combat.Components.Health { Current = 0f, Max = 600f });
            Assert.Equal(NodeStatus.Running, Step(() => BridgeLayingBehaviors.Recover(ref a, ref ws, _eng, _w)));
            _w.DestroyEntity(bridge);
            Assert.Equal(NodeStatus.Failure, Step(() => BridgeLayingBehaviors.Recover(ref a, ref ws, _eng, _w)));
            Assert.Equal(0, Carried.Count);
            Assert.Equal(BridgeOrder.None, Carried.Order);
        }
    }
}
