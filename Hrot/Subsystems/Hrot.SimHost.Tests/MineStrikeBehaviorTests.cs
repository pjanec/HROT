using System.Numerics;
using Fbt;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Terrain;
using Hrot.AI.Behaviors.Brains;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4624</c> MS-B1 (Minefields MF-5, MR-5, MR-6, MR-16; docs/designs/minefields/DESIGN_Minefields.md §11) — the SHIPPED <c>MineStrike</c>
    /// drill through the reflex, the real ingress and brain tick.
    /// </summary>
    public sealed class MineStrikeBehaviorTests
    {
        private static (StationWeaponBehaviorTests.World W, Entity[] Men, MineStrikeReflexSystem Reflex) Squad()
        {
            var w = new StationWeaponBehaviorTests.World();
            var repo = w.Repo;
            if (!repo.Bus.IsRegistered<MineStruck>()) repo.Bus.Register<MineStruck>();
            if (!repo.IsComponentTypeRegistered<UnitRoster>()) repo.RegisterComponent<UnitRoster>();
            if (!repo.IsComponentTypeRegistered<UnitSubordinate>()) repo.RegisterComponent<UnitSubordinate>();
            var roster = new UnitRoster { Count = 3 };
            var men = new Entity[3];
            for (int i = 0; i < 3; i++)
            {
                men[i] = repo.CreateEntity();
                repo.AddComponent(men[i], new SimTransform { Position = new Vector3(5f * i, 0f, 0f), Rotation = Quaternion.Identity });
                repo.AddComponent(men[i], new UnitSubordinate { Commander = w.Man });
                roster.SubordinateEntities[i] = men[i];
            }
            repo.AddComponent(w.Man, roster);
            return (w, men, new MineStrikeReflexSystem());
        }

        private static void Strike(EntityRepository repo, Entity by, Entity field)
            => repo.Bus.Publish(new MineStruck { Field = field, Row = 3, Col = 4, By = by, X = 10f, Y = 0f });

        /// <summary>⭐ MF-5 — a man of the squad sets off a mine: the SQUAD (his commander) runs the drill — every live man is ordered to hold where
        /// he stands (none carried back across the field) — and holds until its <c>Hold</c> is over, then the task may go on. A second strike of the
        /// same field while it runs does not start it again. ✅ Red-proof: the hold orders dropped ⇒ red.</summary>
        [Fact]
        public void CE4624_MS_B1_AManSetsOffAMine_HisSquadHalts_EveryManHoldsWhereHeStands_ThenTheHoldEnds()
        {
            var (w, men, reflex) = Squad();
            var repo = w.Repo;
            var field = repo.CreateEntity();
            int holds = 0, strikes = 0;
            void Tick()
            {
                w.Tick();
                reflex.Execute(repo, 0.1f);
                foreach (var e in repo.ReadManagedEvents<AssignTacticalIntentEvent>()) if (e != null && e.IntentId == BehaviorNames.PersonHold) holds++;
                foreach (var a in repo.ReadManagedEvents<AssignBehaviorEvent>()) if (a != null && a.BehaviorName == BehaviorNames.MineStrike) strikes++;
            }
            Strike(repo, men[1], field);
            for (int i = 0; i < 5; i++) Tick();
            Assert.Equal(3, holds);                                       // every live man, once
            Assert.Null(w.Finished);
            Strike(repo, men[2], field);                                 // the same field again: no new drill
            for (int i = 0; i < 5; i++) Tick();
            Assert.Equal(3, holds);
            Assert.True(strikes <= 1, $"one reaction per field: {strikes}");
            for (int i = 0; i < (int)(MineStrikeBehavior.DefaultHoldSeconds * 10) + 5; i++) Tick();
            Assert.Equal(NodeStatus.Success, w.Finished);               // the hold is over (or, MS-T2 on, the side marked the field)
        }

        /// <summary>⭐ MR-16 — a VEHICLE that sets off a mine runs the drill itself: it stops (its move channel cleared), its crew stays aboard.
        /// ✅ Red-proof: the stop dropped ⇒ the move still active ⇒ red.</summary>
        [Fact]
        public void CE4624_MS_B1_AVehicleSetsOffAMine_ItStops_TheCrewStaysAboard()
        {
            var w = new StationWeaponBehaviorTests.World();
            var repo = w.Repo;
            if (!repo.Bus.IsRegistered<MineStruck>()) repo.Bus.Register<MineStruck>();
            if (!repo.IsComponentTypeRegistered<global::CarKinem.Core.VehicleState>()) repo.RegisterComponent<global::CarKinem.Core.VehicleState>();
            repo.AddComponent(w.Man, new global::CarKinem.Core.VehicleState());
            Assert.Equal(w.Man, MineStrikeReflexSystem.UnitOf(repo, w.Man));
            w.Start(BehaviorNames.MoveToLocation, "{\"target\":[500,0,0],\"speed\":5}");
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal(Fdp.Toolkit.Navigation.NavigationConstants.ActionIdMoveTo, repo.GetComponentRO<LocomotionChannel>(w.Man).ActiveAction);
            var reflex = new MineStrikeReflexSystem();
            Strike(repo, w.Man, repo.CreateEntity());
            for (int i = 0; i < 4; i++) { w.Tick(); reflex.Execute(repo, 0.1f); }
            Assert.NotEqual(Fdp.Toolkit.Navigation.NavigationConstants.ActionIdMoveTo, repo.GetComponentRO<LocomotionChannel>(w.Man).ActiveAction);
            Assert.Equal(BehaviorOrigin.Reaction, repo.GetComponentRO<BehaviorState>(w.Man).Origin);
            Assert.Equal(Fdp.Toolkit.Behavior.BehaviorHash.FromName(BehaviorNames.MineStrike), repo.GetComponentRO<BehaviorState>(w.Man).ActiveBehaviorHash);
        }

        /// <summary>⭐ MR-6 (R-4263) — the drill declares <c>Halts | LeavesRoute</c>: a unit on a Dash refuses it at THE ONE GATE (it presses on).
        /// ✅ Red-proof: the declaration dropped ⇒ red.</summary>
        [Fact]
        public void CE4624_MS_B1_TheDrill_DeclaresHaltsAndLeavesRoute_SoADashPressesOn()
        {
            Hrot.AI.Behaviors.Orders.OrderKinds.Build();
            var conduct = Fdp.Toolkit.Behavior.BehaviorConduct.Of(Fdp.Toolkit.Behavior.BehaviorHash.FromName(BehaviorNames.MineStrike));
            Assert.Equal(Fdp.Toolkit.Behavior.Conduct.Halts | Fdp.Toolkit.Behavior.Conduct.LeavesRoute, conduct);
            Assert.NotEqual(Fdp.Toolkit.Behavior.Conduct.None, conduct & Hrot.AI.Behaviors.Orders.OrderConduct.Forbids(Hrot.AI.Behaviors.Orders.OrderKind.Dash));
        }
    }
}
