using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Perception.LineOfSight;
using Fdp.Toolkit.Perception.Sensors;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkit.Perception.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-3260</c> (BR-7, DA-3, R-2035; docs/DESIGN_Sensors_And_Doctrine.md §5.7) — ACQUISITION TIME BY EXPOSURE: a seen man becomes
    /// a contact after a dwell that grows with range and with how few of his body points show, and shrinks with the observer's proficiency;
    /// 0 for a fully exposed man within 100 m.
    /// </summary>
    public sealed class AcquisitionTests
    {
        /// <summary>AQ-2 — the delays the frame names: 0 fully exposed within 100 m; one of three points at 300 m later than fully exposed
        /// there; an Elite observer quicker, an Untrained one slower.</summary>
        [Fact]
        public void CE3260_TheDelay_GrowsWithRangeAndHiddenPoints_ShrinksWithProficiency()
        {
            Assert.Equal(0f, Acquisition.DelaySeconds(50f, 1f, 1f));
            Assert.Equal(0f, Acquisition.DelaySeconds(100f, 1f, 1f));
            float full300 = Acquisition.DelaySeconds(300f, 1f, 1f), third300 = Acquisition.DelaySeconds(300f, 1f / 3f, 1f);
            Assert.Equal(1f, full300, 3);
            Assert.Equal(2f * (0.5f + 2f / 3f), third300, 3);
            Assert.True(third300 > full300);
            Assert.True(Acquisition.DelaySeconds(300f, 1f / 3f, 0.7f) < third300);
            Assert.True(Acquisition.DelaySeconds(300f, 1f / 3f, 1.8f) > third300);
        }

        /// <summary>AQ-3 — the exposure rides the result's flags; a result with none (another sensor's) carries none and waits for nothing.</summary>
        [Fact]
        public void CE3260_TheExposure_RidesTheResult_AndOnlyASightTestedResultWaits()
        {
            var r = new EqsResult { EntityId = 7 };
            Assert.False(Acquisition.Carries(in r));
            Assert.Equal(0f, Acquisition.DwellOf(in r, 400f, 1f));   // a radar at 400 m: acquired the tick it is seen, as before
            Acquisition.Encode(ref r, 1, 3);
            Assert.True(Acquisition.Carries(in r));
            Assert.Equal(1f / 3f, Acquisition.ExposureOf(in r), 4);
            Assert.True(Acquisition.DwellOf(in r, 400f, 1f) > 0f);
        }

        /// <summary>
        /// AQ-4 — a 1.2 m wall a metre in front of a standing man hides his low and middle points: the sight test sees ONE of his three.
        /// The visual sensor's sight test writes that 1 of 3 into the result. ✅ Red-proofs: <c>VisiblePoints</c> stopping at the first clear
        /// point ⇒ the open case reads 1 of 3 ⇒ red; the sight test not encoding ⇒ the exposure reads 1 ⇒ red.
        /// </summary>
        [Fact]
        public void CE3260_AWallBeforeHim_ShowsOneOfHisThreePoints_TheOpenShowsAll()
        {
            var world = PerceptionTestWorldFactory.Create();
            world.RegisterComponent<PhysicsCollider>();
            var obs = world.CreateEntity();
            world.AddComponent(obs, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            var man = world.CreateEntity();
            world.AddComponent(man, new SimTransform { Position = new Vector3(10f, 0f, 0f), Rotation = Quaternion.Identity });
            var los = new TerrainWorldLosStrategy(() => (TerrainWorld?)null,
                (v, e) => v.HasComponent<PhysicsCollider>(e) ? v.GetComponentRO<PhysicsCollider>(e).Radius : 0f,
                (v, e) => v.HasComponent<PhysicsCollider>(e) ? 1.2f : 0f);
            ISimulationView view = world;

            los.BeginBatch(view);
            Assert.Equal(3, ((ILosStrategy)los).VisiblePoints(view, obs, man, out int open));
            Assert.Equal(3, open);

            var wall = world.CreateEntity();
            world.AddComponent(wall, new SimTransform { Position = new Vector3(9f, 0f, 0f), Rotation = Quaternion.Identity });
            world.AddComponent(wall, new PhysicsCollider { Radius = 0.5f });
            los.BeginBatch(view);
            Assert.Equal(1, ((ILosStrategy)los).VisiblePoints(view, obs, man, out int total));
            Assert.Equal(3, total);
            Assert.True(los.IsVisible(view, obs, man));   // seen — his head shows

            // the visual sensor's sight test writes it into the result the memory stage reads
            var candidates = new[] { new EqsResult { EntityId = (long)man.PackedValue } };
            var sensor = new EqsSensor();
            new StrategySightTest(los).ExecuteBatch(obs, ref sensor, view, candidates);
            Assert.Equal((long)man.PackedValue, candidates[0].EntityId);
            Assert.Equal(1f / 3f, Acquisition.ExposureOf(in candidates[0]), 4);
            world.Dispose();
        }

        /// <summary>
        /// AQ-1 / AQ-5 — through the memory stage, at 10 Hz: a fully exposed man at 50 m is acquired the tick he is seen (as before); a man
        /// showing one of three points at 300 m is acquired only after ≈ 2.3 s of being seen; hidden longer than the lost threshold, he starts
        /// over. ✅ Red-proof: the dwell check removed from <c>ContactHysteresis</c> ⇒ the 300 m man is acquired at once ⇒ red.
        /// </summary>
        [Fact]
        public void CE3260_TheMemoryStage_AcquiresTheExposedManAtOnce_TheHalfHiddenOneAfterHisDwell_AndAHideResets()
        {
            var w = new EntityRepository();
            w.RegisterComponent<SimTransform>();
            w.RegisterComponent<SensorTag>();
            w.RegisterComponent<PartMetadata>();
            w.RegisterComponent<SensorContactList>();
            w.RegisterComponent<EqsSensor>();
            w.RegisterComponent<NetworkIdentity>();
            w.RegisterEvent<SensorTrackStateEvent>();
            var unit = w.CreateEntity();
            w.AddComponent(unit, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            var sensor = w.CreateEntity();
            w.AddComponent(sensor, new SensorTag { Kind = SensorModality.Visual });
            w.AddComponent(sensor, new PartMetadata { ParentEntity = unit });
            w.AddComponent(sensor, new EqsSensor());
            Entity Man(float x) { var e = w.CreateEntity(); w.AddComponent(e, new SimTransform { Position = new Vector3(x, 0f, 0f), Rotation = Quaternion.Identity }); return e; }
            var near = Man(50f);
            var far = Man(300f);
            var stage = new SensorMemoryStage();
            uint tick = 0;
            double t = 0;

            SensorContactState StateOf(Entity target)
            {
                var list = w.GetComponentRO<SensorContactList>(sensor);
                unsafe { for (int i = 0; i < list.Count; i++) if (list.EntityIds[i] == (long)target.PackedValue) return (SensorContactState)list.State[i]; }
                return SensorContactState.Pending;
            }
            void See(params (Entity Who, int Clear)[] seen)
            {
                tick++; t += 0.1;
                w.SetSingleton(new GlobalTime { TotalTime = t, DeltaTime = 0.1f });
                var results = new EqsResult[seen.Length];
                for (int i = 0; i < seen.Length; i++)
                {
                    results[i] = new EqsResult { EntityId = (long)seen[i].Who.PackedValue };
                    Acquisition.Encode(ref results[i], seen[i].Clear, 3);
                }
                stage.Begin(w);
                stage.Observe(w, sensor, tick, results);
                using var cmd = new EntityCommandBuffer();
                stage.Flush(w, cmd);
                cmd.Playback(w);
            }

            See((near, 3), (far, 1));
            Assert.Equal(SensorContactState.Acquired, StateOf(near));   // fully exposed within 100 m: the same tick, as before
            Assert.NotEqual(SensorContactState.Acquired, StateOf(far));
            for (int i = 0; i < 21; i++) See((near, 3), (far, 1));       // 2.2 s seen
            Assert.NotEqual(SensorContactState.Acquired, StateOf(far));
            for (int i = 0; i < 3; i++) See((near, 3), (far, 1));        // 2.5 s ≥ 2.33 s
            Assert.Equal(SensorContactState.Acquired, StateOf(far));

            // he hides longer than the lost threshold, then shows again: lost, and the dwell starts over
            for (int i = 0; i <= (int)Fdp.Toolkit.Perception.Systems.ContactHysteresis.TrackLostThresholdTicks + 1; i++) See((near, 3));
            Assert.Equal(SensorContactState.Lost, StateOf(far));
            See((near, 3), (far, 1));
            Assert.NotEqual(SensorContactState.Acquired, StateOf(far));
            w.Dispose();
        }
    }
}
