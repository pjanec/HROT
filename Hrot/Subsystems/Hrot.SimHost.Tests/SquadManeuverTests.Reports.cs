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
    }
}
