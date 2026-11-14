using System.Linq;
using System.Numerics;
using Fdp.Core;
using Hrot.AI.Behaviors.Brains;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>⭐ <c>CE-7007</c> (City Battle §3a, §11 A-1 — active defence) — shoot and move between a squad's posts while in contact.</summary>
    public partial class SquadManeuverTests
    {
        /// <summary>
        /// ⭐⭐ <c>CE-7007</c> — a squad told to SHOOT AND MOVE (20 s): with no contact nobody moves however long they stand; with a contact in
        /// the squad's pool every man takes his alternate post within his dwell (20 – 32 s), reporting Displacing — and not all in the same
        /// frame. The same squad WITHOUT the setting stays put (the defence as before). ✅ Red-proof: <c>ShootAndMove</c> not called ⇒ nobody moves.
        /// </summary>
        [Fact]
        public void CE7007_InContact_EachManChangesPostAfterHisDwell_WithoutContactOrSettingNobodyMoves()
        {
            foreach (bool on in new[] { true, false })
            {
                var s = new Squad(members: 3);
                s.Order("{\"Position\":[4,0,0],\"SearchRadius\":25" + (on ? ",\"ShootAndMoveSeconds\":20" : "") + "}", BehaviorNames.SquadDefend);
                s.Tick(); s.Tick(); s.Tick(); s.Tick();
                AnswerAll(s, DefendQuery(s, SquadDefendPlay.WindowSite),
                    new Vector3(0, 5, 0), new Vector3(4, 5, 0), new Vector3(8, 5, 0), new Vector3(0, 9, 0), new Vector3(4, 8, 0), new Vector3(8, 8, 0));
                s.Tick(); s.Tick(); s.Tick();
                var given = s.LastOrders();
                foreach (var m in s.Members) s.Teleport(m, given[m].Objective);
                s.Tick();

                bool Moved(Entity m) => s.Orders.Any(o => o.Entity.Equals(m) && s.LastOrders()[m].Objective != given[m].Objective);
                for (int t = 0; t < 8; t++) { s.Pause(5.0); s.Tick(); s.Tick(); }        // 40 s quiet: nobody changes post
                Assert.DoesNotContain(s.Members, Moved);

                s.Pool(s.Repo.CreateEntity(), new Vector3(4f, 120f, 0f), reporters: 1);   // a contact
                var movedAt = new System.Collections.Generic.Dictionary<Entity, int>();
                for (int t = 0; t < 20; t++)
                {
                    s.Pause(2.0); s.Tick(); s.Tick();
                    foreach (var m in s.Members) if (!movedAt.ContainsKey(m) && Moved(m)) movedAt[m] = t;
                    s.Pool(s.Repo.CreateEntity(), new Vector3(4f, 120f, 0f), reporters: 1);   // still there
                }
                if (!on) { Assert.Empty(movedAt); continue; }
                Assert.Equal(3, movedAt.Count);                                              // every man moved
                Assert.True(movedAt.Values.Distinct().Count() > 1, "not all in the same frame");
                Assert.True(HasProgress(s, Fdp.Toolkit.Squad.Command.ProgressKind.Displacing));
            }
        }
    }
}
