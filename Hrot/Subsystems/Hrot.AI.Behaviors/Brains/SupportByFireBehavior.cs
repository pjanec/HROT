using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Squad.Command;
using Fdp.Toolkit.Squad;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-2436</c> AU-1 — <c>SupportByFire</c>'s order: the support point and up to <see cref="MaxPosts"/> reported posts.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SupportByFireParams
    {
        public const int MaxPosts = 4;
        public Vector3 Position;
        public Vector3 P0, P1, P2, P3;
        public int Posts;
        public float Speed;
        public float CooldownSeconds;

        public readonly Vector3 Post(int i) => i switch { 0 => P0, 1 => P1, 2 => P2, _ => P3 };
    }

    /// <summary>⭐ <c>CE-2436</c> AU-1 — what <c>SupportByFire</c> keeps: its phase (0 moving to the point, 1 firing), the SET it sent, the
    /// members to order again.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct SupportByFireState
    {
        public byte Phase;
        public byte Issued;
        public byte SetSent;
        public SimInstant Since;
        /// <summary>⭐ AU-2 (A-8) — when the firing began, and the rounds the squad carried then (−1 = not yet read).</summary>
        public SimInstant FiringSince;
        public long RoundsAtFiring;
        public int Orders;
        public fixed byte Dirty[CommandPicture.MaxSubordinates];
        /// <summary>⭐ <c>CE-4667</c> (D-7) — 1 = this man has no rounds left: he holds with return fire where he is (exit a).</summary>
        public fixed byte Empty[CommandPicture.MaxSubordinates];
        /// <summary>⭐ <c>CE-4667</c> (D-7) — since when no post has had a live contact near it · 1 = the play stopped firing (exit b).</summary>
        public SimInstant QuietSince;
        public byte Down;
        /// <summary>⭐ D-11 B (CE-4666, user-approved) — the base of fire's posts: the squad's own post plan (<see cref="DefencePlan"/>), facing the
        /// reported posts · its outstanding ask · 1 once the posts are given (the men re-ordered to them).</summary>
        public DefencePosts Plan;
        public DefenceAsk Ask;
        public byte Planned;
    }

    /// <summary>
    /// ⭐ D-11 B — the base of fire's crew for <see cref="DefencePlan"/>: the squad's roster (as <see cref="RosterCrew"/>, indexed like
    /// <see cref="SupportByFireBehavior.Run"/>'s members), the posts FACING the reported posts' centre — a sector plan: a line across the line of
    /// fire, a cover search per post, the gun on the longest view. ⛔ No second planner (ruling 9).
    /// </summary>
    public readonly struct SupportCrew : IDefenceCrew
    {
        private readonly EntityRepository _world;
        private readonly Entity _squad;
        private readonly double _now;
        private readonly Vector3 _facing;

        public SupportCrew(EntityRepository world, Entity squad, double now, in SupportByFireParams p)
        {
            _world = world; _squad = squad; _now = now;
            var sum = Vector3.Zero;
            for (int k = 0; k < p.Posts; k++) sum += p.Post(k);
            _facing = p.Posts > 0 ? sum / p.Posts : p.Position;
        }

        public EntityRepository World => _world;
        public Entity Carrier => _squad;
        public double Now => _now;
        public int Count => _world.HasComponent<UnitRoster>(_squad) ? Math.Min(_world.GetComponentRO<UnitRoster>(_squad).Count, DefencePosts.Capacity) : 0;
        public bool Live(int i) => Live(i, out _);
        public bool Live(int i, out Vector3 at)
        {
            at = default;
            var m = CommandPictureOps.Subordinate(_world, _squad, i);
            if (m.IsNull || !_world.IsAlive(m) || !Injury.IsInTheFight(_world, m) || !_world.HasComponent<SimTransform>(m)) return false;
            at = _world.GetComponentRO<SimTransform>(m).Position;
            return true;
        }
        public bool Support(int i)
            => _world.HasComponent<UnitRoster>(_squad) && SquadRoles.CarriesSupportWeapon(SquadRoles.Of(_world, in _world.GetComponentRO<UnitRoster>(_squad), i));
        public Entity Origin()
        {
            for (int i = 0; i < Count; i++) if (Live(i)) return CommandPictureOps.Subordinate(_world, _squad, i);
            return Entity.Null;
        }
        public ProficiencyTier MedianTier()
        {
            Span<Entity> men = stackalloc Entity[DefencePosts.Capacity];
            int n = 0;
            for (int i = 0; i < Count && n < men.Length; i++) if (Live(i)) men[n++] = CommandPictureOps.Subordinate(_world, _squad, i);
            return ProficiencyOf.MedianTier(_world, men[..n]);
        }
        /// <summary>The reported posts' centre (the order's support point when it names none) — the base of fire faces its targets.</summary>
        public bool Facing(out Vector3 at) { at = _facing; return true; }
        public bool DispersesUnposted => true;   // ⭐ D-13
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2436</c> AU-1 (A-1; 📄 docs/designs/group-maneuvers/DESIGN_Assault_Under_Fire.md §5) — the BASE OF FIRE as a squad play: the
    /// squad moves to its support point and holds (every man the library <c>CombatPosture</c> hold — <see cref="SquadManeuverRunner.Hold"/>);
    /// once its men are ON THEIR POSTS — planned at the point (⭐ CE-8319) — or after <see cref="ArriveTimeoutSeconds"/> on the way, every man keeps
    /// SUSTAINED fire on a REPORTED post — man <c>i</c> on post <c>i mod n</c> — through the library <c>Suppress</c>
    /// (<see cref="SquadManeuverRunner.Suppress"/>: aimed when he sees someone there, blind at the point when not — 📌 the measured cause: a
    /// base of fire that only fought what it SAW rarely saw a man in a window at 120–250 m). Ammunition-paced by <c>Suppress</c>'s own cadence
    /// (blind rounds ×2.5 slower). It reports SET once firing — the platoon's <c>SupportSet</c> reads it — and runs until the order is
    /// replaced: the SHIFT (A-3) is a new order with new posts, the LIFT (DA-11) an order to defend.
    /// ⭐ R-2084: a member is ordered only when his order is dirty (a phase change) or no longer stands — no JSON per frame.
    /// </summary>
    public static class SupportByFireBehavior
    {
        /// <summary>⭐ <c>CE-8319</c> — a squad this close to its support point when ordered is already THERE: it fires at once (m).</summary>
        public const float ArriveMetres = 12f;
        /// <summary>Firing starts after this long on the way even short of the point (s): a base of fire that cannot reach its point still fires.</summary>
        public const double ArriveTimeoutSeconds = 60.0;
        /// <summary>⭐ <c>CE-2436</c> AU-2 (A-8) — suppression is ESTABLISHED after this long firing (s) …</summary>
        public const double EstablishSeconds = 15.0;
        /// <summary>… with at least this many rounds spent per man in the fight (a squad whose men carry no counted weapon: the time alone).</summary>
        public const int EstablishRoundsPerMan = 3;
        /// <summary>⭐ D-11 B — how far round the support point the base of fire's post plan looks (m). ⭐ CE-8319: 40 (was 20) — the support point
        /// is a point on the axis, often in a street: 📐 village-street, the nearest houses stood 25–30 m off it, so a 20 m plan found only open spots
        /// IN the street and the base of fire lay there; doctrine puts it in the buildings beside its line of fire.</summary>
        public const float PostSearchMetres = 40f;
        /// <summary>⭐ <c>CE-8319</c> — the squad plans its posts once its men's centre is this close to the support point (m).</summary>
        public const float PlanFromMetres = 20f;

        /// <summary>⭐ <c>CE-4667</c> (D-7, user-approved) — a post is still live while a live contact stands within this of it (m) …</summary>
        public const float PostLiveMetres = 25f;
        /// <summary>… and the play stops firing when no post has been live for this long (s).</summary>
        public const double TargetsDownSeconds = 10.0;

        private static readonly int s_squadHash = BehaviorHash.FromName(BehaviorNames.SupportByFire);
        private static readonly int s_platoonHash = BehaviorHash.FromName(BehaviorNames.PlatoonSupportByFire);

        /// <summary>
        /// ⭐⭐ <c>CE-2436</c> — <paramref name="unit"/>'s current order (running or paused) is a base of fire (<c>SupportByFire</c> /
        /// <c>PlatoonSupportByFire</c>). The SOPs spare it as they spare a defence (<c>PlatoonDefendBehavior.NotDefending</c>,
        /// <c>SquadDefendBehavior.NotDefending</c>): its fire IS the answer to the contact. 📌 Live (cc-battalion, probe): the platoon SOP's
        /// <c>PlatoonHold</c> paused the company's base of fire 16 s after it began, held its squads, and it never fired again.
        /// </summary>
        public static bool Supporting(EntityRepository world, Entity unit)
        {
            var o = Hrot.AI.Behaviors.Orders.CurrentOrders.Of(world, unit);
            return o.Exists && (o.BehaviorHash == s_squadHash || o.BehaviorHash == s_platoonHash);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SupportByFireBlackboard
        {
            public SupportByFireParams Order;
            public SupportByFireState State;
        }

        [BehaviorResolver(BehaviorNames.SupportByFire)]
        public static void Resolve(in SupportByFireParamsJsonDto authored, ref SupportByFireBlackboard block, EntityRepository world, Entity self)
        {
            block.Order = new SupportByFireParams
            {
                Position = authored.Position, Speed = authored.Speed,
                CooldownSeconds = authored.CooldownSeconds > 0f ? authored.CooldownSeconds : 1f,
            };
            Add(ref block.Order, authored.Post1);
            Add(ref block.Order, authored.Post2);
            Add(ref block.Order, authored.Post3);
            Add(ref block.Order, authored.Post4);
            if (block.Order.Posts == 0) Add(ref block.Order, authored.Position);   // ⛔ none named: it holds and fights what it sees (its point)
            block.State = default;
        }

        private static void Add(ref SupportByFireParams p, Vector3 post)
        {
            if (post == Vector3.Zero || p.Posts >= SupportByFireParams.MaxPosts) return;
            switch (p.Posts) { case 0: p.P0 = post; break; case 1: p.P1 = post; break; case 2: p.P2 = post; break; default: p.P3 = post; break; }
            p.Posts++;
        }

        [Behavior(BehaviorNames.SupportByFire, Params = typeof(SupportByFireParamsJsonDto), Fulfils = nameof(Hrot.AI.Behaviors.Orders.OrderKind.SupportByFire), Categories = BehaviorCategory.Commander,
                  Description = "A squad's base of fire: holds at the support point and keeps the reported posts under sustained fire.")]
        [BTreeDefinition(BehaviorNames.SupportByFire, Curated = true, Group = "Library/Combat", ParamsType = typeof(SupportByFireBlackboard))]
        public static BTreeBuilder<SupportByFireBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<SupportByFireBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, Run);

        public static unsafe NodeStatus Run(ref SupportByFireParams p, ref SupportByFireState ws, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<UnitRoster>(self)) return NodeStatus.Running;
            int n = Math.Min(world.GetComponentRO<UnitRoster>(self).Count, CommandPicture.MaxSubordinates);
            double now = CommandPictureOps.Now(world);
            if (ws.Issued == 0)
            {
                ws.Issued = 1; ws.Since = (SimInstant)now;
                for (int i = 0; i < CommandPicture.MaxSubordinates; i++) ws.Dirty[i] = 1;
                // ⭐ D-11 B — nobody has a post yet (SquadDefendPlay.Start's own reset: a zeroed PostOf reads as "post 0", and the plan asks nothing)
                for (int i = 0; i < DefencePosts.Capacity; i++) { ws.Plan.PostOf[i] = DefencePosts.None; ws.Plan.MovedAt[i] = double.NegativeInfinity; ws.Plan.DwellFrom[i] = double.NegativeInfinity; }
                ws.Plan.Hunter = DefencePosts.None;
                // ⭐ CE-8319 — ALREADY at the point when ordered (its own position, or a SHIFT — "the fire moves, not the men"): it fires at once from
                //   where it stands, as before; only a squad that must move up plans its posts and occupies them first
                var c0 = MenCentre(world, self, p.Position);
                if ((c0.X - p.Position.X) * (c0.X - p.Position.X) + (c0.Y - p.Position.Y) * (c0.Y - p.Position.Y) <= ArriveMetres * ArriveMetres)
                {
                    ws.Phase = 1; ws.Planned = 1;
                    ws.FiringSince = (SimInstant)now; ws.QuietSince = (SimInstant)now;
                    ws.RoundsAtFiring = StaffComposerSystem.RoundsOf(world, self, out _);
                }
            }
            if (ws.Phase == 0)
            {
                // ⭐⭐ CE-8319 — it opens fire once its men are ON THEIR POSTS (planned at the point), not when the squad's centre is near the point:
                //   phase 1 orders each man to Suppress where he STANDS, so firing began before the men reached their posts — 📐 live, the arrival
                //   timeout started the fire from the house the squad was leaving. The timeout stays the fallback (a post he cannot reach).
                if (OnTheirPosts(ref ws, self, world, n) || now - ws.Since >= ArriveTimeoutSeconds)
                {
                    ws.Phase = 1;
                    ws.FiringSince = (SimInstant)now;
                    ws.QuietSince = (SimInstant)now;
                    ws.RoundsAtFiring = StaffComposerSystem.RoundsOf(world, self, out _);
                    for (int i = 0; i < CommandPicture.MaxSubordinates; i++) ws.Dirty[i] = 1;
                }
            }
            // ⭐⭐ CE-4667 (D-7, user-approved; Assault Under Fire §9.1a) — exit (b): every reported post with no live contact within
            //   PostLiveMetres for TargetsDownSeconds ⇒ the play stops firing, holds, reports TargetsDown (the platoon decides what is next);
            //   a live contact back at a post resumes the fire. 📌 Live (real-1): a base of fire kept Suppress 434 s on posts whose men were all dead
            if (ws.Phase == 1)
            {
                if (AnyPostLive(ref p, self, world)) { ws.QuietSince = (SimInstant)now; if (ws.Down != 0) { ws.Down = 0; Redo(ref ws); } }
                else if (ws.Down == 0 && now - ws.QuietSince >= TargetsDownSeconds)
                {
                    ws.Down = 1;
                    Redo(ref ws);
                    CommandNodes.ReportProgress(world, self, ProgressKind.TargetsDown, p.Position);
                }
            }
            // ⭐⭐ D-11 B (CE-4666, user-approved 2026-11-19) — the men do not share ONE point: the squad's own post plan places them (a line across
            //   the line of fire at the support point, a cover search per post, facing the reported posts); until it answers they head for the point.
            //   📌 Live (pc-corridor-fight): phase 0 held every man at p.Position ⇒ bunched by construction, in the open.
            // ⭐⭐ CE-8319 — the posts are planned only ONCE THE SQUAD IS AT ITS SUPPORT POINT (its centre within PostSearchMetres, or phase 1 by
            //   the arrival timeout): DefencePlan's window / roof / cover asks search round the crew's first live man (DefencePlan.Step's `from`),
            //   so planned from where the squad STOOD they posted it in the house it was leaving. 📐 live village-street-mg-flank: support point
            //   (411,245), every man posted where he stood at (372,272–278) — 150 m from the gun, across the street, 0 hits in 207 rounds.
            var mid = MenCentre(world, self, p.Position);   // where its MEN are (CentreOf reads the squad entity's own transform first)
            bool atPoint = (mid.X - p.Position.X) * (mid.X - p.Position.X) + (mid.Y - p.Position.Y) * (mid.Y - p.Position.Y) <= PlanFromMetres * PlanFromMetres;
            if (ws.Planned == 0 && (atPoint || ws.Phase == 1))
            {
                var crew = new SupportCrew(world, self, now, in p);
                if (DefencePlan.Step(ref ws.Plan, ref ws.Ask, in crew, default, p.Position, PostSearchMetres)) { ws.Planned = 1; Redo(ref ws); }
            }
            for (int i = 0; i < n; i++)
            {
                var member = CommandPictureOps.Subordinate(world, self, i);
                if (member.IsNull || !world.IsAlive(member) || !Injury.IsInTheFight(world, member)) continue;
                // ⭐ CE-4667 (D-7) — exit (a): a man with no rounds left is no base of fire: he holds with return fire where he is (once, then as any order)
                bool empty = ws.Phase == 1 && StaffComposerSystem.RoundsOf(world, member, out long carries) == 0 && carries > 0;
                if (empty && ws.Empty[i] == 0) { ws.Empty[i] = 1; ws.Dirty[i] = 1; }
                if (ws.Dirty[i] == 0 && CommandOrders.Stands(world, self, member)) continue;
                ws.Dirty[i] = 0;
                var at = world.HasComponent<SimTransform>(member) ? (Vector3)world.GetComponentRO<SimTransform>(member).Position : p.Position;
                bool posted = ws.Planned != 0 && i < DefencePosts.Capacity && ws.Plan.PostOf[i] != DefencePosts.None && ws.Plan.PostOf[i] < ws.Plan.Count;
                // ⭐ CE-8319 — on the way (no post yet) each man heads for his own spot on a line across the line of fire, never one shared point
                var post = posted ? ws.Plan.Point(ws.Plan.PostOf[i]) : OnTheGround(world, SpotOnTheLine(in p, i, n), at.Z);
                if (ws.Phase == 0)
                    SquadManeuverRunner.Hold(world, self, member, post, p.Speed,
                                             posted ? new MemberPost { Set = 1, Radius = SquadDefendPlay.PostRadius, Open = ws.Plan.Open[ws.Plan.PostOf[i]] } : default);   // ⭐ D-13 — prone on a dispersal spot   // ⭐ D-11 B — his post
                else if (ws.Empty[i] != 0 || ws.Down != 0) SquadManeuverRunner.Hold(world, self, member, at, p.Speed);
                else SquadManeuverRunner.Suppress(world, self, member, Entity.Null, p.Post(i % Math.Max(1, p.Posts)), at, p.Speed);
                ws.Orders++;
            }
            // ⭐⭐ AU-2 (A-8) — SET means SUPPRESSION ESTABLISHED, a cause not a clock: firing for EstablishSeconds AND rounds really spent on the
            //   posts (EstablishRoundsPerMan a man in the fight). The platoon's SupportSet reads it, so the assault moves only under it.
            if (ws.Phase == 1 && ws.SetSent == 0 && Established(ref ws, self, world, now))
            {
                ws.SetSent = 1;
                CommandNodes.ReportProgress(world, self, ProgressKind.Set, p.Position);
            }
            return NodeStatus.Running;
        }

        /// <summary>⭐ <c>CE-8319</c> — the plan has answered and every live man stands within <see cref="SquadDefendPlay.PostRadius"/> of his post
        /// (a man the plan gave no post counts as there).</summary>
        private static unsafe bool OnTheirPosts(ref SupportByFireState ws, Entity squad, EntityRepository world, int n)
        {
            if (ws.Planned == 0) return false;
            for (int i = 0; i < n && i < DefencePosts.Capacity; i++)
            {
                var m = CommandPictureOps.Subordinate(world, squad, i);
                if (m.IsNull || !world.IsAlive(m) || !Injury.IsInTheFight(world, m) || !world.HasComponent<SimTransform>(m)) continue;
                if (ws.Plan.PostOf[i] == DefencePosts.None || ws.Plan.PostOf[i] >= ws.Plan.Count) continue;
                var post = ws.Plan.Point(ws.Plan.PostOf[i]);
                var at = (Vector3)world.GetComponentRO<SimTransform>(m).Position;
                if ((at.X - post.X) * (at.X - post.X) + (at.Y - post.Y) * (at.Y - post.Y) > SquadDefendPlay.PostRadius * SquadDefendPlay.PostRadius) return false;
            }
            return true;
        }

        /// <summary>⭐ <c>CE-8319</c> — the middle of the squad's live men (in the fight); <paramref name="none"/> when it has none.</summary>
        private static Vector3 MenCentre(EntityRepository world, Entity squad, Vector3 none)
        {
            if (!world.HasComponent<UnitRoster>(squad)) return none;
            int n = Math.Min(world.GetComponentRO<UnitRoster>(squad).Count, CommandPicture.MaxSubordinates);
            var sum = Vector3.Zero; int live = 0;
            for (int i = 0; i < n; i++)
            {
                var m = CommandPictureOps.Subordinate(world, squad, i);
                if (m.IsNull || !world.IsAlive(m) || !Injury.IsInTheFight(world, m) || !world.HasComponent<SimTransform>(m)) continue;
                sum += (Vector3)world.GetComponentRO<SimTransform>(m).Position; live++;
            }
            return live > 0 ? sum / live : none;
        }

        /// <summary>⭐ <c>CE-8319</c> — member <paramref name="i"/> of <paramref name="n"/>'s spot on the way to the support point: on the line through
        /// <see cref="SupportByFireParams.Position"/> across the line of fire (toward the reported posts' centre), <see cref="DefencePlan.DispersalMetres"/>
        /// apart — doctrine's interval, the same one the plan's dispersal keeps.</summary>
        internal static Vector3 SpotOnTheLine(in SupportByFireParams p, int i, int n)
        {
            var facing = Vector3.Zero;
            for (int k = 0; k < p.Posts; k++) facing += p.Post(k);
            facing = p.Posts > 0 ? facing / p.Posts : p.Position;
            var f = new Vector2(facing.X - p.Position.X, facing.Y - p.Position.Y);
            var across = f.LengthSquared() > 1e-4f ? Vector2.Normalize(new Vector2(-f.Y, f.X)) : Vector2.UnitX;
            float off = (i - (Math.Max(n, 1) - 1) * 0.5f) * DefencePlan.DispersalMetres;
            return p.Position + new Vector3(across * off, 0f);
        }

        /// <summary>⭐ <c>CE-8319</c> — <paramref name="spot"/> on the ground: an order's point is 2-D (its Z often 0), and a move to it on a terrain whose
        /// ground is not at 0 aims under the ground and never starts (📐 village-street: ground ≈ 178 m, the base of fire never left its house). The
        /// world's stand height near <paramref name="zHint"/> (the man's own); no world or no stand ⇒ the spot at the hint's height.</summary>
        private static Vector3 OnTheGround(EntityRepository world, Vector3 spot, float zHint)
        {
            var q = Fdp.Toolkit.World.WorldQuery.Of(world);
            return q != null && q.TryStandAt(spot.X, spot.Y, zHint, out float z) ? spot with { Z = z } : spot with { Z = zHint };
        }

        private static unsafe void Redo(ref SupportByFireState ws)
        {
            for (int i = 0; i < CommandPicture.MaxSubordinates; i++) ws.Dirty[i] = 1;
        }

        /// <summary>⭐ <c>CE-4667</c> (D-7) — some post is still worth firing on: a post with NO known contact near it (within <see cref="PostLiveMetres"/>)
        /// stays live — the base of fire fires BLIND at a reported post by design (AU-1: it rarely sees a man in a window) — and a post with known contacts
        /// is live while one of them is still in the fight (<c>SquadContacts.IsEnemy</c>; a heard one counts). ⇒ the play stops only when every post's
        /// known men are DOWN (they stay in memory by design, CE-2294) — 📌 the real-1 case: hp 0 everywhere, Suppress for 434 s.</summary>
        private static bool AnyPostLive(ref SupportByFireParams p, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<SquadCognitiveState>(self)) return true;   // no picture: never stop on a guess
            ref readonly var squad = ref world.GetComponentRO<SquadCognitiveState>(self);
            var pool = SquadContacts.Slots(in squad.Contacts);
            for (int k = 0; k < Math.Max(1, p.Posts); k++)
            {
                var post = p.Post(k);
                bool known = false;
                for (int c = 0; c < squad.Contacts.Count && c < pool.Length; c++)
                {
                    float dx = pool[c].PositionX - post.X, dy = pool[c].PositionY - post.Y;
                    if (dx * dx + dy * dy > PostLiveMetres * PostLiveMetres) continue;
                    known = true;
                    if (SquadContacts.IsEnemy(world, in pool[c])) return true;   // someone there is still in the fight
                }
                if (!known) return true;                                          // nobody known there: keep the blind fire
            }
            return false;
        }

        /// <summary>⭐ AU-2 (A-8) — firing long enough, and the rounds gone: the squad's carried rounds fell by
        /// <see cref="EstablishRoundsPerMan"/> per man in the fight since firing began (no counted weapon ⇒ the time alone).</summary>
        private static bool Established(ref SupportByFireState ws, Entity self, EntityRepository world, double now)
        {
            if (now - ws.FiringSince < EstablishSeconds) return false;
            long have = StaffComposerSystem.RoundsOf(world, self, out long max);
            if (max <= 0) return true;
            int men = Fdp.Toolkit.Squad.CombatEffective.FightingMembers(world, self);
            return ws.RoundsAtFiring - have >= (long)EstablishRoundsPerMan * System.Math.Max(1, men);
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2436</c> AU-3 (A-1 one level up; 📄 DESIGN_Assault_Under_Fire §9.3) — a PLATOON's base of fire (the company's): every live squad is
    /// ordered <c>SupportByFire</c> on the same reported posts, its point spread <see cref="SpreadMetres"/> across the line of fire; the platoon
    /// reports SET once every live squad has (each squad's SET is its suppression ESTABLISHED — R-2107), so the company's assault goes in only under
    /// established fire. 📌 measured: the company's base of fire ran <c>PlatoonDefend</c> 250 m back and fought only what it saw.
    /// ⭐ R-2084: a squad is ordered only when dirty or its order no longer stands.
    /// </summary>
    public static class PlatoonSupportByFireBehavior
    {
        /// <summary>Squads stand this far apart across the line of fire (m).</summary>
        public const float SpreadMetres = 20f;

        [StructLayout(LayoutKind.Sequential)]
        public struct PlatoonSupportByFireBlackboard
        {
            public SupportByFireParams Order;
            public SupportByFireState State;
        }

        [BehaviorResolver(BehaviorNames.PlatoonSupportByFire)]
        public static void Resolve(in PlatoonSupportByFireParamsJsonDto authored, ref PlatoonSupportByFireBlackboard block, EntityRepository world, Entity self)
        {
            var squad = new SupportByFireParamsJsonDto
            {
                Position = authored.Position, Post1 = authored.Post1, Post2 = authored.Post2, Post3 = authored.Post3, Post4 = authored.Post4,
                Speed = authored.Speed, CooldownSeconds = authored.CooldownSeconds,
            };
            var tmp = new SupportByFireBehavior.SupportByFireBlackboard();
            SupportByFireBehavior.Resolve(in squad, ref tmp, world, self);
            block.Order = tmp.Order;
            block.State = default;
        }

        [Behavior(BehaviorNames.PlatoonSupportByFire, Params = typeof(PlatoonSupportByFireParamsJsonDto), Fulfils = nameof(Hrot.AI.Behaviors.Orders.OrderKind.SupportByFire), Categories = BehaviorCategory.Commander,
                  Description = "A platoon's base of fire: every squad keeps the reported posts under sustained fire.")]
        [BTreeDefinition(BehaviorNames.PlatoonSupportByFire, Curated = true, Group = "Library/Combat", ParamsType = typeof(PlatoonSupportByFireBlackboard))]
        public static BTreeBuilder<PlatoonSupportByFireBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<PlatoonSupportByFireBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, Run);

        public static unsafe NodeStatus Run(ref SupportByFireParams p, ref SupportByFireState ws, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<UnitRoster>(self)) return NodeStatus.Running;
            int n = Math.Min(world.GetComponentRO<UnitRoster>(self).Count, CommandPicture.MaxSubordinates);
            if (ws.Issued == 0)
            {
                ws.Issued = 1;
                for (int i = 0; i < CommandPicture.MaxSubordinates; i++) ws.Dirty[i] = 1;
            }
            var toPosts = new Vector2(p.P0.X - p.Position.X, p.P0.Y - p.Position.Y);
            var dir = toPosts.LengthSquared() > 1e-4f ? Vector2.Normalize(toPosts) : Vector2.UnitY;
            var across = new Vector2(-dir.Y, dir.X);
            var json = Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;
            int live = 0, set = 0;
            for (int i = 0; i < n; i++)
            {
                var squad = CommandPictureOps.Subordinate(world, self, i);
                if (squad.IsNull || !world.IsAlive(squad)) continue;
                live++;
                if (CommandPictureOps.ProgressOf(world, self, i) == ProgressKind.Set) set++;
                if (ws.Dirty[i] == 0 && CommandOrders.Stands(world, self, squad)) continue;
                ws.Dirty[i] = 0;
                var at = p.Position + new Vector3(across * ((i - (n - 1) * 0.5f) * SpreadMetres), 0f);
                var physical = System.Text.Json.JsonSerializer.Serialize(new SupportByFireParamsJsonDto
                {
                    Position = at, Speed = p.Speed, CooldownSeconds = p.CooldownSeconds,
                    Post1 = p.Posts > 0 ? p.P0 : default, Post2 = p.Posts > 1 ? p.P1 : default, Post3 = p.Posts > 2 ? p.P2 : default, Post4 = p.Posts > 3 ? p.P3 : default,
                }, json);
                // ⭐ agg-behaviors R-8315 / CE-8302 (R-8341) — a stochastic squad is ordered SupportByFire by KIND: the neutral AttackOrder HoldBack on ITS support point
                //   (the same `at` the physical squad gets) — it stands there; a HoldBack on the posts walked it to 100 m of them
                var neutral = System.Text.Json.JsonSerializer.Serialize(new Hrot.AI.Behaviors.Orders.AttackOrder
                    { Position = at, Speed = p.Speed, HoldBack = true }, json);
                CommandOrders.Route(world, self, squad, Hrot.AI.Behaviors.Orders.OrderKind.SupportByFire, neutral, BehaviorNames.SupportByFire, physical, newOrder: true);
                CommandOrders.AskSitrep(world, squad);
                ws.Orders++;
            }
            if (ws.SetSent == 0 && live > 0 && set == live)
            {
                ws.SetSent = 1;
                CommandNodes.ReportProgress(world, self, ProgressKind.Set, p.Position);
            }
            return NodeStatus.Running;
        }
    }
}
