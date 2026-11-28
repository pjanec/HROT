using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Squad.Command;
using Fdp.Toolkit.Spatial.Eqs;
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

        /// <summary>⭐ <c>CE-8319</c> — the base of fire opens fire once its men are ON THEIR POSTS: answer its post plan's asks (windows and roof
        /// with nothing, cover with the points where its men stand) and tick until every man is ordered to fire, or <paramref name="ticks"/> pass.
        /// The surveys nobody answers here (frames, the gun fan) settle by their own timeout.</summary>
        private static void OnPosts(Squad s, int ticks = 120)
        {
            string? Intent(Fdp.Core.Entity m) => s.Orders.LastOrDefault(o => o.Entity.Equals(m))?.IntentId;   // none yet ⇒ null
            for (int k = 0; k < ticks; k++)
            {
                if (s.Members.All(m => Intent(m) == BehaviorNames.Suppress)) return;
                // cover 9 m apart round the squad's middle (the plan keeps its posts 8 m apart, then 5)
                var mid = s.Members.Select(m => (Vector3)s.Repo.GetComponentRO<Fdp.Core.SimTransform>(m).Position).Aggregate(Vector3.Zero, (a, b) => a + b) / s.Members.Length;
                var spaced = Enumerable.Range(0, s.Members.Length).Select(i => mid + new Vector3((i - (s.Members.Length - 1) * 0.5f) * 9f, 2f, 0f)).ToArray();
                var c = EqsChildSensor.Find(s.Repo, s.Commander, SquadDefendPlay.CoverSite);
                if (!c.IsNull && !s.Repo.GetComponentRO<EqsCognitiveBuffer>(c).IsReady) AnswerAll(s, c, spaced);
                foreach (int site in new[] { SquadDefendPlay.WindowSite, SquadDefendPlay.RoofSite })
                {
                    var a = EqsChildSensor.Find(s.Repo, s.Commander, site);
                    if (!a.IsNull && !s.Repo.GetComponentRO<EqsCognitiveBuffer>(a).IsReady) AnswerAll(s, a);
                }
                s.Pause(0.25);
                s.Tick();
                // each man walks onto the post he was ordered to (the test has no locomotion)
                foreach (var m in s.Members)
                    if (Intent(m) == BehaviorNames.CombatPosture) s.Teleport(m, PostureObjective(s, m));
            }
        }

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
            OnPosts(s);   // ⭐ CE-8319 — on its posts (planned at the point): it fires
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
        /// ⭐ <c>CE-5051</c> — a base of fire still MARCHING to a far support point does not open fire short of it on a clock: 90 s on the way, gaining
        /// ground, no fire order; once it gains nothing for <see cref="SupportByFireBehavior.StallSeconds"/> (it cannot get nearer) it fires from where it
        /// is. 📐 stechovice-battle: stopped 265 m short by the old 60 s timeout. 🔴 Red-proof: the clock back ⇒ fire orders at 60 s on the way.
        /// </summary>
        [Fact]
        public void CE5051_SupportByFire_StillMarching_DoesNotFireShortOfThePoint_AStuckOneDoes()
        {
            var s = new Squad(4);
            var support = new Vector3(0f, 300f, 0f);
            s.Order(SupportByFireJson(support, new Vector3(0f, 420f, 0f)), BehaviorNames.SupportByFire);
            s.Tick(); s.Tick(); s.Tick();
            for (int k = 1; k <= 9; k++)   // 90 s, 25 m nearer each 10 s
            {
                for (int i = 0; i < s.Members.Length; i++) s.Teleport(s.Members[i], new Vector3(i * 2f - 3f, 25f * k, 0f));
                s.Pause(10.0);
                s.Tick();
            }
            Assert.DoesNotContain(s.Orders, o => o.IntentId == BehaviorNames.Suppress);   // still marching: no fire short of the point
            s.Pause(SupportByFireBehavior.StallSeconds + 0.5);                              // stuck 75 m short
            s.Tick(); s.Tick();
            Assert.Contains(s.Orders, o => o.IntentId == BehaviorNames.Suppress);          // it fires from where it is
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
            OnPosts(s);   // ⭐ CE-8319 — at the point already: on its posts, it fires
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
        /// ⭐⭐ <c>CE-4667</c> (D-7, user-approved) exit (a) — a man of the base of fire whose rounds are gone is no base of fire: he is re-ordered to
        /// hold with return fire where he is (<c>CombatPosture</c>), the others keep suppressing. ✅ Red-proof: the empty check dropped ⇒ he keeps
        /// his Suppress order ⇒ red.
        /// </summary>
        [Fact]
        public void CE4667_AManOfTheBaseOfFire_WithNoRoundsLeft_HoldsWhereHeIs_TheRestFire()
        {
            var s = new Squad(3);
            foreach (var m in s.Members)
                s.Repo.AddComponent(m, new Fdp.Toolkit.Combat.Components.WeaponAmmo { Count = 1, Ammo0 = 200, Max0 = 200 });
            s.Order(SupportByFireJson(new Vector3(4f, 0f, 0f), new Vector3(0f, 150f, 0f)), BehaviorNames.SupportByFire);
            OnPosts(s);   // ⭐ CE-8319 — at the point already: on its posts, it fires
            Assert.Equal(BehaviorNames.Suppress, LastIntent(s, s.Members[0]));
            s.Repo.GetComponentRW<Fdp.Toolkit.Combat.Components.WeaponAmmo>(s.Members[0]).Ammo0 = 0;   // out of ammo
            s.Tick(); s.Tick();
            Assert.Equal(BehaviorNames.CombatPosture, LastIntent(s, s.Members[0]));
            Assert.Equal(BehaviorNames.Suppress, LastIntent(s, s.Members[1]));
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4667</c> (D-7, user-approved) exit (b) — when every man the squad knows at its posts is DOWN for
        /// <see cref="SupportByFireBehavior.TargetsDownSeconds"/>, the base of fire stops firing: every man holds where he is and the squad reports
        /// PROGRESS TargetsDown (the platoon decides what is next); one of them back in the fight ⇒ the fire resumes. A post with nobody known
        /// near it keeps the blind fire (the first rail of this file). ✅ Red-proofs: the stop dropped ⇒ still Suppress ⇒ red; the resume dropped ⇒
        /// still holding ⇒ red.
        /// </summary>
        [Fact]
        public void CE4667_TheBaseOfFire_StopsWhenEveryKnownManAtItsPostsIsDown_AndResumesIfOneGetsUp()
        {
            var s = new Squad(3);
            var post = new Vector3(0f, 150f, 0f);
            var enemies = s.PoolMany(2, post);
            foreach (var e in enemies) s.Repo.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = 100f, Max = 100f });
            s.Order(SupportByFireJson(new Vector3(4f, 0f, 0f), post), BehaviorNames.SupportByFire);
            OnPosts(s);   // ⭐ CE-8319 — at the point already: on its posts, it fires
            Assert.Equal(BehaviorNames.Suppress, LastIntent(s, s.Members[0]));
            s.Pause(SupportByFireBehavior.TargetsDownSeconds + 1); s.Tick(); s.Tick();
            Assert.Equal(BehaviorNames.Suppress, LastIntent(s, s.Members[0]));                       // they are alive: fire on

            foreach (var e in enemies) s.Repo.GetComponentRW<Fdp.Toolkit.Combat.Components.Health>(e).Current = 0f;
            s.Tick(); s.Pause(SupportByFireBehavior.TargetsDownSeconds + 1); s.Tick(); s.Tick();
            foreach (var m in s.Members) Assert.Equal(BehaviorNames.CombatPosture, LastIntent(s, m));   // every known man down: hold
            Assert.Single(Queued(s, s.Commander), r => r.Kind == ReportKind.Progress && r.Subtype == (byte)ProgressKind.TargetsDown);

            s.Repo.GetComponentRW<Fdp.Toolkit.Combat.Components.Health>(enemies[0]).Current = 50f;      // one gets up
            s.Tick(); s.Tick();
            Assert.Equal(BehaviorNames.Suppress, LastIntent(s, s.Members[0]));
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
    

        /// <summary>The company's queued FireSupport REQUESTs (not yet sent).</summary>
        private static System.Collections.Generic.List<CommandReport> FireRequests(Squad s)
        {
            var list = new System.Collections.Generic.List<CommandReport>();
            if (!s.Repo.HasComponent<ReportOutbox>(s.Commander)) return list;
            var q = s.Repo.GetComponentRO<ReportOutbox>(s.Commander).Queued;
            for (int i = 0; i < q.Count; i++)
                if (q.Items[i].Report.Kind == ReportKind.Request && q.Items[i].Report.Subtype == (byte)RequestKind.FireSupport) list.Add(q.Items[i].Report);
            return list;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2436</c> AU-3 (A-5) — the company's indirect fire: as the assault goes in it asks a SUSTAINED HE mission
        /// (<see cref="CompanyAttackBehavior.SustainedRounds"/> rounds a gun) on the REPORTED posts (their centre); at half the deadline its smoke is a
        /// SCREEN <see cref="CompanyAttackBehavior.ScreenMetres"/> from the objective toward where it came from — never on the objective; objective 1
        /// taken ⇒ the fire SHIFTS to objective 2 (sustained). ✅ Red-proofs: the rounds not passed ⇒ Count 1 · the screen on the objective ⇒ the
        /// screen point check · the shift dropped ⇒ no request at objective 2.
        /// </summary>
        [Fact]
        public void CE2436_CompanyAttack_SustainedFireOnThePosts_TheSmokeScreensTheCrossing_TheFireShiftsBeyond()
        {
            var c = new Company();
            Vector3 o1 = new(0f, 600f, 0f), o2 = new(0f, 1000f, 0f);
            var postA = new Vector3(10f, 610f, 0f);
            var postB = new Vector3(-10f, 590f, 0f);
            c.P.ReportSpot(c.Second, postA, 2);
            c.P.ReportSpot(c.Third, postB, 2);   // another reporter: one reporter's near spots fold into one element
            c.Ticks(2);
            CompanyAttack(c, 100f, o1, o2);
            c.Ticks(6);
            var support = c.Last<PlatoonSupportByFireParamsJsonDto>(c.Second, BehaviorNames.PlatoonSupportByFire);
            Assert.True(Near(support.Post1, postA) || Near(support.Post1, postB), $"the base of fire on a reported post ({support.Post1})");
            Assert.True(Near(support.Post2, postA) || Near(support.Post2, postB), $"and on the other ({support.Post2})");

            c.Set(c.Second); c.Ticks(5);
            var he = FireRequests(c.S).Single(r => (r.Equipment & ElementEquipment.Smoke) == 0);
            Assert.Equal(CompanyAttackBehavior.SustainedRounds, he.Count);
            Assert.True(Near(he.At, (postA + postB) * 0.5f), $"sustained on the posts' centre ({he.At})");

            c.Over(55);
            var smoke = FireRequests(c.S).Single(r => (r.Equipment & ElementEquipment.Smoke) != 0);
            Assert.True(Vector2.Distance(new(smoke.At.X, smoke.At.Y), new(o1.X, o1.Y)) > CompanyAttackBehavior.ScreenMetres - 1f, $"not on the objective ({smoke.At})");
            Assert.True(smoke.At.Y < o1.Y, "between the objective and where the assault comes from");

            c.Set(c.First); c.Ticks(5);   // objective 1 taken
            Assert.Contains(FireRequests(c.S), r => (r.Equipment & ElementEquipment.Smoke) == 0 && Near(r.At, o2) && r.Count == CompanyAttackBehavior.SustainedRounds);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2436</c> AU-3 (A-5) — a FireSupport REQUEST asking N rounds is answered by the battery with a SUSTAINED mission: its strike
        /// carries <c>Rounds</c> = N (a plain request keeps the battery's default). ✅ Red-proof: <c>Rounds</c> not passed ⇒ 0.
        /// </summary>
        [Fact]
        public void CE2436_ASustainedRequest_IsFiredAsASustainedMission()
        {
            var p = Headquarters();
            var battery = p.S.Members[2];
            var street = new Vector3(0f, 1200f, 0f);
            p.Report(p.S.Members[0], new CommandReport
            {
                Kind = ReportKind.Request, Subtype = (byte)RequestKind.FireSupport, At = street, Count = 8, Equipment = ElementEquipment.Smoke,
            });
            p.Ticks(4);
            var strike = System.Text.Json.JsonSerializer.Deserialize<Hrot.AI.Behaviors.Orders.StrikeOrder>(
                p.S.Orders.Last(o => o.Entity.Equals(battery) && o.IntentId == StrikeIntent).JsonParams, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
            Assert.Equal(8, strike.Rounds);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2436</c> AU-3 (A-1 one level up) — a PLATOON's base of fire: every squad is ordered <c>SupportByFire</c> on the same posts, its
        /// point spread <see cref="PlatoonSupportByFireBehavior.SpreadMetres"/> across the line of fire; the platoon reports SET only once EVERY
        /// squad has (each squad's SET = its suppression established). ✅ Red-proofs: Set on the first squad ⇒ Set with one squad · no spread ⇒
        /// both squads on one point.
        /// </summary>
        [Fact]
        public void CE2436_PlatoonSupportByFire_EverySquadFiresOnThePosts_Spread_SetWhenAllAre()
        {
            var p = new Platoon(squads: 2);
            var at = new Vector3(0f, 100f, 0f);
            var post = new Vector3(0f, 300f, 0f);
            p.S.Order(System.Text.Json.JsonSerializer.Serialize(new PlatoonSupportByFireParamsJsonDto { Position = at, Post1 = post },
                Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed), BehaviorNames.PlatoonSupportByFire);
            p.Ticks(4);
            var a = OrderTo<SupportByFireParamsJsonDto>(p, p.Alpha, BehaviorNames.SupportByFire);
            var b = OrderTo<SupportByFireParamsJsonDto>(p, p.Bravo, BehaviorNames.SupportByFire);
            Assert.True(Vector2.Distance(new(a.Post1.X, a.Post1.Y), new(post.X, post.Y)) < 0.01f);
            Assert.True(Vector2.Distance(new(b.Post1.X, b.Post1.Y), new(post.X, post.Y)) < 0.01f);
            Assert.InRange(Vector2.Distance(new(a.Position.X, a.Position.Y), new(b.Position.X, b.Position.Y)),
                           PlatoonSupportByFireBehavior.SpreadMetres - 0.5f, PlatoonSupportByFireBehavior.SpreadMetres + 0.5f);   // spread across
            Assert.InRange(System.MathF.Abs(a.Position.Y - at.Y), 0f, 0.5f);                                                      // on the line, not toward the posts

            p.ReportSet(p.Alpha); p.Ticks(3);
            Assert.DoesNotContain(Queued(p.S, p.S.Commander), r => r.Kind == ReportKind.Progress && r.Subtype == (byte)ProgressKind.Set);
            p.ReportSet(p.Bravo); p.Ticks(3);
            Assert.Single(Queued(p.S, p.S.Commander), r => r.Kind == ReportKind.Progress && r.Subtype == (byte)ProgressKind.Set);
        }
    

        /// <summary>
        /// ⭐⭐ <c>CE-2436</c> AU-4 (A-4) — a VEHICLE in the company (here slot 0, where a platoon would assault) never takes a platoon's role: it is
        /// ordered <c>VehicleSupportByFire</c> to a stand-off point <see cref="CompanyAttackBehavior.VehicleSpreadMetres"/> beside the base of fire's,
        /// never forward to the objective — still there at the consolidation; the platoons take assault / support / reserve in order, and the attack
        /// ends Success without a Set from the vehicle. ✅ Red-proofs: the vehicle check dropped ⇒ the vehicle is ordered PlatoonAttack · AllSet
        /// waiting on it ⇒ the run never ends.
        /// </summary>
        [Fact]
        public void CE2436_CompanyAttack_AVehicleSupportsByFireFromTheBaseOfFiresLine_NeverAssaults()
        {
            var c = new Company(platoons: 4);
            var ifv = c.S.Members[0];
            if (!c.S.Repo.IsComponentTypeRegistered<global::CarKinem.Core.VehicleState>()) c.S.Repo.RegisterComponent<global::CarKinem.Core.VehicleState>();
            c.S.Repo.AddComponent(ifv, new global::CarKinem.Core.VehicleState());
            Fdp.Core.Entity assault = c.S.Members[1], support = c.S.Members[2], reserve = c.S.Members[3];
            var o = new Vector3(0f, 600f, 0f);
            CompanyAttack(c, 0f, o);
            c.Ticks(6);
            var basePoint = c.Last<PlatoonSupportByFireParamsJsonDto>(support, BehaviorNames.PlatoonSupportByFire).Position;
            var v = c.Last<VehicleSupportByFireParamsJsonDto>(ifv, BehaviorNames.VehicleSupportByFire);
            Assert.InRange(Vector2.Distance(new(v.Position.X, v.Position.Y), new(basePoint.X, basePoint.Y)),
                           CompanyAttackBehavior.VehicleSpreadMetres - 0.5f, CompanyAttackBehavior.VehicleSpreadMetres + 0.5f);
            Assert.InRange(System.MathF.Abs(v.Position.Y - basePoint.Y), 0f, 0.5f);   // beside it on the line, not toward the objective

            c.Set(support); c.Ticks(5);
            Assert.Equal(1, c.OrdersTo(assault, BehaviorNames.PlatoonAttack));
            Assert.Equal(0, c.OrdersTo(ifv, BehaviorNames.PlatoonAttack));
            c.Set(assault); c.Ticks(5);   // taken: consolidate
            Assert.Equal(BehaviorNames.VehicleSupportByFire, c.S.Orders.Last(x => x.Entity.Equals(ifv)).IntentId);
            c.Set(assault); c.Set(support); c.Set(reserve);
            Assert.Equal(Fbt.NodeStatus.Success, c.RunToEnd());
        }
    

        /// <summary>
        /// ⭐⭐ <c>CE-2436</c> — the SOPs spare a BASE OF FIRE as they spare a defence: a platoon running <c>PlatoonSupportByFire</c> is not paused by
        /// its SOP's <c>PlatoonHold</c> on a reported enemy (its squads are not ordered to hold), and a squad running <c>SupportByFire</c> is not
        /// paused by its SOP's <c>ReactToContact</c>. 📌 Live (cc-battalion probe): the hold paused the company's base of fire 16 s after it began
        /// and it never fired again. ✅ Red-proofs: <c>Supporting</c> dropped from <c>PlatoonDefendBehavior.NotDefending</c> ⇒ the platoon holds ·
        /// from <c>SquadDefendBehavior.NotDefending</c> ⇒ the squad reacts.
        /// </summary>
        [Fact]
        public void CE2436_TheSops_DoNotPauseABaseOfFire_OnAContact()
        {
            var p = new Platoon(squads: 3);
            p.S.StartSop("BasicPlatoonSop");
            var o = new Vector3(0f, 200f, 0f);
            p.S.Order(System.Text.Json.JsonSerializer.Serialize(new PlatoonSupportByFireParamsJsonDto { Position = new Vector3(0f, 60f, 0f), Post1 = o },
                Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed), BehaviorNames.PlatoonSupportByFire);
            p.Ticks(4);
            Assert.Equal(BehaviorNames.PlatoonSupportByFire, p.S.TaskName);
            p.ReportSpot(p.Bravo, o, 3);
            p.Ticks(8);
            Assert.Equal(BehaviorNames.PlatoonSupportByFire, p.S.TaskName);
            Assert.False(p.S.Repo.HasComponent<Fdp.Toolkit.Behavior.Components.PausedTask>(p.S.Commander));
            Assert.Equal(0, p.OrdersTo(p.Alpha, "Order.Hold"));

            var s = new Squad(4);
            s.Order(SupportByFireJson(new Vector3(6f, 0f, 0f), new Vector3(0f, 150f, 0f)), BehaviorNames.SupportByFire);
            s.StartSop("BasicSquadSop");
            for (int i = 0; i < 6; i++) s.Tick();
            Assert.Equal(BehaviorNames.SupportByFire, s.TaskName);
            var enemy = Enemy(s, new Vector3(0f, 60f, 0f));
            s.Pool(enemy, new Vector3(0f, 60f, 0f), reporters: 0b0011);
            s.Sense(Fdp.Toolkit.Perception.Events.SensorChange.SquadContact);
            s.Sense(Fdp.Toolkit.Perception.Events.SensorChange.SquadUnderFire);
            for (int i = 0; i < SopReactionFrames; i++) s.Tick();
            Assert.Equal(BehaviorNames.SupportByFire, s.TaskName);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2436</c> AU-7 (A-10c, R-2110) — the squad under a DRONE: its drone gunners — the anti-drone man (his shotgun) and the automatic
        /// rifleman (his M249) — are sent <c>EngageAircraft</c> (they stand and fire), every other man <c>ShelterFromAir</c> (cover; he fires only once
        /// it is close). ✅ Red-proof: the drone-gunner choice dropped ⇒ every man shelters ⇒ red.
        /// </summary>
        [Fact]
        public void CE2436_AU7_UnderADrone_TheShotgunManAndTheMgEngage_TheRestTakeCover()
        {
            var s = new Squad(members: 4);
            if (!s.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>()) s.Repo.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            s.Repo.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(Hrot.Map.Common.HrotEnvironment.CreateTkb());
            long[] types = { Hrot.Map.Common.TkbEntityTypes.Infantry_Rifleman, Hrot.Map.Common.TkbEntityTypes.Infantry_AntiDrone,
                             Hrot.Map.Common.TkbEntityTypes.Infantry_AutomaticRifleman, Hrot.Map.Common.TkbEntityTypes.Infantry_Rifleman };
            for (int i = 0; i < 4; i++) s.Repo.AddComponent(s.Members[i], new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)(types[i]) });
            s.Order("{\"Objective\":[0,100,0],\"BoundMetres\":30}");
            s.StartSop("BasicSquadSop");
            for (int i = 0; i < 20; i++) s.Tick();

            s.Orders.Clear();
            s.Sense(Fdp.Toolkit.Perception.Events.SensorChange.AirThreat);
            for (int i = 0; i < SopReactionFrames + 2; i++) s.Tick();
            Assert.Equal(BehaviorNames.SquadShelterFromAir, s.TaskName);
            string Sent(int i) => s.Orders.Last(o => o.Entity == s.Members[i]).IntentId;
            Assert.Equal(BehaviorNames.ShelterFromAir, Sent(0));
            Assert.Equal(BehaviorNames.EngageAircraft, Sent(1));
            Assert.Equal(BehaviorNames.EngageAircraft, Sent(2));
            Assert.Equal(BehaviorNames.ShelterFromAir, Sent(3));
        }

        private static void Aircraft(Squad s, Fdp.Core.Entity e)
        {
            if (!s.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Motion.Locomotion>()) s.Repo.RegisterComponent<Fdp.Toolkit.Motion.Locomotion>();
            s.Repo.AddComponent(e, new Fdp.Toolkit.Motion.Locomotion { Class = Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft });
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2436</c> AU-5 (A-6, R-2111) — the company's DRONE TEAM (a subordinate whose roster holds a drone) takes no platoon role: it is
        /// ordered <c>DroneTeam</c> — watch the objective, strike the posts (the objective while none is reported) — never the base of fire or the
        /// assault; the assault still goes on the base of fire's Set (the team reports no Set); at the consolidation it only watches (no strike on
        /// our own objective). ✅ Red-proofs: the team not recognised ⇒ it takes the assault role ⇒ red; the strike kept at the consolidation ⇒ red.
        /// </summary>
        [Fact]
        public void CE2436_AU5_CompanyAttack_TheDroneTeamWatchesTheObjective_AndStrikesThePosts()
        {
            var c = new Company(platoons: 4);
            var team = c.S.Members[0];
            var drone = c.S.Repo.CreateEntity();
            Aircraft(c.S, drone);
            var roster = new Fdp.Core.CommandHierarchy.UnitRoster();
            Fdp.Core.CommandHierarchy.UnitRoster.Add(ref roster, drone);
            c.S.Repo.AddComponent(team, roster);
            Fdp.Core.Entity assault = c.S.Members[1], support = c.S.Members[2], reserve = c.S.Members[3];
            var o = new Vector3(0f, 600f, 0f);
            CompanyAttack(c, 0f, o);
            c.Ticks(6);
            var t = c.Last<DroneTeamParamsJsonDto>(team, BehaviorNames.DroneTeam);
            Assert.Equal(o, t.Watch);
            Assert.Equal(o, t.Strike);
            Assert.Equal(1, c.OrdersTo(support, BehaviorNames.PlatoonSupportByFire));
            Assert.Equal(0, c.OrdersTo(team, BehaviorNames.PlatoonSupportByFire));

            c.Set(support); c.Ticks(5);
            Assert.Equal(1, c.OrdersTo(assault, BehaviorNames.PlatoonAttack));
            Assert.Equal(0, c.OrdersTo(team, BehaviorNames.PlatoonAttack));
            c.Set(assault); c.Ticks(5);   // taken: consolidate
            Assert.Equal(Vector3.Zero, c.Last<DroneTeamParamsJsonDto>(team, BehaviorNames.DroneTeam).Strike);
            c.Set(assault); c.Set(support); c.Set(reserve);
            Assert.Equal(Fbt.NodeStatus.Success, c.RunToEnd());
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2436</c> AU-5 (A-6) — the team's play: its UNARMED drone (the recon quadcopter) is ordered <c>DroneRecon</c> over the watch
        /// point, its operator <c>DroneOperator</c> with the ARMED drone (the bomber) on the strike point; with no strike point the operator is left
        /// alone. ✅ Red-proofs: the recon drone sent to strike ⇒ red; the operator not ordered ⇒ red; ordered with no strike ⇒ red.
        /// </summary>
        [Fact]
        public void CE2436_AU5_TheDroneTeam_SendsTheReconDroneToWatch_AndTheOperatorToStrike()
        {
            DroneTeamParamsJsonDto Team(Vector3 strike) => new() { Watch = new Vector3(0f, 300f, 0f), Strike = strike };
            Squad Built(Vector3 strike)
            {
                var s = new Squad(members: 3);
                if (!s.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>()) s.Repo.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
                s.Repo.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(Hrot.Map.Common.HrotEnvironment.CreateTkb());
                for (int i = 1; i < 3; i++)
                {
                    Aircraft(s, s.Members[i]);
                    s.Repo.AddComponent(s.Members[i], new Fdp.Toolkit.Replication.Components.TkbIdentity
                        { TkbType = (Fdp.Core.TkbTypeId)(i == 1 ? Hrot.Map.Common.TkbEntityTypes.Drone_Recon : Hrot.Map.Common.TkbEntityTypes.Drone_Bomber) });
                    s.Repo.AddComponent(s.Members[i], new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 1000 + i });
                }
                s.Order(System.Text.Json.JsonSerializer.Serialize(Team(strike), Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed), BehaviorNames.DroneTeam);
                for (int i = 0; i < 4; i++) s.Tick();
                return s;
            }
            var post = new Vector3(10f, 320f, 0f);
            var s = Built(post);
            Assert.Equal(BehaviorNames.DroneRecon, s.Orders.Last(o => o.Entity == s.Members[1]).IntentId);
            Assert.DoesNotContain(s.Orders, o => o.Entity == s.Members[2]);   // the operator flies it
            var op = s.Orders.Last(o => o.Entity == s.Members[0]);
            Assert.Equal(BehaviorNames.DroneOperator, op.IntentId);
            var order = System.Text.Json.JsonSerializer.Deserialize<DroneOperatorOrder>(op.JsonParams, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
            Assert.Equal(1002, order.Drone1.Ref.NetworkId);
            Assert.Equal(post, (Vector3)order.Location);

            var watchOnly = Built(Vector3.Zero);
            Assert.DoesNotContain(watchOnly.Orders, o => o.Entity == watchOnly.Members[0]);
        }
    
        /// <summary>
        /// ⭐⭐ <c>CE-2436</c> AU-4 blind fire (A-4 as designed) — a vehicle supporting by fire that SEES NOTHING itself fires its cannon on the
        /// REPORTED posts it was given: once at its stand-off point, a <see cref="VehicleSupportByFireBehavior.BlindRounds"/>-round burst
        /// (<c>FireAtPoint</c>, mount 0) on the posts' centre; the next only <see cref="VehicleSupportByFireBehavior.BlindIntervalSeconds"/> after
        /// the first ended; and it keeps supporting. Without posts it ends at once (nothing to fight), as before.
        /// ✅ Red-proofs: no blind branch ⇒ no burst, it ends ⇒ red; the interval dropped ⇒ the second burst at once ⇒ red.
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public unsafe void CE2436_AU4_AVehicleThatSeesNothing_FiresBurstsOnTheReportedPosts(bool posts)
        {
            var w = new StationWeaponBehaviorTests.World();
            var repo = w.Repo;
            if (!repo.HasComponent<WeaponChannel>(w.Man)) repo.AddComponent(w.Man, new WeaponChannel());
            w.Start(BehaviorNames.VehicleSupportByFire, posts ? "{\"Position\":[0,0,0],\"Posts\":[300,40,0]}" : "{\"Position\":[0,0,0]}");
            for (int i = 0; i < 3; i++) w.Tick();
            repo.GetComponentRW<LocomotionChannel>(w.Man).Status = Fbt.NodeStatus.Success;   // at its stand-off point
            for (int i = 0; i < 3; i++) w.Tick();
            ref readonly var ch = ref repo.GetComponentRO<WeaponChannel>(w.Man);
            if (!posts)
            {
                Assert.Equal(Fbt.NodeStatus.Success, w.Finished);
                Assert.NotEqual(Fdp.Toolkit.Combat.CombatConstants.ActionIdFireAtPoint, ch.ActiveAction);
                return;
            }
            Assert.Null(w.Finished);
            Assert.Equal(Fdp.Toolkit.Combat.CombatConstants.ActionIdFireAtPoint, ch.ActiveAction);
            var fire = System.Runtime.CompilerServices.Unsafe.As<byte, Fdp.Toolkit.Combat.Executors.FireAtPointParams>(ref repo.GetComponentRW<WeaponChannel>(w.Man).Params[0]);
            Assert.Equal(new Vector3(300f, 40f, 0f), fire.Point);
            Assert.Equal(VehicleSupportByFireBehavior.BlindRounds, fire.Rounds);
            Assert.Equal(0, fire.Mount);
            uint first = ch.ActionInstanceId;
            repo.GetComponentRW<WeaponChannel>(w.Man).Status = Fbt.NodeStatus.Success;     // the burst is out
            for (int i = 0; i < 50; i++) w.Tick();                                           // 5 s: no second burst yet
            Assert.NotEqual(Fdp.Toolkit.Combat.CombatConstants.ActionIdFireAtPoint, repo.GetComponentRO<WeaponChannel>(w.Man).ActiveAction);
            for (int i = 0; i < 110; i++) w.Tick();                                          // past the 15 s
            Assert.Equal(Fdp.Toolkit.Combat.CombatConstants.ActionIdFireAtPoint, repo.GetComponentRO<WeaponChannel>(w.Man).ActiveAction);
            Assert.NotEqual(first, repo.GetComponentRO<WeaponChannel>(w.Man).ActionInstanceId);
            Assert.Null(w.Finished);
        }
        /// <summary>
        /// ⭐⭐ D-11 B (CE-4666, user-approved 2026-11-19; Assault Under Fire §9.1b) — the base of fire's men are PLACED by the squad's own post plan
        /// (<see cref="DefencePlan"/>, facing the reported posts), not held on ONE point: its cover ask answers four points and each man is ordered
        /// to a DIFFERENT one of them, his post set (<c>CombatPosture</c> with <c>Post</c>). ⭐ CE-8319: the plan starts only once the squad is AT the point,
        /// and on the way each man heads for his own spot on a line across the line of fire there.
        /// ✅ Red-proofs: phase 0 back to <c>Hold(…, p.Position)</c> ⇒ two men on one point ⇒ red; the plan started away from the point ⇒ a post search ⇒ red.
        /// </summary>
        [Fact]
        public void D11B_TheBaseOfFire_TakesPostsFromTheSquadsOwnPlan_NotOnePoint()
        {
            var s = new Squad(4);
            var support = new Vector3(6f, 60f, 0f);
            s.Order(SupportByFireJson(support, new Vector3(0f, 180f, 0f), new Vector3(20f, 185f, 0f)), BehaviorNames.SupportByFire);
            s.Tick(); s.Tick(); s.Tick();
            // ⭐ CE-8319 — on the way each man heads for his OWN spot on a line across the line of fire at the point (never one shared point)
            var onTheWay = s.Members.Select(m => PostureObjective(s, m)).ToArray();
            Assert.Equal(onTheWay.Length, onTheWay.Distinct().Count());
            Assert.All(onTheWay, o => Assert.True(Vector3.Distance(o, support) <= 2f * Hrot.AI.Behaviors.Brains.DefencePlan.DispersalMetres, $"{o} far from the point"));
            // ⭐ CE-8319 — and the posts are not searched for until the squad is AT its point (searched from where it stood, they were in the house it left)
            Assert.True(EqsChildSensor.Find(s.Repo, s.Commander, SquadDefendPlay.CoverSite).IsNull
                        && EqsChildSensor.Find(s.Repo, s.Commander, SquadDefendPlay.WindowSite).IsNull, "no post search while the squad is away from its point");
            foreach (var m in s.Members) s.Repo.GetComponentRW<Fdp.Core.SimTransform>(m).Position = support;   // it arrives

            var cover = new[] { support + new Vector3(-9f, 4f, 0f), support + new Vector3(-3f, 5f, 0f), support + new Vector3(3f, 5f, 0f), support + new Vector3(9f, 4f, 0f) };
            for (int k = 0; k < 80; k++)
            {
                var c = EqsChildSensor.Find(s.Repo, s.Commander, SquadDefendPlay.CoverSite);
                if (!c.IsNull && !s.Repo.GetComponentRO<EqsCognitiveBuffer>(c).IsReady) AnswerAll(s, c, cover);
                foreach (int site in new[] { SquadDefendPlay.WindowSite, SquadDefendPlay.RoofSite })
                {
                    var a = EqsChildSensor.Find(s.Repo, s.Commander, site);
                    if (!a.IsNull && !s.Repo.GetComponentRO<EqsCognitiveBuffer>(a).IsReady) AnswerAll(s, a);
                }
                s.Pause(0.25);
                s.Tick();
            }
            var objectives = s.Members.Select(m => PostureObjective(s, m)).ToArray();
            Assert.All(objectives, o => Assert.True(cover.Contains(o), $"ordered to {o}; cover {string.Join(' ', cover)}"));
            Assert.Equal(objectives.Length, objectives.Distinct().Count());   // no two men on one point
        }

        /// <summary>The objective of the last <c>CombatPosture</c> order to <paramref name="m"/>.</summary>
        private static Vector3 PostureObjective(Squad s, Fdp.Core.Entity m)
            => System.Text.Json.JsonSerializer.Deserialize<CombatPostureParamsJsonDto>(
                   s.Orders.Last(o => o.Entity.Equals(m) && o.IntentId == BehaviorNames.CombatPosture).JsonParams,
                   Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed)!.Advance.Objective;
    }
}
