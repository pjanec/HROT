using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Perception.Events;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>⭐ <c>CE-2074</c> (ROE, R-200) and <c>CE-2076</c> (recent senses) — new features, no suite before.</summary>
    public sealed class RoeAndRecentSensesTests
    {
        private static EntityRepository World()
        {
            var world = TestWorldFactory.Create();
            if (!world.IsComponentTypeRegistered<Roe>()) world.RegisterComponent<Roe>();
            if (!world.IsComponentTypeRegistered<RecentSenses>()) world.RegisterComponent<RecentSenses>();
            world.Bus.Register<SetRoeEvent>();
            world.Bus.Register<SensorChangedEvent>();
            return world;
        }

        private static void Set(EntityRepository world, RoeSystem sys, Entity e, RoeFire fire, RoeReactions reactions, BehaviorOrigin origin)
        {
            world.Bus.Publish(new SetRoeEvent { Entity = e, Fire = fire, Reactions = reactions, Origin = origin });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);
        }

        [Fact]
        public void CE2074_NoRoe_ReadsAsFireAtWill_AndReact()
        {
            var world = World();
            var e = world.CreateEntity();
            Assert.Equal(RoeFire.FireAtWill, RoeOf.Fire(world, e));
            Assert.Equal(RoeReactions.React, RoeOf.Reactions(world, e));
            world.Dispose();
        }

        [Fact]
        public void CE2074_AnOrderSetsTheRoe_AndALowerOriginCannotChangeIt()
        {
            var world = World();
            var sys = new RoeSystem();
            var e = world.CreateEntity();

            Set(world, sys, e, RoeFire.HoldFire, RoeReactions.StayOnTask, BehaviorOrigin.Superior);
            Assert.Equal(RoeFire.HoldFire, RoeOf.Fire(world, e));
            Assert.Equal(RoeReactions.StayOnTask, RoeOf.Reactions(world, e));

            Set(world, sys, e, RoeFire.FireAtWill, RoeReactions.ReactionsUnset, BehaviorOrigin.Sop);   // the SOP may not relax an order's ROE
            Assert.Equal(RoeFire.HoldFire, RoeOf.Fire(world, e));
            Assert.Equal(1, sys.RefusedCount);

            Set(world, sys, e, RoeFire.ReturnFire, RoeReactions.ReactionsUnset, BehaviorOrigin.Operator);   // Unset keeps Reactions
            Assert.Equal(RoeFire.ReturnFire, RoeOf.Fire(world, e));
            Assert.Equal(RoeReactions.StayOnTask, RoeOf.Reactions(world, e));
            world.Dispose();
        }

        [Fact]
        public void CE2074_TheTkbDefault_YieldsToAnyOrigin()
        {
            var world = World();
            var sys = new RoeSystem();
            var e = world.CreateEntity();
            world.AddComponent(e, new Roe { Fire = RoeFire.HoldFire });   // TKB default: SetBy Unmarked

            Set(world, sys, e, RoeFire.FireAtWill, RoeReactions.ReactionsUnset, BehaviorOrigin.Sop);
            Assert.Equal(RoeFire.FireAtWill, RoeOf.Fire(world, e));
            world.Dispose();
        }

        /// <summary>⭐ <c>CE-2095</c> — an order sets the ReturnFire window; <c>0</c> keeps it (as <c>Unset</c> does); no ROE or an
        /// unset window reads as the 5 s default; the saved / replicated intent carries it.</summary>
        [Fact]
        public void CE2095_AnOrderSetsTheReturnFireWindow_ZeroKeepsIt_AndTheIntentCarriesIt()
        {
            var world = World();
            var sys = new RoeSystem();
            var e = world.CreateEntity();
            Assert.Equal(RoeOf.DefaultReturnFireWindowSeconds, RoeOf.ReturnFireWindowSeconds(world, e));

            world.Bus.Publish(new SetRoeEvent { Entity = e, Fire = RoeFire.ReturnFire, Origin = BehaviorOrigin.Superior,
                                                ReturnFireWindowSeconds = 12f });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);
            Assert.Equal(12f, RoeOf.ReturnFireWindowSeconds(world, e));

            Set(world, sys, e, RoeFire.HoldFire, RoeReactions.ReactionsUnset, BehaviorOrigin.Superior);   // window 0 ⇒ kept
            Assert.Equal(12f, RoeOf.ReturnFireWindowSeconds(world, e));

            var saved = BrainIntentReader.RoeOf(world, e, BrainIntentScope.Ordered);
            Assert.Equal(12f, saved!.ReturnFireWindowSeconds);
            var json = System.Text.Json.JsonSerializer.Serialize(saved);
            Assert.Equal(12f, System.Text.Json.JsonSerializer.Deserialize<SavedRoe>(json)!.ReturnFireWindowSeconds);
            world.Dispose();
        }

        [Fact]
        public void CE2076_ASensingChange_IsRemembered_AfterItsEventIsGone()
        {
            var world = World();
            var sys = new RecentSensesSystem();
            var e = world.CreateEntity();

            world.Bus.Publish(new SensorChangedEvent { Unit = e, What = SensorChange.Hit });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();   // the event is gone now

            Assert.True(RecentSensesOf.Within(world, e, SensorChange.Hit, 5.0));
            Assert.False(RecentSensesOf.Within(world, e, SensorChange.FirstThreat, 5.0));   // never happened

            var senses = world.GetComponent<RecentSenses>(e);
            Assert.False(senses.Within(SensorChange.Hit, 5.0, now: senses.Hit + 6.0));   // too long ago
            world.Dispose();
        }
    }
}
