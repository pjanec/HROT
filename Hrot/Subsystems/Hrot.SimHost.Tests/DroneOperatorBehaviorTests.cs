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
    }
}
