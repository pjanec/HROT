using System;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Perception.Components;
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

        /// <summary>⭐ <c>CE-2239</c> part 1 (CV-8, §5j FL-1) — the FORCE LEVEL: no ROE or an unset level reads Lethal (today's behaviour); an
        /// order sets it; <c>ForceUnset</c> keeps it; the saved / replicated intent carries it. ✅ Red-proof: RoeSystem's ForceLevel line
        /// dropped ⇒ red; the reader's line dropped ⇒ red.</summary>
        [Fact]
        public void CE2239_AnOrderSetsTheForceLevel_UnsetKeepsIt_AndTheIntentCarriesIt()
        {
            var world = World();
            var sys = new RoeSystem();
            var e = world.CreateEntity();
            Assert.Equal(RoeForceLevel.Lethal, RoeOf.ForceLevel(world, e));
            world.AddComponent(e, new Roe());
            Assert.Equal(RoeForceLevel.Lethal, RoeOf.ForceLevel(world, e));

            world.Bus.Publish(new SetRoeEvent { Entity = e, Origin = BehaviorOrigin.Superior, ForceLevel = RoeForceLevel.NonLethal });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);
            Assert.Equal(RoeForceLevel.NonLethal, RoeOf.ForceLevel(world, e));

            Set(world, sys, e, RoeFire.HoldFire, RoeReactions.ReactionsUnset, BehaviorOrigin.Superior);   // ForceUnset ⇒ kept
            Assert.Equal(RoeForceLevel.NonLethal, RoeOf.ForceLevel(world, e));

            var saved = BrainIntentReader.RoeOf(world, e, BrainIntentScope.Ordered);
            Assert.Equal(RoeForceLevel.NonLethal, saved!.ForceLevel);
            var json = System.Text.Json.JsonSerializer.Serialize(saved);
            Assert.Equal(RoeForceLevel.NonLethal, System.Text.Json.JsonSerializer.Deserialize<SavedRoe>(json)!.ForceLevel);
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

        /// <summary>
        /// 🔴 <c>CE-2104</c> — "alerted" is derived: a HEARD contact in the memory makes the unit in contact; the memory
        /// emptying ends it, unless the linger keeps it on for N s after <c>AllClear</c>.
        /// </summary>
        [Fact]
        public void CE2104_InContact_FollowsTheMemory_AndLingersAfterAllClear()
        {
            var world = World();
            if (!world.IsComponentTypeRegistered<TargetMemory>()) world.RegisterComponent<TargetMemory>();
            var sys = new RecentSensesSystem();
            var e = world.CreateEntity();
            world.AddComponent(e, new TargetMemory());
            var now = new GlobalTime { TotalTime = 100.0 };
            world.SetSingleton(now);
            var noLinger = new SopContactParams();
            var linger = new SopContactParams { LingerSeconds = 30f };

            Assert.False(SopConditions.InContact(ref noLinger, e, world));                // relaxed: nothing remembered

            ref var mem = ref world.GetComponentRW<TargetMemory>(e);
            TargetMemory.HearContact(ref mem, 0f, 50f, 0f, 8f, sourceClass: 1, scoreBoost: 5f, tick: 1);
            Assert.True(SopConditions.InContact(ref noLinger, e, world));                 // a heard shot ⇒ in contact

            TargetMemory.Forget(ref world.GetComponentRW<TargetMemory>(e), 0);            // it faded …
            world.Bus.Publish(new SensorChangedEvent { Unit = e, What = SensorChange.AllClear });   // … ⇒ AllClear
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);
            Assert.False(SopConditions.InContact(ref noLinger, e, world));                // no linger: ends with the contact
            Assert.True(SopConditions.InContact(ref linger, e, world));                   // linger: still alerted …

            now.TotalTime += 31.0;
            world.SetSingleton(now);
            Assert.False(SopConditions.InContact(ref linger, e, world));                  // … until 30 s after AllClear
            world.Dispose();
        }
    
        /// <summary>⭐ <c>CE-2123</c> — every kind is recorded and read back, the danger-area kinds (8–10, until now silently dropped by a
        /// byte mask) and the squad kinds (11–13) included. ✅ Red-proof: the mask back to a byte ⇒ the kinds ≥ 8 read as never seen.</summary>
        [Fact]
        public void CE2123_EveryKind_IsRecorded_AndReadBack()
        {
            var senses = new Fdp.Toolkit.Behavior.Components.RecentSenses();
            var kinds = (Fdp.Toolkit.Perception.Events.SensorChange[])Enum.GetValues(typeof(Fdp.Toolkit.Perception.Events.SensorChange));
            for (int i = 0; i < kinds.Length; i++) senses.Record(kinds[i], 10d + i);
            for (int i = 0; i < kinds.Length; i++)
            {
                Assert.True(senses.TryGetLast(kinds[i], out double at), kinds[i].ToString());
                Assert.Equal(10d + i, at);
            }
        }

        // ── CE-2289 (PC-6): ROE up the chain of command ─────────────────────────────────────────────────────────────────

        /// <summary>A platoon → squad → two men, the hierarchy registered.</summary>
        private static (EntityRepository world, RoeSystem sys, Entity platoon, Entity squad, Entity a, Entity b) Chain()
        {
            var world = World();
            if (!world.IsComponentTypeRegistered<Fdp.Core.CommandHierarchy.UnitSubordinate>())
                world.RegisterComponent<Fdp.Core.CommandHierarchy.UnitSubordinate>();
            var platoon = world.CreateEntity();
            var squad = world.CreateEntity();
            var a = world.CreateEntity();
            var b = world.CreateEntity();
            world.AddComponent(squad, new Fdp.Core.CommandHierarchy.UnitSubordinate { Commander = platoon });
            world.AddComponent(a, new Fdp.Core.CommandHierarchy.UnitSubordinate { Commander = squad });
            world.AddComponent(b, new Fdp.Core.CommandHierarchy.UnitSubordinate { Commander = squad });
            world.AddComponent(a, new Roe());   // the TKB's unset default, as BehaviorTkbTranslator stamps it
            world.AddComponent(b, new Roe());
            return (world, new RoeSystem(), platoon, squad, a, b);
        }

        /// <summary>⭐ <c>CE-2289</c> — a squad's <c>HoldFire</c> holds every member; a man's own order still wins. ✅ Red-proof: the
        /// chain walk removed from <c>RoeOf.Decide</c> ⇒ both men read <c>FireAtWill</c>.</summary>
        [Fact]
        public void CE2289_ASquadsHoldFire_HoldsEveryMember_AndAMansOwnOrderWins()
        {
            var (world, sys, _, squad, a, b) = Chain();
            Set(world, sys, squad, RoeFire.HoldFire, RoeReactions.StayOnTask, BehaviorOrigin.Superior);
            Assert.Equal(RoeFire.HoldFire, RoeOf.Fire(world, a));
            Assert.Equal(RoeFire.HoldFire, RoeOf.Fire(world, b));
            Assert.Equal(RoeReactions.StayOnTask, RoeOf.Reactions(world, b));

            Set(world, sys, a, RoeFire.FireAtWill, RoeReactions.ReactionsUnset, BehaviorOrigin.Superior);   // a per-man setting
            Assert.Equal(RoeFire.FireAtWill, RoeOf.Fire(world, a));
            Assert.Equal(RoeFire.HoldFire, RoeOf.Fire(world, b));
            world.Dispose();
        }

        /// <summary>⭐ <c>CE-2289</c> — the NEAREST set commander decides, per field: the platoon's ROE reaches the men through a squad
        /// that set nothing; a squad's own setting beats the platoon's; a field the squad left unset still comes from the platoon.</summary>
        [Fact]
        public void CE2289_TheNearestSetCommanderDecides_PerField()
        {
            var (world, sys, platoon, squad, a, _) = Chain();
            world.Bus.Publish(new SetRoeEvent { Entity = platoon, Fire = RoeFire.HoldFire, ForceLevel = RoeForceLevel.NonLethal,
                                                ReturnFireWindowSeconds = 9f, Origin = BehaviorOrigin.Operator });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);
            Assert.Equal(RoeFire.HoldFire, RoeOf.Fire(world, a));                 // through a squad with no ROE at all
            Assert.Equal(RoeForceLevel.NonLethal, RoeOf.ForceLevel(world, a));
            Assert.Equal(9f, RoeOf.ReturnFireWindowSeconds(world, a));

            Set(world, sys, squad, RoeFire.ReturnFire, RoeReactions.ReactionsUnset, BehaviorOrigin.Superior);
            Assert.Equal(RoeFire.ReturnFire, RoeOf.Fire(world, a));               // the nearer one
            Assert.Equal(RoeForceLevel.NonLethal, RoeOf.ForceLevel(world, a));   // the squad left it unset ⇒ still the platoon's
            Assert.Equal(RoeReactions.React, RoeOf.Reactions(world, a));          // nobody set it ⇒ the built-in default
            world.Dispose();
        }

        /// <summary>⭐ <c>CE-2289</c> — a TKB default on the man (set by nobody) yields to a commander's ORDER, and still applies when
        /// no commander set the field (the security types' <c>NonLethal</c>, §5j FL-1); a commander's own TKB default reaches nobody.</summary>
        [Fact]
        public void CE2289_ATkbDefault_YieldsToTheChain_ButAppliesWhenTheChainIsSilent()
        {
            var (world, sys, _, squad, a, _) = Chain();
            world.GetComponentRW<Roe>(a) = new Roe { ForceLevel = RoeForceLevel.NonLethal };   // the man's TKB default
            world.AddComponent(squad, new Roe { Fire = RoeFire.HoldFire });                     // the squad's TKB default
            Assert.Equal(RoeForceLevel.NonLethal, RoeOf.ForceLevel(world, a));
            Assert.Equal(RoeFire.FireAtWill, RoeOf.Fire(world, a));               // a commander's TKB default is not an order

            world.Bus.Publish(new SetRoeEvent { Entity = squad, ForceLevel = RoeForceLevel.Lethal, Origin = BehaviorOrigin.Superior });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);
            Assert.Equal(RoeForceLevel.Lethal, RoeOf.ForceLevel(world, a));
            Assert.Equal(RoeFire.HoldFire, RoeOf.Fire(world, a));                 // now the squad's ROE is an order's, every field
            world.Dispose();
        }

        /// <summary>⭐ <c>CE-2289</c> — a mis-authored cycle (A commands B commands A) ends at <c>RoeOf.MaxChainDepth</c>, and a world
        /// without the hierarchy registered reads the man alone.</summary>
        [Fact]
        public void CE2289_ACycle_Ends_AndNoHierarchy_ReadsTheManAlone()
        {
            var (world, _, _, squad, a, _) = Chain();
            world.GetComponentRW<Fdp.Core.CommandHierarchy.UnitSubordinate>(squad).Commander = a;   // a ↔ squad
            Assert.Equal(RoeFire.FireAtWill, RoeOf.Fire(world, a));
            world.Dispose();

            var bare = World();
            var man = bare.CreateEntity();
            Assert.Equal(RoeFire.FireAtWill, RoeOf.Fire(bare, man));
            bare.Dispose();
        }
}
}
