using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Squad.Command;
using Hrot.AI.Behaviors.Brains;
using Hrot.AI.Behaviors.Brains.Objectives;
using Hrot.CGF.Systems;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>⭐⭐ agg-behaviors <c>ST-0</c> — the acceptance rail: a physical company commands a STOCHASTIC platoon end to end.</summary>
    public partial class SquadManeuverTests
    {
        /// <summary>
        /// ⭐⭐⭐ agg-behaviors <c>ST-0 ACCEPTANCE</c> (design §23.5 / §23.6; R-8300…R-8306) — a physical COMPANY commander over a STOCHASTIC platoon (an
        /// <c>AggregateUnit</c> Stochastic, a full slot table, its holdings, no roster) and a physical one: ① the stochastic platoon's SITREP strength reaches the
        /// company GREEN (46 of 48 slots up); ② the objectives' census counts its 46 men in an area; ③ the company's defence order — through
        /// <c>CommandOrders.Route</c> — goes to it as <c>Order.Defend</c> (the physical platoon still gets <c>PlatoonDefend</c>), the receiver's routing table resolves
        /// it to <c>StochasticUnitDefend</c>, the play runs and the company's picture shows PROGRESS Set, the unit's <c>Prepared</c> rising Hasty → Prepared with sim
        /// time; ④ a Strike order to it — no routing row — is refused and the company hears PROGRESS TaskFailed.
        /// ✅ Red-proofs: <c>Route</c> always <c>Assign</c> ⇒ ③ no Order.Defend · the staff's stochastic query dropped ⇒ no SITREP, no Set ⇒ ①/③ red ·
        /// the routing row's play unregistered ⇒ no play runs ⇒ ③ red.
        /// </summary>
        [Fact]
        public void ST0_Acceptance_APhysicalCompany_CommandsAStochasticPlatoon_EndToEnd()
        {
            var c = new Company();
            var repo = c.S.Repo;
            var plat = c.First; var physical = c.Second;
            AggregateTestUnits.Make(repo, plat, slots: 48, up: 46);
            repo.AddComponent(plat, new UnitEntity());
            repo.AddComponent(plat, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 6001 });
            repo.AddComponent(plat, new BehaviorState());
            repo.SetAuthority<BehaviorState>(plat, true);
            repo.SetDisType(plat, Hrot.Core.Tkb.UnitDis.Land(Hrot.Core.Tkb.UnitArm.Infantry, Hrot.Core.Tkb.UnitEchelon.Platoon, 225));
            var composer = new StaffComposerSystem();
            var resolver = new TacticalIntentResolutionSystem(new TacticalIntentMapperRegistry(), c.S.Registry);
            int seen = 0;
            void Frame()
            {
                c.Ticks(1);
                composer.Execute(repo, 0.016f);
                repo.FlushCommandBuffers();                                                                // a parallel phase defers its events/commands to its batch end
                // the receiver's resolution: every superior order to the stochastic platoon, once
                var all = c.S.Orders;
                for (; seen < all.Count; seen++)
                    if (all[seen].Entity.Equals(plat)) { repo.Bus.PublishManaged(all[seen]); repo.Bus.SwapBuffers(); resolver.Execute(repo, 0.016f); }
            }
            void Frames(int n) { for (int i = 0; i < n; i++) Frame(); }

            // the company defends — every platoon gets the neutral Defend order
            c.S.Order("{\"Position\":" + Pt(Vector3.Zero) + ",\"Threat\":" + Pt(new Vector3(0f, 500f, 0f)) + "}", BehaviorNames.CompanyDefend);
            Frames(8);
            Assert.True(c.S.Orders.Any(o => o.Entity.Equals(plat) && o.IntentId == "Order.Defend"), "③ the stochastic platoon is ordered by KIND");
            Assert.False(c.S.Orders.Any(o => o.Entity.Equals(plat) && o.IntentId == BehaviorNames.PlatoonDefend));
            Assert.True(c.OrdersTo(physical, BehaviorNames.PlatoonDefend) > 0, "the physical platoon still gets its play");
            Assert.Equal(1, c.S.Orders.Count(o => o.Entity.Equals(plat) && o.IntentId == "Order.Defend"));   // an unchanged re-send is recognised (R-8301): once

            // ③ the receiver's routing table started the generic play, and it reported Set to the company
            Assert.Equal(BehaviorHash.FromName(Hrot.Core.Tkb.ResolutionRoutingTable.Plays.Defend), repo.GetComponentRO<BehaviorState>(plat).ActiveBehaviorHash);
            c.S.Pause(5); Frames(6);
            Assert.Equal(ProgressKind.Set, CommandPictureOps.ProgressOf(repo, c.S.Commander, 0));
            Assert.Equal(PreparedLevel.Hasty, repo.GetComponentRO<AggregateUnit>(plat).Prepared);
            c.S.Pause(700); Frames(3);
            Assert.Equal(PreparedLevel.Prepared, repo.GetComponentRO<AggregateUnit>(plat).Prepared);      // R-8306: after 10 min

            // ① its SITREP strength (asked by the company, CE-4615) reads Green
            c.S.Pause(20); Frames(4); c.S.Pause(5); Frames(4);                                           // a SITREP is composed, then sent after the staff's delay
            int row = CommandPictureOps.EnsureRow(repo, c.S.Commander, CommandPictureOps.NetIdOf(repo, plat));   // the picture's row of THIS platoon (its order is the report order)
            Assert.Equal(StatusColour.Green, repo.GetComponentRO<CommandPicture>(c.S.Commander).Subordinates.Items[row].Strength);

            // ② the census counts its men inside an area
            var area = AreaLink.Circle(new WorldPoint(0f, 0f, 0f), 50f);
            repo.GetComponentRW<SimTransform>(plat).Position = Vector3.Zero;
            Assert.Equal(46, ObjectiveCensus.MenInside(repo, new EntityRef(6001), in area));

            // ④ a refused order (Strike: no routing row) reaches the company as TaskFailed
            repo.GetComponentRW<CommandPicture>(c.S.Commander).Subordinates.Items[row].Progress = ProgressKind.NoProgress;
            CommandOrders.Order(repo, c.S.Commander, plat, "Strike", "{}", newOrder: true);
            Frames(4); c.S.Pause(5); Frames(6);                                                          // refused ⇒ queued ⇒ the staff's delay passes ⇒ sent
            Assert.Equal(ProgressKind.TaskFailed, CommandPictureOps.ProgressOf(repo, c.S.Commander, 0));
        }
    }
}
