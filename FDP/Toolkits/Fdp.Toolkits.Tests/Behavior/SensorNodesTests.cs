using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Tests.Utility;
using Xunit;

namespace Fdp.Toolkit.Tests.Behavior
{
    /// <summary>⭐ <c>CE-3054</c> D — the shared sensor / threat-memory nodes (docs/DESIGN_Sensors_And_Doctrine.md §7.8a).</summary>
    public sealed class SensorNodesTests : System.IDisposable
    {
        private readonly UtilityTestWorld _w = new();

        public void Dispose() => _w.Dispose();

        private Entity Sensor(Entity unit, SensorModality kind, float topScore, int count, int part = 1000)
        {
            if (!_w.Repo.IsComponentTypeRegistered<SensorTag>()) _w.Repo.RegisterComponent<SensorTag>();
            var child = _w.SpawnEqsSensor(unit, 1u, topScore, count, part);
            _w.Repo.AddComponent(child, new SensorTag { Kind = kind });
            return child;
        }

        [Fact]
        public void CE3054_Sees_ReadsTheUnitsSensorOfTheKind_AndItsThresholds()
        {
            var unit = _w.SpawnAgent(1f, 1f);
            Sensor(unit, SensorModality.Visual, topScore: 0.8f, count: 3);
            Sensor(unit, SensorModality.Thermal, topScore: 0.2f, count: 1, part: 1001);

            var p = new SensorReadParams { Kind = SensorModality.Visual, MinCount = 2, MinTopScore = 0.5f };
            Assert.True(SensorNodes.Sees(ref p, unit, _w.Repo));
            p.MinCount = 4;
            Assert.False(SensorNodes.Sees(ref p, unit, _w.Repo));

            var t = new SensorReadParams { Kind = SensorModality.Thermal, MinTopScore = 0.5f };
            Assert.False(SensorNodes.Sees(ref t, unit, _w.Repo));   // the thermal top is too weak
            var a = new SensorReadParams { Kind = SensorModality.Acoustic };
            Assert.False(SensorNodes.Sees(ref a, unit, _w.Repo));   // no acoustic sensor
        }

        [Fact]
        public void CE3054_Read_WritesTheCountTopAndStamp_AndClearsWithoutAnAnswer()
        {
            var unit = _w.SpawnAgent(1f, 1f);
            Sensor(unit, SensorModality.Visual, topScore: 0.9f, count: 2);

            var p = new SensorReadParams { Kind = SensorModality.Visual };
            var ws = default(SensorReading);
            Assert.Equal(NodeStatus.Running, SensorNodes.Read(ref p, ref ws, unit, _w.Repo));
            Assert.Equal(2, ws.Count);
            Assert.Equal(0.9f, ws.TopScore, precision: 4);
            Assert.Equal(1u, ws.AnswerTick);

            var r = new SensorReadParams { Kind = SensorModality.Radar };
            SensorNodes.Read(ref r, ref ws, unit, _w.Repo);
            Assert.Equal(default, ws);
        }

        [Fact]
        public void CE3054_ThreatsAtLeast_CountsByDanger_AndLiveOnly()
        {
            var unit = _w.SpawnAgent(1f, 1f);
            var armed = _w.Repo.CreateEntity();
            _w.Repo.AddComponent(armed, new WeaponState { Ammo = 30, MaxAmmo = 30 });
            var unarmed = _w.Repo.CreateEntity();
            _w.SeedContact(unit, armed, 50f, 1f, -1f, true);
            _w.SeedContact(unit, unarmed, 60f, 0.1f, -1f, true);   // faded

            var two = new ThreatCountParams { Count = 2 };
            Assert.True(SensorNodes.ThreatsAtLeast(ref two, unit, _w.Repo));
            var twoArmed = new ThreatCountParams { Count = 2, MinDanger = 1f };
            Assert.False(SensorNodes.ThreatsAtLeast(ref twoArmed, unit, _w.Repo));
            var oneArmed = new ThreatCountParams { Count = 1, MinDanger = 1f };
            Assert.True(SensorNodes.ThreatsAtLeast(ref oneArmed, unit, _w.Repo));
            var twoLive = new ThreatCountParams { Count = 2, LiveOnly = true };
            Assert.False(SensorNodes.ThreatsAtLeast(ref twoLive, unit, _w.Repo));   // the faded one is not live
        }

        /// <summary>⭐ <c>CE-3063</c> ② — a HEARD contact counts by its sound class's danger (K3): a heard tank is a dangerous
        /// threat, heard footsteps are not.</summary>
        [Fact]
        public void CE3063_ThreatsAtLeast_CountsAHeardContactByItsClass()
        {
            var unit = _w.SpawnAgent(1f, 1f);
            _w.SeedHeard(unit, 100f, 0f, 20f, (byte)Fdp.Toolkit.Tkb.Domain.SoundSourceClass.Footsteps);
            var dangerous = new ThreatCountParams { Count = 1, MinDanger = 0.9f };
            Assert.False(SensorNodes.ThreatsAtLeast(ref dangerous, unit, _w.Repo), "footsteps read 0.6");
            _w.SeedHeard(unit, -300f, 0f, 20f, (byte)Fdp.Toolkit.Tkb.Domain.SoundSourceClass.TrackedEngine);
            Assert.True(SensorNodes.ThreatsAtLeast(ref dangerous, unit, _w.Repo), "a heard tracked engine reads 1");
        }
    }
}
