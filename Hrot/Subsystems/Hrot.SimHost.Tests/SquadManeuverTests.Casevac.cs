using System.Numerics;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Maneuvers;
using Hrot.AI.Behaviors.Brains;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>⭐ <c>CE-7009</c> (City Battle §3a, A-3) — casualty EVACUATION: the care play carries the man to a collection point back from the fight.</summary>
    public partial class SquadManeuverTests
    {
        /// <summary>
        /// ⭐⭐ <c>CE-7009</c> — <c>CasualtyCare { evacuateMetres: 150 }</c>: no cover query is asked; once the coverers have fired the medic carries the
        /// man to the point 150 m from him straight AWAY from the threat (the enemy at (0, 80) ⇒ south), and the man lying there ends the drill.
        /// Without it the drill is unchanged (the cover query — <c>CE2152_CasualtyCare_TheOthersCover_…</c>). ✅ Red-proof: the evacuation arm
        /// dropped ⇒ the cover query is asked ⇒ red.
        /// </summary>
        [Fact]
        public void CE7009_CasualtyCare_Evacuate_CarriesHim150mAwayFromTheThreat_NoCoverQuery()
        {
            var (s, casualty, medic, _) = CareSquad();
            var from = s.At(casualty);
            s.Order("{\"evacuateMetres\":150}", BehaviorNames.CasualtyCare);
            s.Tick(); s.Tick(); s.Tick();
            Assert.Equal(CasualtyCareManeuver.ManeuverKind, s.Kind);
            Assert.True(CoverQuery(s).IsNull, "no cover query: the point is the collection point");
            Fire(s, s.Members[0]); Fire(s, s.Members[3]);
            for (int i = 0; i < 4; i++) s.Tick();
            Assert.Equal(BehaviorNames.CarryCasualty, LastIntent(s, medic));
            var point = SquadDrillTrace.Of(s.Repo, s.Commander).Orders[2].Point;
            var away = Vector2.Normalize(new Vector2(from.X, from.Y) - new Vector2(0f, 80f));
            var expected = new Vector2(from.X, from.Y) + away * 150f;
            Assert.True(Vector2.Distance(new Vector2(point.X, point.Y), expected) < 0.5f, $"the collection point {point}, expected {expected}");

            s.Teleport(casualty, point + new Vector3(1f, 0f, 0f));
            s.Tick(); s.Tick();
            Assert.True(s.Finished(out var result));
            Assert.Equal(Fbt.NodeStatus.Success, result);
        }
    }
}
