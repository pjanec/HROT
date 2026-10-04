using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Perception.Systems;
using Xunit;

namespace Fdp.Toolkit.Perception.Tests
{
    /// <summary>Unit rails for <see cref="SensorTrackDebounceSystem"/> (CE-3032).</summary>
    public class SensorTrackDebounceSystemTests
    {
        private static Entity Unit(EntityRepository world, float x)
        {
            var e = world.CreateEntity();
            world.AddComponent(e, new SimTransform { Position = new Vector3(x, 0f, 0f), Rotation = Quaternion.Identity });
            return e;
        }

        private static void Sight(EntityRepository world, Entity observer, params Entity[] targets)
        {
            foreach (var t in targets) world.Bus.Publish(new TargetVisibleEvent { Observer = observer, Target = t });
            world.Bus.SwapBuffers();
        }

        /// <summary>
        /// ⭐ CE-3032 — an observer that sees THREE targets in its first tick keeps all three. 🔴 Pass 2 used to add one
        /// single-contact list PER sighting, so the last add won: two targets were announced Acquired and then never
        /// tracked (never refreshed, never Lost).
        /// </summary>
        [Fact]
        public void FirstTick_SeveralSightings_OneListHoldingEveryContact_CE3032()
        {
            var world = PerceptionTestWorldFactory.Create();
            var observer = Unit(world, 0f);
            var a = Unit(world, 10f); var b = Unit(world, 20f); var c = Unit(world, 30f);
            Sight(world, observer, a, b, c);

            new SensorTrackDebounceSystem().Execute(world, 0.1f);
            world.FlushCommandBuffers();
            world.Bus.SwapBuffers();

            var list = world.GetComponentRO<SensorContactList>(observer);
            Assert.Equal(3, list.Count);
            var acquired = world.Bus.Read<SensorTrackStateEvent>();
            Assert.Equal(3, acquired.Length);
            foreach (var evt in acquired) Assert.Equal(SensorTrackStatus.Acquired, evt.State);
        }

        /// <summary>
        /// ⭐ CE-3032 — grouping by observer keeps every sighting with ITS observer: two observers' events interleaved
        /// in one tick update each observer's own list only.
        /// </summary>
        [Fact]
        public void InterleavedObservers_EachKeepsOnlyItsOwnSightings_CE3032()
        {
            var world = PerceptionTestWorldFactory.Create();
            var o1 = Unit(world, 0f); var o2 = Unit(world, 1f);
            var a = Unit(world, 10f); var b = Unit(world, 20f);
            world.Bus.Publish(new TargetVisibleEvent { Observer = o1, Target = a });
            world.Bus.Publish(new TargetVisibleEvent { Observer = o2, Target = b });
            world.Bus.Publish(new TargetVisibleEvent { Observer = o1, Target = b });
            world.Bus.SwapBuffers();

            new SensorTrackDebounceSystem().Execute(world, 0.1f);
            world.FlushCommandBuffers();

            Assert.Equal(2, world.GetComponentRO<SensorContactList>(o1).Count);
            var l2 = world.GetComponentRO<SensorContactList>(o2);
            Assert.Equal(1, l2.Count);
            unsafe { Assert.Equal((long)b.PackedValue, l2.EntityIds[0]); }
        }
    }
}
