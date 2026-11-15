using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Squad.Command;
using Hrot.AI.Behaviors.Brains;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>⭐ <c>CE-2436</c> AU-1 (A-1) — the squad's <c>SupportByFire</c> play (DESIGN_Assault_Under_Fire §5, §7 item 1).</summary>
    public partial class SquadManeuverTests
    {
        private static SuppressParamsJsonDto SuppressOrderTo(Squad s, Fdp.Core.Entity m)
            => System.Text.Json.JsonSerializer.Deserialize<SuppressParamsJsonDto>(
                   s.Orders.Last(o => o.Entity.Equals(m) && o.IntentId == BehaviorNames.Suppress).JsonParams,
                   Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);

        private static string SupportByFireJson(Vector3 position, params Vector3[] posts)
            => System.Text.Json.JsonSerializer.Serialize(new SupportByFireParamsJsonDto
            {
                Position = position,
                Post1 = posts.Length > 0 ? posts[0] : default, Post2 = posts.Length > 1 ? posts[1] : default,
                Post3 = posts.Length > 2 ? posts[2] : default, Post4 = posts.Length > 3 ? posts[3] : default,
            }, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);

        /// <summary>
        /// ⭐⭐ <c>CE-2436</c> AU-1 (A-1) — the base of fire: the squad first HOLDS at its support point (no fire order while it is on the way);
        /// once there, EVERY man is ordered to suppress a REPORTED post — man i on post i mod n, nobody needing to see anyone there (the library
        /// <c>Suppress</c> at the point, no target: 📌 the per-man half is <c>SuppressNodesTests.CE2152_ASuppressorWhoNeverSawHim_…</c> —
        /// <c>FireAtPoint</c> at that very point) — and the squad reports SET once firing. A new order with new posts (the SHIFT) re-tasks every
        /// man onto them. ✅ Red-proofs: the arrival check dropped ⇒ fire orders on the way · man i on post 0 ⇒ the second post unfired ·
        /// the SET dropped ⇒ no Set queued.
        /// </summary>
        [Fact]
        public void CE2436_SupportByFire_HoldsAtThePoint_ThenEveryManFiresOnAReportedPost_ReportsSet_AndShiftsOnOrder()
        {
            var s = new Squad(4);
            var support = new Vector3(6f, 60f, 0f);
            var postA = new Vector3(0f, 180f, 0f);
            var postB = new Vector3(20f, 185f, 0f);
            s.Order(SupportByFireJson(support, postA, postB), BehaviorNames.SupportByFire);
            s.Tick(); s.Tick(); s.Tick();

            foreach (var m in s.Members) Assert.Equal(BehaviorNames.CombatPosture, LastIntent(s, m));   // on the way: hold at the point
            Assert.DoesNotContain(s.Orders, o => o.IntentId == BehaviorNames.Suppress);
            Assert.DoesNotContain(Queued(s, s.Commander), r => r.Kind == ReportKind.Progress && r.Subtype == (byte)ProgressKind.Set);

            for (int i = 0; i < s.Members.Length; i++) s.Teleport(s.Members[i], support + new Vector3(i * 2f - 3f, 0f, 0f));   // arrived
            s.Tick(); s.Tick(); s.Tick();
            for (int i = 0; i < s.Members.Length; i++)
            {
                Assert.Equal(BehaviorNames.Suppress, LastIntent(s, s.Members[i]));
                var o = SuppressOrderTo(s, s.Members[i]);
                var post = i % 2 == 0 ? postA : postB;
                Assert.True(Vector2.Distance(new Vector2(o.X, o.Y), new Vector2(post.X, post.Y)) < 0.01f, $"man {i} fires at ({o.X}, {o.Y}), post {post}");
                Assert.True(o.Target.Ref.IsNone, "a reported post, not a seen man");
            }
            // ⭐ AU-2 (A-8) — SET is "suppression ESTABLISHED": not yet on arrival, only after firing EstablishSeconds (no counted weapon here)
            Assert.DoesNotContain(Queued(s, s.Commander), r => r.Kind == ReportKind.Progress && r.Subtype == (byte)ProgressKind.Set);
            s.Pause(SupportByFireBehavior.EstablishSeconds + 0.1);
            s.Tick();
            Assert.Single(Queued(s, s.Commander), r => r.Kind == ReportKind.Progress && r.Subtype == (byte)ProgressKind.Set);

            int before = s.Orders.Count(o => o.IntentId == BehaviorNames.Suppress);
            s.Tick(); s.Tick();
            Assert.Equal(before, s.Orders.Count(o => o.IntentId == BehaviorNames.Suppress));   // R-2084: no order re-sent while it stands

            var depth = new Vector3(5f, 220f, 0f);                                              // the SHIFT: a new order, new posts
            s.Order(SupportByFireJson(support, depth), BehaviorNames.SupportByFire);
            for (int k = 0; k < 6; k++) s.Tick();
            foreach (var m in s.Members)
            {
                var o = SuppressOrderTo(s, m);
                Assert.True(Vector2.Distance(new Vector2(o.X, o.Y), new Vector2(depth.X, depth.Y)) < 0.01f, $"shifted to ({o.X}, {o.Y})");
            }
        }
    

        /// <summary>
        /// ⭐⭐ <c>CE-2436</c> AU-2 (A-8) — "suppression established" is a CAUSE, not a clock: a base of fire whose men carry counted weapons reports
        /// SET only once it has fired <see cref="SupportByFireBehavior.EstablishSeconds"/> AND spent <see cref="SupportByFireBehavior.EstablishRoundsPerMan"/>
        /// rounds a man since firing began. ✅ Red-proofs: the rounds check dropped ⇒ Set with nothing fired · the time check dropped ⇒ Set at once.
        /// </summary>
        [Fact]
        public void CE2436_SupportByFire_SuppressionIsEstablished_OnlyWhenRoundsWereSpent_NotByTheClockAlone()
        {
            var s = new Squad(3);
            foreach (var m in s.Members)
                s.Repo.AddComponent(m, new Fdp.Toolkit.Combat.Components.WeaponAmmo { Count = 1, Ammo0 = 200, Max0 = 200 });
            var support = new Vector3(4f, 0f, 0f);
            s.Order(SupportByFireJson(support, new Vector3(0f, 150f, 0f)), BehaviorNames.SupportByFire);
            s.Tick(); s.Tick(); s.Tick();
            Assert.Equal(BehaviorNames.Suppress, LastIntent(s, s.Members[0]));   // at the point already: firing
            s.Tick();
            Assert.DoesNotContain(Queued(s, s.Commander), r => r.Kind == ReportKind.Progress && r.Subtype == (byte)ProgressKind.Set);

            s.Pause(SupportByFireBehavior.EstablishSeconds + 0.1);
            s.Tick();
            Assert.DoesNotContain(Queued(s, s.Commander), r => r.Kind == ReportKind.Progress && r.Subtype == (byte)ProgressKind.Set);   // the clock alone

            foreach (var m in s.Members) s.Repo.GetComponentRW<Fdp.Toolkit.Combat.Components.WeaponAmmo>(m).Ammo0 -= SupportByFireBehavior.EstablishRoundsPerMan;
            s.Tick();
            Assert.Single(Queued(s, s.Commander), r => r.Kind == ReportKind.Progress && r.Subtype == (byte)ProgressKind.Set);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2436</c> AU-2 (A-7; DA-12's open half) — the CROSSING SMOKE: a squad going by fire and movement (in contact) has ONE covering man
        /// who carries smoke and is at least Regular throw a smoke grenade toward the threat before the first rush (<c>FireAtPoint</c>, round smoke,
        /// one grenade, ≤ 15 m and at most half-way); once a run; after <c>SmokeSeconds</c> he covers again. ✅ Red-proof: the smoke arm removed
        /// ⇒ every covering man's order is Suppress.
        /// </summary>
        [Fact]
        public void CE2436_FireAndMovement_OneCoveringManThrowsCrossingSmokeTowardTheThreat_OnceBeforeTheFirstRush()
        {
            var s = FmSquad();
            AssaultRiflemen(s);
            s.Order(FmOrder, BehaviorNames.FireAndMovement);
            s.Tick(); s.Tick(); s.Tick();
            var smokes = s.Orders.Where(o => o.IntentId == BehaviorNames.FireAtPoint).ToList();
            Assert.Single(smokes);
            var thrower = smokes[0].Entity;
            Assert.True(thrower.Equals(s.Members[2]) || thrower.Equals(s.Members[3]), "a covering man throws");
            var fire = System.Text.Json.JsonSerializer.Deserialize<FireAtPointParamsJsonDto>(smokes[0].JsonParams, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
            Assert.Equal(1, fire.Round);
            Assert.Equal(1, fire.Rounds);
            var from = s.At(thrower);
            var threat = new Vector3(6f, 80f, 0f);
            Assert.True(Vector2.Distance(new(fire.X, fire.Y), new(threat.X, threat.Y)) < Vector2.Distance(new(from.X, from.Y), new(threat.X, threat.Y)), "toward the threat");
            Assert.InRange(Vector2.Distance(new(from.X, from.Y), new(fire.X, fire.Y)), BreakContactPlay.SmokeMetres - 0.5f, BreakContactPlay.SmokeMetres + 0.5f);

            for (int i = 0; i < (int)(BreakContactPlay.SmokeSeconds / 0.016f) + 3; i++) s.Tick();
            Assert.Equal(BehaviorNames.Suppress, LastIntent(s, thrower));                     // he covers again
            Assert.Single(s.Orders.Where(o => o.IntentId == BehaviorNames.FireAtPoint));     // once a run
        }
    

        /// <summary>
        /// ⭐ <c>CE-2436</c> AU-2 (A-7) — "bounding only BEFORE contact": an attack on an area where nothing is reported (no post) approaches by
        /// bounding overwatch; the same attack with the objective REPORTED closes by fire and movement. ✅ Red-proof: the contact flag forced to 1
        /// ⇒ the unreported attack rushes too.
        /// </summary>
        [Fact]
        public void CE2436_PlatoonAttack_BeforeContactTheAssaultBounds_InContactItClosesByFireAndMovement()
        {
            foreach (bool reported in new[] { false, true })
            {
                var p = new Platoon(squads: 3);
                var o = new Vector3(0f, 200f, 0f);
                if (reported) { p.ReportSpot(p.Bravo, o, 3); p.Ticks(2); }
                Attack(p, o);
                p.Ticks(3); p.S.Pause(PlatoonAttackBehavior.SurveyTimeoutSeconds + 0.5); p.Ticks(4);
                p.ReportSet(p.Bravo); p.Ticks(4);
                Assert.Equal(reported ? 0 : 1, p.OrdersTo(p.Alpha, BehaviorNames.BoundingOverwatch));
                Assert.Equal(reported ? 1 : 0, p.OrdersTo(p.Alpha, BehaviorNames.FireAndMovement));
            }
        }
    }
}
