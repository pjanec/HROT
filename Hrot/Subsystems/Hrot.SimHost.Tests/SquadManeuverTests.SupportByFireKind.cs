using System;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;
using Hrot.AI.Behaviors.Orders;
using Hrot.CGF.Commander;
using Hrot.Core.Tkb;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>CE-4686</c> ③ — a subordinate's SUPPORT BY FIRE: where it stands, what it fires at, and the form the ONE routing table resolves for it.
    /// </summary>
    public partial class SquadManeuverTests
    {
        private static string? SupportByFireFor(UnitEchelon echelon, bool stochastic)
        {
            var repo = new EntityRepository();
            var u = repo.CreateEntity();
            repo.SetDisType(u, UnitDis.Land(UnitArm.Infantry, echelon, 225));
            if (stochastic) AggregateTestUnits.Make(repo, u, slots: 9, up: 9, echelon: (byte)(echelon == UnitEchelon.Squad ? 0 : 1));
            var resolution = stochastic ? AggregateResolution.Stochastic : AggregateResolution.Physical;
            return OrderReissue.PlayFor(repo, u, resolution, OrderKind.SupportByFire, KnownBehaviour);
        }

        /// <summary>
        /// ⭐ Measured, and pinned: the KIND SupportByFire resolves to <c>PlatoonSupportByFire</c> for a physical platoon, to the GENERIC stochastic attack
        /// (which holds back only on <c>HoldBack</c>) for a stochastic one, and to NOTHING for a physical squad (its play is named plain
        /// <c>SupportByFire</c>, not <c>&lt;Class&gt;SupportByFire</c>) — the seat picks the form from this.
        /// </summary>
        [Fact]
        public void CE4686_TheKindSupportByFire_ResolvesForEverySubordinateTheSeatOrders()
        {
            Assert.Equal("PlatoonSupportByFire", SupportByFireFor(UnitEchelon.Platoon, stochastic: false));
            Assert.Equal(ResolutionRoutingTable.Plays.Attack, SupportByFireFor(UnitEchelon.Platoon, stochastic: true));
            Assert.Null(SupportByFireFor(UnitEchelon.Squad, stochastic: false));
        }

        /// <summary>
        /// ⭐ A SupportByFire with a support place: a PHYSICAL platoon gets the KIND (PlatoonSupportByFire: it sets AT the support place, Post1 = the
        /// target); a STOCHASTIC one gets Attack + HoldBack standing AT the support place (CE-8302: with HoldBack the Position is where it stands).
        /// 📌 CE-4685 (Haiku): the old form stood a platoon on the OBJECTIVE (Red's position) and gave a physical platoon a hold-back Attack, which
        /// has no platoon play (R-8392) — it stalled. 🔴 Red-proof: the support_at mapping dropped from ToAction ⇒ Position = the objective.
        /// </summary>
        [Fact]
        public void CE4686_ASupportByFire_StandsAtTheSupportPlace_InTheFormTheSubordinateTakes()
        {
            var mind = new RailMind();
            int call = 0;
            mind.Answer = _ => ++call == 1
                ? new[]
                  {
                      DecisionContract.ToAction(new UnitOrder("1st Platoon", "SupportByFire", "house", "support", null, null, Array.Empty<Condition>(), ""), Vocab4686)!,
                      DecisionContract.ToAction(new UnitOrder("2nd Platoon", "SupportByFire", "house", "support", null, null, Array.Empty<Condition>(), ""), Vocab4686)!,
                  }
                : Array.Empty<CommanderAction>();
            var s = SeatedCompany(mind);
            var physical = s.Repo.GetComponentRO<AggregateUnit>(s.P.Alpha);   // 1st Platoon split: physical
            physical.Resolution = AggregateResolution.Physical;
            s.Repo.SetComponent(s.P.Alpha, physical);
            Fast(s, heartbeat: 0.5f, snapshot: 60f);
            Run(s, 60);
            var toAlpha = s.P.S.Orders.Last(o => o.Entity.Equals(s.P.Alpha));
            var toBravo = s.P.S.Orders.Last(o => o.Entity.Equals(s.P.Bravo));
            Assert.Equal("Order.SupportByFire", toAlpha.IntentId);                    // the KIND — PlatoonSupportByFire runs it
            Assert.Contains("\"Position\":[60", toAlpha.JsonParams);                  // stands at 'support' (60, 60)
            Assert.Contains("\"Post1\":[100", toAlpha.JsonParams);                    // fires at 'house' (100, 100)
            Assert.Equal("Order.Attack", toBravo.IntentId);                           // stochastic: the generic play reads HoldBack
            Assert.Contains("\"Position\":[60", toBravo.JsonParams);
            Assert.Contains("\"HoldBack\":true", toBravo.JsonParams);
            s.Seats.Dispose();
        }

        /// <summary>
        /// ⭐ CE-4686 ③ — an order its unit REPORTED failed is not "in force": the same order again goes out (once), instead of "already in force — nothing
        /// sent". 📌 Haiku run: the cleared support by fire was re-issued and swallowed as a no-op. 🔴 Red-proof: the failed check dropped ⇒ one order only.
        /// </summary>
        [Fact]
        public void CE4686_AnOrderItsUnitReportedFailed_IsGivenAgain_NotSwallowedAsInForce()
        {
            var mind = new RailMind();
            mind.Answer = _ => new[] { new CommanderAction(CommanderActionKind.Order, "Alpha", "Move", Where: "house") };
            var s = new Seated(mind);
            Fast(s, heartbeat: 0.5f, snapshot: 60f);
            Run(s, 120);
            Assert.Equal(1, s.P.S.Orders.Count(o => o.Entity.Equals(s.P.Alpha) && o.IntentId == "Order.Move"));   // repeats are no-ops
            s.P.Report(s.P.Alpha, new Fdp.Toolkit.Squad.Command.CommandReport
            {
                Kind = Fdp.Toolkit.Squad.Command.ReportKind.Progress, Subtype = (byte)Fdp.Toolkit.Squad.Command.ProgressKind.TaskFailed,
            });
            Run(s, 120);
            Assert.Equal(2, s.P.S.Orders.Count(o => o.Entity.Equals(s.P.Alpha) && o.IntentId == "Order.Move"));   // given again, once
            s.Seats.Dispose();
        }

        /// <summary>
        /// ⭐ CE-4686 — a unit OUT OF THE FIGHT carries no order in force in the snapshot. 📌 Haiku run: a destroyed platoon's SupportByFire stayed listed.
        /// 🔴 Red-proof: the in-the-fight check dropped ⇒ "ALPHA Move" still listed.
        /// </summary>
        [Fact]
        public void CE4686_AUnitOutOfTheFight_CarriesNoOrderInForce()
        {
            var mind = new RailMind();
            int call = 0;
            mind.Answer = _ => ++call == 1 ? new[] { new CommanderAction(CommanderActionKind.Order, "1st Platoon", "Move", Where: "house") } : Array.Empty<CommanderAction>();
            var s = SeatedCompany(mind);
            Fast(s, heartbeat: 0.5f, snapshot: 1f);
            Run(s, 150);
            Assert.Contains("1ST PLATOON Move", s.Items().Last(i => i.Kind == FeedKind.Snapshot).Radio);
            AggregateTestUnits.Make(s.Repo, s.P.Alpha, slots: 9, up: 0, echelon: 1);   // every man down: out of the fight
            Run(s, 120);
            Assert.DoesNotContain("1ST PLATOON Move", s.Items().Last(i => i.Kind == FeedKind.Snapshot).Radio);
            s.Seats.Dispose();
        }

        private static readonly SeatVocabulary Vocab4686 = new("Blue Company", new[] { "1st Platoon", "2nd Platoon" }, new[] { "house", "objective", "support", "start" });
    }
}
