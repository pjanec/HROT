using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>⭐ <c>CE-4589</c> (Command Chain §8e HQ-10…HQ-14) — the battalion's RESERVE: a designated roster slot answers a Reinforce, with the
    /// play of its echelon; the HQ's other ground units are never pulled.</summary>
    public partial class SquadManeuverTests
    {
        /// <summary>An HQ over six ground units (slot 0 asks; slot 3 the reserve, a COMPANY when <paramref name="company"/>, else a PLATOON;
        /// slots 1, 2, 4, 5 squads), running <c>SupportCommand {Reserve}</c>.</summary>
        private static Platoon Battalion(int reserve, bool company)
        {
            var p = new Platoon(squads: 6);
            var repo = p.S.Repo;
            Entity Unit(Entity e, params Entity[] members)
            {
                if (!repo.HasComponent<UnitEntity>(e)) repo.AddComponent(e, new UnitEntity());
                var roster = new UnitRoster();
                if (members.Length == 0) UnitRoster.Add(ref roster, repo.CreateEntity());
                foreach (var m in members) UnitRoster.Add(ref roster, m);
                if (repo.HasComponent<UnitRoster>(e)) repo.SetComponent(e, roster); else repo.AddComponent(e, roster);
                return e;
            }
            for (int i = 0; i < 6; i++) Unit(p.S.Members[i]);
            var squad = Unit(repo.CreateEntity());
            if (company) Unit(p.S.Members[3], Unit(repo.CreateEntity(), squad));   // a unit of platoons
            else Unit(p.S.Members[3], squad);                                         // a unit of squads
            p.S.Order("{\"Reserve\":" + reserve + "}", BehaviorNames.SupportCommand);
            p.Ticks(4);
            return p;
        }

        private static void Spot(Platoon p, Vector3 at)
            => p.Report(p.S.Members[0], new Fdp.Toolkit.Squad.Command.CommandReport { Kind = Fdp.Toolkit.Squad.Command.ReportKind.Spot, At = at, Count = 3 });

        /// <summary>⭐ <c>CE-4589</c> acceptance 1 + 2 — Reserve = slot 3 (a company): slot 0's Reinforce sends slot 3 <c>CompanyDefend</c> at the
        /// request's point facing the spotted enemy; the free squads (1, 2, 4, 5) are never chosen; a second ask while the reserve is committed
        /// waits. ✅ Red-proofs: the slot mask ignored ⇒ slot 1 goes · the echelon read as "unit of units" ⇒ <c>PlatoonDefend</c> ⇒ red.</summary>
        [Fact]
        public void CE4589_TheReserveCompany_AnswersAReinforce_WithCompanyDefend_AndNoOtherUnitIsPulled()
        {
            var p = Battalion(reserve: 1 << 3, company: true);
            var at = new Vector3(50f, 400f, 0f);
            Spot(p, new Vector3(60f, 520f, 0f));
            Ask(p, Fdp.Toolkit.Squad.Command.RequestKind.Reinforce, at);
            p.Ticks(4);
            Assert.Equal(1, p.OrdersTo(p.S.Members[3], BehaviorNames.CompanyDefend));
            var order = OrderTo<CompanyDefendParamsJsonDto>(p, p.S.Members[3], BehaviorNames.CompanyDefend);
            Assert.Equal<Vector3>(at, (Vector3)order.Position);
            Assert.Equal<Vector3>(new Vector3(60f, 520f, 0f), (Vector3)order.Threat);
            foreach (int i in new[] { 0, 1, 2, 4, 5 })
                Assert.Equal(0, p.S.Orders.Count(o => o.Entity.Equals(p.S.Members[i]) && o.IntentId != BehaviorNames.SupportCommand));

            Running(p, p.S.Members[3], BehaviorNames.CompanyDefend);
            Ask(p, Fdp.Toolkit.Squad.Command.RequestKind.Reinforce, at);
            p.Ticks(4);
            Assert.Equal(1, p.OrdersTo(p.S.Members[3], BehaviorNames.CompanyDefend));   // committed once
            foreach (int i in new[] { 1, 2, 4, 5 })
                Assert.Equal(0, p.OrdersTo(p.S.Members[i], BehaviorNames.SquadDefend));
            Assert.NotEmpty(Requests(PictureOf(p, p.S.Commander)));   // the ask waits
        }

        /// <summary>⭐ <c>CE-4589</c> acceptance 3 — a PLATOON reserve gets <c>PlatoonDefend</c>; and the echelon helper reads squad 0 · platoon 1 ·
        /// company 2. ✅ Red-proof: <c>DepthOf</c> stops at one level ⇒ the company reads as a platoon ⇒ red.</summary>
        [Fact]
        public void CE4589_APlatoonReserve_GetsPlatoonDefend_AndTheEchelonIsTheRosterDepth()
        {
            var p = Battalion(reserve: 1 << 3, company: false);
            Ask(p, Fdp.Toolkit.Squad.Command.RequestKind.Reinforce, new Vector3(50f, 400f, 0f));
            p.Ticks(4);
            Assert.Equal(1, p.OrdersTo(p.S.Members[3], BehaviorNames.PlatoonDefend));
            Assert.Equal(0, p.OrdersTo(p.S.Members[1], BehaviorNames.SquadDefend));

            var c = Battalion(reserve: 1 << 3, company: true);
            Assert.Equal(Fdp.Toolkit.Squad.Echelon.Squad, Fdp.Toolkit.Squad.Echelon.DepthOf(c.S.Repo, c.S.Members[1]));
            Assert.Equal(Fdp.Toolkit.Squad.Echelon.Company, Fdp.Toolkit.Squad.Echelon.DepthOf(c.S.Repo, c.S.Members[3]));
            Assert.Equal(Fdp.Toolkit.Squad.Echelon.Company + 1, Fdp.Toolkit.Squad.Echelon.DepthOf(c.S.Repo, c.S.Commander));
        }
    }
}
