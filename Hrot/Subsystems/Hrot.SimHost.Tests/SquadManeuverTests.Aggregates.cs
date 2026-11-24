using System;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Replication.Components;
using Hrot.CGF.Commander;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4685</c> (DESIGN_LLM_Commander §5d) — the seat at a COMPANY over AGGREGATED platoons: the briefing says what he is and what his units are
    /// (from their classes), the plays are his class's (the one routing table), and a platoon that SPLITS is news while his orders to it still land.
    /// </summary>
    public partial class SquadManeuverTests
    {
        /// <summary>The fixture's commander as a company (TKB 306) over its two units as STOCHASTIC platoons (305, 8 of 9 men up), the built-in TKB.</summary>
        private static Seated SeatedCompany(IMind mind, bool behaviours = true) =>
            new Seated(mind, isBehaviour: behaviours ? KnownBehaviour : null, before: s =>
            {
                var db = new Fdp.Toolkit.Tkb.TkbDatabase();
                Hrot.Map.Common.HrotEnvironment.RegisterBuiltInTkb(db);
                s.Repo.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
                s.Name(s.P.S.Commander, "Blue Company");
                s.Name(s.P.Alpha, "1st Platoon");
                s.Name(s.P.Bravo, "2nd Platoon");
                Tkb(s.Repo, s.P.S.Commander, 306);
                foreach (var p in new[] { s.P.Alpha, s.P.Bravo })
                {
                    Tkb(s.Repo, p, 305);
                    AggregateTestUnits.Make(s.Repo, p, slots: 9, up: 8, echelon: 1);
                }
            });

        /// <summary>A behaviour name the product registers (every <c>BehaviorNames</c> constant) — the registry's answer, as <c>CgfLogicPack</c> passes it.</summary>
        private static bool KnownBehaviour(string name) => Names.Contains(name);
        private static readonly System.Collections.Generic.HashSet<string> Names = typeof(Hrot.Map.Definitions.Behavior.BehaviorNames)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!).ToHashSet();

        /// <summary>The class as the spawner gives it: the TKB identity and the header's DIS type (the routing table reads the header).</summary>
        private static void Tkb(EntityRepository w, Entity e, long type)
        {
            var id = new TkbIdentity { TkbType = (TkbTypeId)type };
            if (w.HasComponent<TkbIdentity>(e)) w.SetComponent(e, id); else w.AddComponent(e, id);
            w.SetDisType(e, w.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>().GetByType(type)!.DisType);
        }

        /// <summary>
        /// ⭐ A-1 — a company seat's briefing says he is a COMPANY commander commanding PLATOONS (their classes, never the hard-coded "platoon / squads"),
        /// and the board gives each aggregated platoon's strength in men.
        /// 🔴 Red-proof: the echelon from the class dropped (always "platoon") ⇒ "a company commander" is missing.
        /// </summary>
        [Fact]
        public void Aggregates_ACompanySeat_IsBriefedAsACompany_OverItsAggregatedPlatoons()
        {
            var s = SeatedCompany(new RailMind());
            Run(s, 30);
            Assert.Equal("company", s.Seat.Echelon);
            Assert.Equal("platoon", s.Seat.Subordinate);
            Assert.Contains("Blue Company, a company commander. You command platoons, never single soldiers", s.Seat.Prefix);
            Assert.Contains("Your platoons: 1st Platoon 2nd Platoon", s.Seat.Prefix);
            Assert.DoesNotContain("squads", s.Seat.Prefix.Split('\n')[0]);
            var board = s.Items().First(i => i.Kind == FeedKind.Snapshot);
            Assert.Contains("aggregated, 8 men", board.Radio);
            Assert.Contains("\"echelon\":\"company\"", board.Json);
            Assert.Contains("\"aggregated\":true", board.Json);
            s.Seats.Dispose();
        }

        /// <summary>
        /// ⭐ A-2 — the plays offered are his CLASS's (the routing table: a company runs <c>CompanyAttack</c> / <c>CompanyDefend</c>), the play he names
        /// becomes his task, and a platoon's play is refused for him.
        /// 🔴 Red-proof: <c>PlaysOf</c> returns the platoon four ⇒ CompanyAttack is "not a play".
        /// </summary>
        [Fact]
        public void Aggregates_ACompanySeat_RunsItsOwnClassesPlay_AndIsRefusedAPlatoonPlay()
        {
            var mind = new RailMind();
            int call = 0;
            mind.Answer = _ => ++call switch
            {
                1 => new[] { new CommanderAction(CommanderActionKind.RunPlay, Order: "PlatoonAttack", Where: "house") },
                2 => new[] { new CommanderAction(CommanderActionKind.RunPlay, Order: "CompanyAttack", Where: "house") },
                _ => Array.Empty<CommanderAction>(),
            };
            var s = SeatedCompany(mind);
            Fast(s, heartbeat: 0.5f, snapshot: 60f);
            Run(s, 200);
            Assert.Contains("CompanyAttack", s.Seat.Plays);
            Assert.Contains("CompanyDefend", s.Seat.Plays);
            Assert.DoesNotContain("PlatoonAttack", s.Seat.Plays);
            Assert.Contains(s.Items(), i => i.Kind == FeedKind.OrderState && i.Radio.Contains("'PlatoonAttack' is not a play of a company"));
            Assert.Contains(s.P.S.Orders, o => o.Entity.Equals(s.P.S.Commander) && o.IntentId == "CompanyAttack");
            Assert.DoesNotContain(s.P.S.Orders, o => o.IntentId == "PlatoonAttack");
            s.Seats.Dispose();
        }

        /// <summary>
        /// ⭐ A-3 — an aggregated platoon that SPLITS (Physical, X-5) is news on the radio, ONCE, and a waking event; it is the same entity, so the seat's
        /// next order to it still lands.
        /// 🔴 Red-proof: the split check dropped from <c>Falls</c> ⇒ no "has split" event.
        /// </summary>
        [Fact]
        public void Aggregates_APlatoonThatSplits_IsNewsOnce_AndTheSeatsNextOrderStillLands()
        {
            var mind = new RailMind();
            bool split = false;
            mind.Answer = b => split && b.New.Any(i => i.Radio.Contains("has split"))
                ? new[] { new CommanderAction(CommanderActionKind.Order, "2nd Platoon", "Move", Where: "start") }
                : Array.Empty<CommanderAction>();
            var s = SeatedCompany(mind);
            Fast(s, heartbeat: 0.5f, snapshot: 60f);
            Run(s, 30);
            Assert.DoesNotContain(s.Items(), i => i.Radio.Contains("has split"));
            var a = s.Repo.GetComponentRO<AggregateUnit>(s.P.Bravo);
            a.Resolution = AggregateResolution.Physical;
            s.Repo.SetComponent(s.P.Bravo, a);
            split = true;
            Run(s, 300);
            var news = s.Items().Where(i => i.Kind == FeedKind.Event && i.Radio.Contains("has split")).ToList();
            Assert.Single(news);
            Assert.True(news[0].Wake);
            Assert.Contains("2ND PLATOON: near the enemy — it has split from an aggregated platoon into its own units", news[0].Radio);
            Assert.Contains("\"event\":\"split\"", news[0].Json);
            Assert.Contains(s.P.S.Orders, o => o.Entity.Equals(s.P.Bravo) && o.IntentId.Contains("Move"));
            s.Seats.Dispose();
        }
    }
}
