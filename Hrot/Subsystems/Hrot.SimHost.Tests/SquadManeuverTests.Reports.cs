using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Squad.Command;
using Hrot.AI.Behaviors.Brains;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>⭐ <c>CE-4610</c> — the reports a commander READS now have a SENDER (or the reader is gone): PhaseLine and ObjectiveSecure from
    /// the attacks, the SITREP's ammunition colour.</summary>
    public partial class SquadManeuverTests
    {
        private static Fdp.Toolkit.Squad.Command.CommandReport[] Queued(Squad s, Entity unit)
        {
            if (!s.Repo.HasComponent<ReportOutbox>(unit)) return System.Array.Empty<CommandReport>();
            var q = s.Repo.GetComponentRO<ReportOutbox>(unit).Queued;
            var all = new CommandReport[q.Count];
            for (int i = 0; i < q.Count; i++) all[i] = q.Items[i].Report;
            return all;
        }

        /// <summary>⭐ <c>CE-4610</c> — a company taking objective 1 reports PROGRESS PhaseLine 1 AT it; taking objective 2 (the last) reports
        /// PhaseLine 2 and then ObjectiveSecure at objective 2 — before the consolidation ends. ✅ Red-proof: <c>NextObjective</c>'s report dropped ⇒ red.</summary>
        [Fact]
        public void CE4610_ACompany_ReportsEachPhaseLine_ThenObjectiveSecure()
        {
            var c = new Company();
            Vector3 o1 = new(0f, 600f, 0f), o2 = new(0f, 1000f, 0f);
            CompanyAttack(c, 0f, o1, o2);
            c.Ticks(6);
            Assert.DoesNotContain(Queued(c.S, c.S.Commander), r => r.Kind == ReportKind.Progress && r.Subtype == (byte)ProgressKind.PhaseLine);
            c.Set(c.Second); c.Ticks(5);
            c.Set(c.First); c.Ticks(5);
            var pl1 = Queued(c.S, c.S.Commander).Single(r => r.Kind == ReportKind.Progress && r.Subtype == (byte)ProgressKind.PhaseLine);
            Assert.Equal((byte)1, pl1.Count);
            Assert.True(Near(pl1.At, o1));
            c.Set(c.Second); c.Ticks(5);
            c.Set(c.First); c.Ticks(5);
            var q = Queued(c.S, c.S.Commander);
            Assert.Contains(q, r => r.Kind == ReportKind.Progress && r.Subtype == (byte)ProgressKind.PhaseLine && r.Count == 2 && Near(r.At, o2));
            Assert.Contains(q, r => r.Kind == ReportKind.Progress && r.Subtype == (byte)ProgressKind.ObjectiveSecure && Near(r.At, o2));
        }

        /// <summary>⭐ <c>CE-4610</c> — the SITREP carries the unit's AMMUNITION colour from its men's rounds (≥ 50 % GREEN, ≥ 25 % AMBER, some RED,
        /// none BLACK; no counted weapon ⇒ unknown), and the superior's picture takes it. ✅ Red-proof: the SITREP without <c>Ammo</c> ⇒ the picture
        /// stays unknown ⇒ red.</summary>
        [Fact]
        public void CE4610_TheSitrep_CarriesTheAmmunitionColour()
        {
            var c = new Chain(members: 4);
            Assert.Equal(StatusColour.ColourUnknown, StaffComposerSystem.AmmoOf(c.S.Repo, c.S.Commander));
            void Rounds(int each)
            {
                foreach (var m in c.S.Members)
                {
                    var a = new Fdp.Toolkit.Combat.Components.WeaponAmmo { Count = 1, Ammo0 = each, Max0 = 100, Mag0 = -1, LoadRounds0 = -1 };
                    if (c.S.Repo.HasComponent<Fdp.Toolkit.Combat.Components.WeaponAmmo>(m)) c.S.Repo.SetComponent(m, a); else c.S.Repo.AddComponent(m, a);
                }
            }
            Rounds(60); Assert.Equal(StatusColour.Green, StaffComposerSystem.AmmoOf(c.S.Repo, c.S.Commander));
            Rounds(30); Assert.Equal(StatusColour.Amber, StaffComposerSystem.AmmoOf(c.S.Repo, c.S.Commander));
            Rounds(10); Assert.Equal(StatusColour.Red, StaffComposerSystem.AmmoOf(c.S.Repo, c.S.Commander));
            Rounds(0); Assert.Equal(StatusColour.Black, StaffComposerSystem.AmmoOf(c.S.Repo, c.S.Commander));

            Rounds(30);
            c.S.Repo.AddComponent(c.S.Commander, new ReportingRequirements { SitrepEverySeconds = 15f });
            c.Ticks(2); c.S.Pause(3.1); c.Ticks(2);
            Assert.Equal(StatusColour.Amber, c.Picture.Subordinates.Items[0].Ammo);
        }

        /// <summary>A body-less platoon of <paramref name="slots"/> slots, stochastic, <paramref name="up"/> of them up.</summary>
        private static Entity StochasticPlatoon(Fdp.Core.EntityRepository w, int slots, int up, int broken = 0, Vector3 at = default)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new Fdp.Core.CommandHierarchy.UnitEntity());
            w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            AggregateTestUnits.Make(w, e, slots, up, broken);
            return e;
        }

        /// <summary>⭐⭐ agg-behaviors <c>ST-0 ①</c> (RV-5; design §23.6) — a STOCHASTIC unit has no roster and no bodies, so its strength is its SLOTS: N of M
        /// up reads Present N / Authorized M, the SITREP colour from that share (46/48 GREEN, 20/48 not), a broken-stress slot is in the fight but not
        /// standing, and every slot down is out of the fight and BLACK. ✅ Red-proof: the stochastic branch of <c>UnitStrength.Of</c> removed ⇒ Unknown ⇒ red;
        /// of <c>CombatEffective.IsInTheFight</c> removed ⇒ the wiped-out unit stays in the fight ⇒ red; <c>StaffComposerSystem.StrengthOf</c>'s roster guard ⇒ Unknown ⇒ red.</summary>
        [Fact]
        public void ST0_AStochasticUnit_ReadsItsSlotsAsItsStrength_Colour_AndTheFight()
        {
            using var w = new Fdp.Core.EntityRepository();
            Hrot.SimHost.SimHostComponentRegistry.RegisterAll(w);
            var u = StochasticPlatoon(w, slots: 48, up: 46);
            var s = Fdp.Toolkit.Squad.UnitStrength.Of(w, u);
            Assert.Equal((46f, 48, true), (s.Present, s.Authorized, s.Known));
            Assert.Equal(StatusColour.Green, Fdp.Toolkit.Squad.UnitStrength.ColourOf(w, u));
            Assert.Equal(StatusColour.Green, StaffComposerSystem.StrengthOf(w, u));                 // the SITREP path: no UnitRoster, still read
            Assert.True(Fdp.Toolkit.Squad.CombatEffective.IsInTheFight(w, u));
            Assert.Equal(46, Fdp.Toolkit.Squad.CombatEffective.FightingMembers(w, u));

            AggregateTestUnits.Make(w, u, slots: 48, up: 20);
            Assert.NotEqual(StatusColour.Green, Fdp.Toolkit.Squad.UnitStrength.ColourOf(w, u));
            Assert.Equal(StatusColour.Red, Fdp.Toolkit.Squad.UnitStrength.ColourOf(w, u));         // 0.42 < Amber, still fighting

            // broken stress: in the fight (casualties only) but not standing
            AggregateTestUnits.Make(w, u, slots: 48, up: 40, broken: 12);
            Assert.Equal(40, Fdp.Toolkit.Squad.CombatEffective.FightingMembers(w, u));
            Assert.Equal(28, Fdp.Toolkit.Squad.CombatEffective.StandingMembers(w, u));
            Assert.Equal(40f, Fdp.Toolkit.Squad.UnitStrength.Of(w, u).Present);
            Assert.Equal(28f, Fdp.Toolkit.Squad.UnitStrength.Of(w, u, standing: true).Present);
            Assert.True(Fdp.Toolkit.Squad.CombatEffective.IsInTheFight(w, u));
            Assert.True(Fdp.Toolkit.Squad.CombatEffective.IsStanding(w, u));
            AggregateTestUnits.Make(w, u, slots: 48, up: 40, broken: 40);                           // every man up is broken
            Assert.True(Fdp.Toolkit.Squad.CombatEffective.IsInTheFight(w, u));
            Assert.False(Fdp.Toolkit.Squad.CombatEffective.IsStanding(w, u));

            AggregateTestUnits.Make(w, u, slots: 48, up: 0);                                        // wiped out
            Assert.False(Fdp.Toolkit.Squad.CombatEffective.IsInTheFight(w, u));
            Assert.Equal(StatusColour.Black, Fdp.Toolkit.Squad.UnitStrength.ColourOf(w, u));
            Assert.Equal(StatusColour.Black, StaffComposerSystem.StrengthOf(w, u));

            AggregateTestUnits.Make(w, u, slots: 48, up: 46, resolution: Fdp.Toolkit.Aggregate.AggregateResolution.Physical);
            Assert.False(Fdp.Toolkit.Aggregate.AggregateUnits.IsStochastic(w, u));                  // a physical unit keeps its bodies: no roster ⇒ Unknown, as before
            Assert.False(Fdp.Toolkit.Squad.UnitStrength.Of(w, u).Known);
        }

        /// <summary>⭐⭐ agg-behaviors <c>ST-0 ①</c> — a stochastic PLATOON under a physical COMPANY's roster adds its men (its live slots), not "1 man, composition
        /// unknown"; and its ammunition adds nothing (the contract holds no slot capacity ⇒ <c>AmmoOf</c> stays Unknown for it — finding, STATUS_AggBehaviors §5).
        /// ✅ Red-proof: the stochastic arm of <c>UnitStrength.Sum</c> removed ⇒ 1 man ⇒ red.</summary>
        [Fact]
        public void ST0_AStochasticPlatoon_UnderAPhysicalCompany_AddsItsMen_AndItsAmmoIsUnknown()
        {
            using var w = new Fdp.Core.EntityRepository();
            Hrot.SimHost.SimHostComponentRegistry.RegisterAll(w);
            var platoon = StochasticPlatoon(w, slots: 48, up: 40);
            var company = w.CreateEntity();
            w.AddComponent(company, new Fdp.Core.CommandHierarchy.UnitEntity());
            var roster = new Fdp.Core.CommandHierarchy.UnitRoster();
            roster.SubordinateEntities[roster.Count++] = platoon;
            w.AddComponent(company, roster);
            var s = Fdp.Toolkit.Squad.UnitStrength.Of(w, company);
            Assert.Equal((40f, 48), (s.Present, s.Authorized));
            Assert.Equal(1, Fdp.Toolkit.Squad.CombatEffective.FightingMembers(w, company) > 0 ? 1 : 0);   // the platoon is in the fight
            Assert.Equal(StatusColour.Amber, Fdp.Toolkit.Squad.UnitStrength.ColourOf(w, company));      // 40/48 = 0.83
            Assert.Equal(StatusColour.ColourUnknown, StaffComposerSystem.AmmoOf(w, platoon));
            Assert.Equal(StatusColour.ColourUnknown, StaffComposerSystem.AmmoOf(w, company));
            Assert.Equal(0L, StaffComposerSystem.RoundsOf(w, company, out long max)); Assert.Equal(0L, max);
        }

        /// <summary>⭐ agg-behaviors <c>ST-0 ①</c> — the stochastic readers allocate NOTHING in the steady state (R-2084: 0 B per call). ✅ Red-proof: a
        /// <c>new</c> / LINQ in <c>AggregateUnits.Slots</c> ⇒ bytes &gt; 0 ⇒ red.</summary>
        [Fact]
        public void ST0_TheStochasticReaders_Allocate_ZeroBytes()
        {
            using var w = new Fdp.Core.EntityRepository();
            Hrot.SimHost.SimHostComponentRegistry.RegisterAll(w);
            var platoon = StochasticPlatoon(w, slots: 48, up: 40, broken: 5);
            var company = w.CreateEntity();
            w.AddComponent(company, new Fdp.Core.CommandHierarchy.UnitEntity());
            var roster = new Fdp.Core.CommandHierarchy.UnitRoster();
            roster.SubordinateEntities[roster.Count++] = platoon;
            w.AddComponent(company, roster);
            long Run()
            {
                long x = 0;
                x += (long)Fdp.Toolkit.Squad.UnitStrength.Of(w, platoon, standing: true).Present;
                x += (long)Fdp.Toolkit.Squad.UnitStrength.Of(w, company).Present;
                x += Fdp.Toolkit.Squad.CombatEffective.IsInTheFight(w, platoon) ? 1 : 0;
                x += Fdp.Toolkit.Squad.CombatEffective.StandingMembers(w, platoon);
                x += (int)StaffComposerSystem.StrengthOf(w, platoon);
                x += StaffComposerSystem.RoundsOf(w, company, out _);
                return x;
            }
            for (int i = 0; i < 5; i++) Run();                                  // warm
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) Run();
            Assert.Equal(0L, System.GC.GetAllocatedBytesForCurrentThread() - before);
        }

        /// <summary>⭐ <c>CE-4615</c> — a COMPANY asks every platoon it orders for a SITREP every 15 s (the one <c>CommandOrders.AskSitrep</c>), so its
        /// LOST / relief guards can read a block's colour. 📐 Live on cc-battalion-collapse: a block wiped out never read Black ⇒ no counter-attack.
        /// ✅ Red-proof: the company's AskSitrep calls removed ⇒ no requirement on the platoons ⇒ red.</summary>
        [Fact]
        public void CE4615_ACompany_AsksEveryPlatoonItOrders_ForASitrep()
        {
            var c = new Company();
            c.S.Order("{\"Position\":" + Pt(new Vector3(0f, 0f, 0f)) + ",\"Threat\":" + Pt(new Vector3(0f, 500f, 0f)) + "}", BehaviorNames.CompanyDefend);
            var reporting = new ReportingRequirementsSystem();
            for (int i = 0; i < 6; i++) { c.Ticks(1); reporting.Execute(c.S.Repo, 0.016f); }
            foreach (var platoon in new[] { c.First, c.Second, c.Third })
            {
                Assert.True(c.S.Repo.HasComponent<ReportingRequirements>(platoon), "the platoon was asked to report");
                Assert.Equal(15f, (float)c.S.Repo.GetComponentRO<ReportingRequirements>(platoon).SitrepEverySeconds);
            }
        }
    }
}
