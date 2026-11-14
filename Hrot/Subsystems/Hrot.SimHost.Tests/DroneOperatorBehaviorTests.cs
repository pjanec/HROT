using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Combat.Components;
using Hrot.AI.Behaviors.Brains;
using Hrot.CGF.Configuration;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-7026</c> (City Battle §11 R-1a) — <see cref="DroneOperatorBehavior.Operate"/>: one drone of the stock in the air at a time, each
    /// LAUNCHED by an order (<c>AirPlatformStrike</c> on the area); the next only once the last is gone and the interval has passed; a stock drone
    /// lost before it flew is skipped; the stock spent ⇒ Success.
    /// </summary>
    public sealed class DroneOperatorBehaviorTests : System.IDisposable
    {
        private readonly EntityRepository _w = new();
        private readonly Entity _op;
        private readonly Entity[] _drones = new Entity[3];
        private readonly List<AssignTacticalIntentEvent> _orders = new();
        private double _time;
        private long _frame;

        public DroneOperatorBehaviorTests()
        {
            SimHostComponentRegistry.RegisterAll(_w);
            CognitiveComponentRegistry.RegisterAll(_w);
            BrainPhase.Prepare(_w);   // the brains' types (SubordinateOrders …) as the phase registers them
            if (!_w.Bus.IsRegisteredManaged<AssignTacticalIntentEvent>()) _w.Bus.RegisterManaged<AssignTacticalIntentEvent>();
            _op = _w.CreateEntity();
            _w.AddComponent(_op, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            _w.AddComponent(_op, new BehaviorState());
            for (int i = 0; i < _drones.Length; i++)
            {
                _drones[i] = _w.CreateEntity();
                _w.AddComponent(_drones[i], new SimTransform { Position = new Vector3(2f + i, 0f, 0f), Rotation = Quaternion.Identity });
                _w.AddComponent(_drones[i], new Health { Current = 10f, Max = 10f });
            }
        }

        public void Dispose() => _w.Dispose();

        private NodeStatus Step(ref DroneOperatorParams p, double dt = 0.5)
        {
            _w.SetSingleton(new GlobalTime { TotalTime = _time, DeltaTime = (float)dt, TimeScale = 1f, FrameNumber = ++_frame });
            var s = DroneOperatorBehavior.Operate(ref p, _op, _w);
            _w.Bus.SwapBuffers();
            _orders.AddRange(_w.Bus.ReadManaged<AssignTacticalIntentEvent>());
            _time += dt;
            return s;
        }

        private int Launched(Entity drone) => _orders.Count(o => o.Entity == drone && o.IntentId == BehaviorNames.AirPlatformStrike);

        /// <summary>
        /// ⭐⭐ <c>CE7026_R1</c> — ONE AT A TIME: the first drone is ordered to strike the area at once; while it flies nothing else is launched; it is
        /// shot down ⇒ the next after the interval (not before); the third was lost on the ground ⇒ skipped; the last gone ⇒ Success.
        /// ✅ Red-proof: the in-the-air wait dropped ⇒ the second launches while the first still flies ⇒ red.
        /// </summary>
        [Fact]
        public void CE7026_TheOperator_LaunchesOneDroneAtATime_TheNextOnlyOnceTheLastIsGone()
        {
            var area = new Vector3(300f, 200f, 0f);
            var p = new DroneOperatorParams
            {
                Drone0 = _drones[0], Drone1 = _drones[1], Drone2 = _drones[2], Drone3 = Entity.Null,
                Point = area, IntervalSeconds = 10f, Current = DroneOperatorBehavior.None,
            };
            Assert.Equal(NodeStatus.Running, Step(ref p));
            Assert.Equal(1, Launched(_drones[0]));
            var json = _orders.Single(o => o.Entity == _drones[0]).JsonParams;
            var strike = System.Text.Json.JsonSerializer.Deserialize<Hrot.AI.Behaviors.Orders.StrikeOrder>(json, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
            Assert.Equal(area.X, ((Vector3)strike.Location).X, 1);
            Assert.Equal(area.Y, ((Vector3)strike.Location).Y, 1);

            for (int i = 0; i < 40; i++) Assert.Equal(NodeStatus.Running, Step(ref p));     // 20 s in the air: no second launch
            Assert.Equal(0, Launched(_drones[1]));

            _w.GetComponentRW<Health>(_drones[0]).Current = 0f;                               // shot down
            _w.DestroyEntity(_drones[2]);                                                     // the third lost on the ground
            for (int i = 0; i < 19; i++) Step(ref p);                                         // 9.5 s: the interval not passed
            Assert.Equal(0, Launched(_drones[1]));
            Step(ref p); Step(ref p);
            Assert.Equal(1, Launched(_drones[1]));

            _w.GetComponentRW<Health>(_drones[1]).Current = 0f;
            NodeStatus last = NodeStatus.Running;
            for (int i = 0; i < 30 && last == NodeStatus.Running; i++) last = Step(ref p);
            Assert.Equal(NodeStatus.Success, last);
            Assert.Equal(2, _orders.Count(o => o.IntentId == BehaviorNames.AirPlatformStrike));   // the lost one never ordered
        }
    
        /// <summary>⭐ <c>CE-7027</c> — a drone that ENDS its order alive (the bomber back from its drop) is done; one whose known type has rounds
        /// left for another sortie is sent AGAIN — the bomber with two grenades flies two sorties, then the next drone goes. Until the order
        /// reached the drone (never seen running it) nothing is decided. ✅ Red-proof: the re-send dropped ⇒ the bomber flies once ⇒ red.</summary>
        [Fact]
        public void CE7027_ABomberBackFromItsDrop_IsSentAgainWhileItHasRoundsLeft()
        {
            const long Bomber = 9503;
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var bomber = new Fdp.Interfaces.TkbTemplate("bomber", Bomber);
            bomber.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto
                { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Bomb, InitialAmmunition = 2 } } });
            db.Register(bomber);
            _w.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            if (!_w.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>()) _w.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            _w.AddComponent(_drones[0], new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)Bomber });
            _w.GetComponentRW<BehaviorState>(_op).InstanceId = 7;   // the operator's run: the one his orders are recorded under
            var p = new DroneOperatorParams
            {
                Drone0 = _drones[0], Drone1 = _drones[1], Drone2 = Entity.Null, Drone3 = Entity.Null,
                Point = new Vector3(300f, 200f, 0f), IntervalSeconds = 2f, Current = DroneOperatorBehavior.None,
            };
            void Runs(bool running)
            {
                var bs = new BehaviorState { Origin = BehaviorOrigin.Superior, ActiveBehaviorHash = running ? BehaviorHash.FromName(BehaviorNames.AirPlatformStrike) : BehaviorIds.None };
                if (_w.HasComponent<BehaviorState>(_drones[0])) _w.SetComponent(_drones[0], bs); else _w.AddComponent(_drones[0], bs);
            }

            Step(ref p);
            Assert.Equal(1, Launched(_drones[0]));
            for (int i = 0; i < 10; i++) Step(ref p);                       // the order not yet running there: nothing decided
            Assert.Equal((1, 0), (Launched(_drones[0]), Launched(_drones[1])));
            Runs(true); for (int i = 0; i < 4; i++) Step(ref p);          // the sortie
            Runs(false); for (int i = 0; i < 6; i++) Step(ref p);         // back home, its order ended: after the interval, again
            Assert.Equal((2, 0), (Launched(_drones[0]), Launched(_drones[1])));
            Runs(true); for (int i = 0; i < 4; i++) Step(ref p);
            Runs(false); for (int i = 0; i < 6; i++) Step(ref p);         // its second grenade gone: the next drone
            Assert.Equal((2, 1), (Launched(_drones[0]), Launched(_drones[1])));
        }

        /// <summary>⭐ <c>CE-7028</c> — an UNARMED stock drone (the recon quadcopter: no mount) is sent to WATCH the area (<c>DroneRecon</c>), an armed one
        /// to strike it. ✅ Red-proof: the mount test dropped ⇒ the recon drone gets a strike ⇒ red.</summary>
        [Fact]
        public void CE7028_AnUnarmedDrone_IsSentToWatch_AnArmedOneToStrike()
        {
            const long Recon = 9501, Fpv = 9502;
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            db.Register(new Fdp.Interfaces.TkbTemplate("recon", Recon));
            var fpv = new Fdp.Interfaces.TkbTemplate("fpv", Fpv);
            fpv.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { InitialAmmunition = 1 } } });
            db.Register(fpv);
            _w.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            if (!_w.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>()) _w.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            _w.AddComponent(_drones[0], new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)Recon });
            _w.AddComponent(_drones[1], new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)Fpv });
            var p = new DroneOperatorParams
            {
                Drone0 = _drones[0], Drone1 = _drones[1], Drone2 = Entity.Null, Drone3 = Entity.Null,
                Point = new Vector3(300f, 200f, 0f), IntervalSeconds = 1f, Current = DroneOperatorBehavior.None,
            };
            Step(ref p);
            Assert.Equal(BehaviorNames.DroneRecon, Assert.Single(_orders, o => o.Entity == _drones[0]).IntentId);
            _w.GetComponentRW<Health>(_drones[0]).Current = 0f;
            for (int i = 0; i < 4; i++) Step(ref p);
            Assert.Equal(BehaviorNames.AirPlatformStrike, Assert.Single(_orders, o => o.Entity == _drones[1]).IntentId);
        }
    }
}
