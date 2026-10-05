using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Perception.Systems;
using Xunit;

namespace Fdp.Toolkit.Perception.Tests
{
    /// <summary>
    /// ⭐ <c>CE-3039</c> — the edges of what a unit senses (<see cref="SensorChangedEvent"/>), each published by the system that
    /// already holds the fact (docs/DESIGN_Sensors_And_Doctrine.md §7.3). An edge fires ONCE per change, never per frame.
    /// </summary>
    public class SensorChangedEventTests
    {
        private static List<SensorChangedEvent> Run(EntityRepository world, IEcsModuleSystem system, float dt = 0.1f)
        {
            var view = (ISimulationView)world;
            system.Execute(view, dt);
            ((EntityCommandBuffer)view.GetCommandBuffer()).Playback(world);
            world.Bus.SwapBuffers();
            return view.ReadEvents<SensorChangedEvent>().ToArray().ToList();
        }

        private static EntityRepository World()
        {
            var w = PerceptionTestWorldFactory.Create();
            if (!w.IsComponentTypeRegistered<Health>()) w.RegisterComponent<Health>();
            return w;
        }

        [Fact]
        public void TheTrackSet_PublishesAcquiredOnce_AndLostOnce()
        {
            var w = World();
            var unit = w.CreateEntity();
            var target = w.CreateEntity();
            var sys = new ActiveSensorTracksUpdateSystem();

            w.Bus.Publish(new SensorTrackStateEvent { Observer = unit, Target = target, State = SensorTrackStatus.Acquired });
            w.Bus.SwapBuffers();
            var e1 = Run(w, sys);
            var acquired = Assert.Single(e1);
            Assert.Equal(SensorChange.Acquired, acquired.What);
            Assert.Equal(unit, acquired.Unit);
            Assert.Equal(target, acquired.Target);

            // A repeated Acquired for a track already held is a position update — no edge.
            w.Bus.Publish(new SensorTrackStateEvent { Observer = unit, Target = target, State = SensorTrackStatus.Acquired, PositionX = 5f });
            w.Bus.SwapBuffers();
            Assert.Empty(Run(w, sys));

            w.Bus.Publish(new SensorTrackStateEvent { Observer = unit, Target = target, State = SensorTrackStatus.Lost });
            w.Bus.SwapBuffers();
            Assert.Equal(SensorChange.Lost, Assert.Single(Run(w, sys)).What);

            // Lost for a track not held — no edge.
            w.Bus.Publish(new SensorTrackStateEvent { Observer = unit, Target = target, State = SensorTrackStatus.Lost });
            w.Bus.SwapBuffers();
            Assert.Empty(Run(w, sys));
        }

        /// <summary>⭐ CE-3060 — the track keeps the kinds the event carries, and the memory takes them (not an assumed Visual).</summary>
        [Fact]
        public unsafe void TheTrackKeepsItsKinds_AndTheMemoryTakesThem_CE3060()
        {
            var w = World();
            var unit = w.CreateEntity();
            var target = w.CreateEntity();
            w.AddComponent(unit, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            w.AddComponent(unit, new TargetMemory());

            w.Bus.Publish(new SensorTrackStateEvent { Observer = unit, Target = target, State = SensorTrackStatus.Acquired, Modality = SensorModality.Thermal });
            w.Bus.SwapBuffers();
            Run(w, new ActiveSensorTracksUpdateSystem());
            Assert.Equal((byte)SensorModality.Thermal, w.GetComponentRO<ActiveSensorTracks>(unit).Modalities[0]);

            Run(w, new ThreatEvaluationSystem());
            var mem = w.GetComponentRO<TargetMemory>(unit);
            Assert.Equal(1, mem.Count);
            Assert.Equal((byte)SensorModality.Thermal, mem.Modalities[0]);   // ⇒ HasLineOfSight reads 0 for it
        }

        /// <summary>⭐ CE-3064 — a near miss (local or from the wire) becomes the unit's SensorChange.NearMiss, once per event.</summary>
        [Fact]
        public void ANearMiss_IsTheUnitsNearMissEdge_CE3064()
        {
            var w = World();
            if (!w.Bus.IsRegistered<Fdp.Toolkit.Combat.Events.NearMissEvent>()) w.RegisterEvent<Fdp.Toolkit.Combat.Events.NearMissEvent>();
            var unit = w.CreateEntity();
            w.Bus.Publish(new Fdp.Toolkit.Combat.Events.NearMissEvent { Unit = unit, X = 1f, Y = 2f, IsRemote = true });
            w.Bus.SwapBuffers();
            var e = Assert.Single(Run(w, new NearMissSensingSystem()));
            Assert.Equal(SensorChange.NearMiss, e.What);
            Assert.Equal(unit, e.Unit);
        }

        [Fact]
        public unsafe void TheMemory_PublishesFirstThreat_ThenAllClear_WhenItForgets()
        {
            var w = World();
            var unit = w.CreateEntity();
            var target = w.CreateEntity();
            w.AddComponent(unit, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            w.AddComponent(unit, new TargetMemory());
            var tracks = new ActiveSensorTracks();
            tracks.EntityIds[0] = (long)target.PackedValue;
            tracks.Count = 1;
            w.AddComponent(unit, tracks);
            var sys = new ThreatEvaluationSystem();

            Assert.Equal(SensorChange.FirstThreat, Assert.Single(Run(w, sys, 1f)).What);   // empty ⇒ one contact
            Assert.Empty(Run(w, sys, 1f));                                                   // still held — no edge

            w.SetComponent(unit, new ActiveSensorTracks());                                  // no sensor tracks it any more
            List<SensorChangedEvent> last = new();
            for (int i = 0; i < 200 && !last.Any(e => e.What == SensorChange.AllClear); i++) last = Run(w, sys, 1f);
            Assert.Contains(last, e => e.What == SensorChange.AllClear && e.Unit == unit);   // forgotten (CE-3046) ⇒ all clear
        }

        [Fact]
        public void AHealthDrop_PublishesHit_OnceForTheDrop()
        {
            var w = World();
            var unit = w.CreateEntity();
            w.AddComponent(unit, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            w.AddComponent(unit, new TargetMemory());
            w.AddComponent(unit, new Health { Current = 100f, Max = 100f });
            var sys = new ThreatEvaluationSystem();

            Assert.Empty(Run(w, sys));                                   // first sight only records the value
            w.SetComponent(unit, new Health { Current = 70f, Max = 100f });
            var hit = Assert.Single(Run(w, sys));
            Assert.Equal(SensorChange.Hit, hit.What);
            Assert.Equal(unit, hit.Unit);
            Assert.Empty(Run(w, sys));                                   // unchanged ⇒ no edge
            w.SetComponent(unit, new Health { Current = 90f, Max = 100f });
            Assert.Empty(Run(w, sys));                                   // healing is not a hit
        }
    }
}
