using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Squad.Command;
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
    }
}
