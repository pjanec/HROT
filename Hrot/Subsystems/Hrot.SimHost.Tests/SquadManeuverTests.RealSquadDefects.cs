using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Maneuvers;
using Hrot.AI.Behaviors.Brains;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>⭐ <c>CE-4605</c>…<c>CE-4608</c> (aggregate squad A-0) — real-squad defects found analysing the aggregate squad; each rail is
    /// red-proved by reverting the fix's key line. 📄 docs/designs/aggregate-units/DESIGN_Aggregate_Squad.md §6.</summary>
    public partial class SquadManeuverTests
    {
        /// <summary>⭐ <c>CE-4605</c> — nobody sees the threat and the two LAST slots are dead: the split falls back to the live men by rank,
        /// so the assault element is non-empty and holds only live men (the slot split put both survivors in the base of fire ⇒ nobody
        /// assaulted ⇒ <c>PhaseComplete</c> passed on <c>!any</c> and the play reported Success). ✅ Red-proof: the helper replaced by the
        /// slot split ⇒ the assault is empty.</summary>
        [Fact]
        public void CE4605_SuppressAndManeuver_NobodySeesIt_DeadInTheLastSlots_TheLiveMenStillAssault()
        {
            var s = new Squad(members: 4);
            Down(s, s.Members[2], 0f); Down(s, s.Members[3], 0f);                  // dead
            var enemy = Enemy(s, new Vector3(0f, 60f, 0f));
            s.Pool(enemy, new Vector3(0f, 60f, 0f), reporters: 0);                  // nobody sees him
            s.Order("{}", BehaviorNames.SuppressAndManeuver);
            for (int i = 0; i < 4; i++) s.Tick();
            Assert.Equal(SuppressAndManeuverManeuver.PhaseSuppressing, s.Phase);
            var squad = s.Repo.GetComponentRO<SquadCognitiveState>(s.Commander);
            Assert.Equal(SuppressAndManeuverManeuver.ElementBaseOfFire, squad.Elements.MemberElements[0]);
            Assert.Equal(SuppressAndManeuverManeuver.ElementAssault, squad.Elements.MemberElements[1]);
            Assert.False(s.Finished(out _), "no assault, no Success");
        }

        /// <summary>⭐ <c>CE-4605</c> — the dead in the FIRST slots, everyone live sees him: the assault holds live men only (never a corpse).</summary>
        [Fact]
        public void CE4605_SuppressAndManeuver_DeadInTheFirstSlots_TheAssaultHoldsOnlyLiveMen()
        {
            var s = new Squad(members: 4);
            Down(s, s.Members[0], 0f); Down(s, s.Members[1], 0f);
            var enemy = Enemy(s, new Vector3(0f, 60f, 0f));
            s.Pool(enemy, new Vector3(0f, 60f, 0f), reporters: 0b1100);
            s.Order("{}", BehaviorNames.SuppressAndManeuver);
            for (int i = 0; i < 4; i++) s.Tick();
            var squad = s.Repo.GetComponentRO<SquadCognitiveState>(s.Commander);
            Assert.Equal(SuppressAndManeuverManeuver.ElementBaseOfFire, squad.Elements.MemberElements[2]);
            Assert.Equal(SuppressAndManeuverManeuver.ElementAssault, squad.Elements.MemberElements[3]);
        }
    }
}
