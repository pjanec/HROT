using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Squad.Command;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4686</c> ① (HQ-3b; Command Chain §8d — a user-authorised cross-lane edit) — a commander of UNITS relays its subordinates' ground
    /// SPOTs up one level: merged per group, the freshest speaking for it with its own observation time, at most two groups, and never pushing
    /// out the support elements (HQ-3's reason) of a full superior picture.
    /// </summary>
    public partial class SquadManeuverTests
    {
        /// <summary>The fixture's members as SQUADS (commanders, a roster each — empty here): the platoon then commands units, as in a real platoon.</summary>
        private static (Platoon P, Entity Hq, StaffComposerSystem Composer) UnitsUnderHq()
        {
            var (p, hq, composer) = UnderHq();
            for (int i = 0; i < p.S.Members.Length; i++)
                if (!p.S.Repo.HasComponent<Fdp.Core.CommandHierarchy.UnitRoster>(p.S.Members[i])) p.S.Repo.AddComponent(p.S.Members[i], new Fdp.Core.CommandHierarchy.UnitRoster());
            return (p, hq, composer);
        }

        private static void SpotFrom(Platoon p, Entity from, Vector3 at, byte count, float ago)
            => p.Report(from, new CommandReport { Kind = ReportKind.Spot, At = at, Count = count, Size = StaffComposerSystem.SizeOf(count) }, ago);

        private static ReportedElement[] ElementsOf(in CommandPicture pic)
        {
            var list = new ReportedElement[pic.Elements.Count];
            for (int i = 0; i < list.Length; i++) list[i] = pic.Elements.Items[i];
            return list;
        }

        /// <summary>
        /// ⭐ Two squads report ONE enemy (20 m apart): the platoon relays ONE group up — the fresher report's place and observation time, the larger
        /// count — as its own report (the source the superior keeps is the platoon).
        /// 🔴 Red-proof: <c>RelayGround</c> not called ⇒ the HQ holds nothing.
        /// </summary>
        [Fact]
        public void CE4686_APlatoonRelaysItsSquadsSpotUp_AsOneGroup_WithItsOwnAge()
        {
            var (p, hq, composer) = UnitsUnderHq();
            SpotFrom(p, p.Alpha, new Vector3(0f, 300f, 0f), 3, ago: 6f);
            SpotFrom(p, p.Bravo, new Vector3(20f, 310f, 0f), 5, ago: 2f);
            Run(p, composer, 3); p.S.Pause(3.1); Run(p, composer, 3);
            var e = Assert.Single(ElementsOf(PictureOf(p, hq)));
            Assert.True(Vector3.Distance(e.At, new Vector3(20f, 310f, 0f)) < 0.01f, $"{e.At}");   // the fresher report speaks
            Assert.Equal(5, e.Count);                                                                // the larger count
            Assert.Equal(CommandPictureOps.NetIdOf(p.S.Repo, p.S.Commander), e.FromNetId);        // the platoon reported it
            Assert.True(CommandPictureOps.Now(p.S.Repo) - e.ObservedAt > 3f, "it ages from the squad's observation, not the relay");
        }

        /// <summary>
        /// ⭐ Three enemy groups: only the two FRESHEST go up (HQ-3: a superior's eight slots never fill with one sender's infantry); a group re-reported
        /// in place is not re-sent before its refresh.
        /// 🔴 Red-proof: <c>MaxGroundRelays</c> 3 ⇒ three elements.
        /// </summary>
        [Fact]
        public void CE4686_AtMostTwoGroups_TheFreshest_AreRelayed()
        {
            var (p, hq, composer) = UnitsUnderHq();
            SpotFrom(p, p.Alpha, new Vector3(0f, 300f, 0f), 2, ago: 30f);
            SpotFrom(p, p.Alpha, new Vector3(300f, 300f, 0f), 2, ago: 10f);
            SpotFrom(p, p.Bravo, new Vector3(600f, 300f, 0f), 2, ago: 20f);
            Run(p, composer, 3); p.S.Pause(3.1); Run(p, composer, 3);
            var at = ElementsOf(PictureOf(p, hq)).Select(x => x.At.X).OrderBy(x => x).ToArray();
            Assert.Equal(new[] { 300f, 600f }, at);
        }

        /// <summary>
        /// ⭐ A superior's FULL picture: a relayed ground element evicts the oldest GROUND element, never an IndirectFire / AirDefence one; with only
        /// support elements left, the ground report is dropped.
        /// 🔴 Red-proof: the eviction's support guard dropped ⇒ the oldest support element goes.
        /// </summary>
        [Fact]
        public void CE4686_AFullPicture_AGroundReportNeverEvictsASupportElement()
        {
            var (p, hq, composer) = UnitsUnderHq();
            var repo = p.S.Repo;
            if (!repo.IsComponentTypeRegistered<CommandPicture>()) repo.RegisterComponent<CommandPicture>();
            var full = new CommandPicture();
            for (int i = 0; i < CommandPicture.MaxElements; i++)
                full.Elements.Items[full.Elements.Count++] = new ReportedElement
                {
                    At = new Vector3(5000f + i * 300f, 0f, 0f), Count = 1, FromNetId = 900 + i, ObservedAt = (SimInstant)(i == 0 ? 1.0 : 0.5),   // the mortars are the OLDEST
                    Equipment = i == 0 ? ElementEquipment.NoEquipment : ElementEquipment.IndirectFire,   // one old ground element, seven mortars
                };
            if (repo.HasComponent<CommandPicture>(hq)) repo.SetComponent(hq, full); else repo.AddComponent(hq, full);
            SpotFrom(p, p.Alpha, new Vector3(0f, 300f, 0f), 2, ago: 1f);
            Run(p, composer, 3); p.S.Pause(3.1); Run(p, composer, 3);
            var els = ElementsOf(PictureOf(p, hq));
            Assert.Equal(CommandPicture.MaxElements, els.Length);
            Assert.Equal(7, els.Count(x => x.Equipment == ElementEquipment.IndirectFire));          // every mortar kept
            Assert.Contains(els, x => x.At.X == 0f);                                                // the relayed group replaced the old ground one

            // now only support elements: the next ground group is dropped, not pushed in
            var pic = repo.GetComponentRO<CommandPicture>(hq);
            for (int i = 0; i < pic.Elements.Count; i++) pic.Elements.Items[i].Equipment = ElementEquipment.IndirectFire;
            repo.SetComponent(hq, pic);
            SpotFrom(p, p.Bravo, new Vector3(900f, 300f, 0f), 2, ago: 0f);
            p.S.Pause(16.0);
            Run(p, composer, 3); p.S.Pause(3.1); Run(p, composer, 3);
            var after = ElementsOf(PictureOf(p, hq));
            Assert.Equal(CommandPicture.MaxElements, after.Length);
            Assert.DoesNotContain(after, x => x.At.X == 900f);                                       // the new ground group was dropped
            Assert.True(after.Count(x => x.Equipment == ElementEquipment.IndirectFire) >= 7, "no support element was pushed out");   // (the platoon's own refresh of its group replaces that one in place)
        }
    }
}
