using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Squad.Command;
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
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2436</c> AU-1 (A-1; 📄 docs/designs/group-maneuvers/DESIGN_Assault_Under_Fire.md §5) — the BASE OF FIRE as a squad play: the
    /// squad moves to its support point and holds (every man the library <c>CombatPosture</c> hold — <see cref="SquadManeuverRunner.Hold"/>);
    /// once it is there (its centre within <see cref="ArriveMetres"/>, or <see cref="ArriveTimeoutSeconds"/> on the way) every man keeps
    /// SUSTAINED fire on a REPORTED post — man <c>i</c> on post <c>i mod n</c> — through the library <c>Suppress</c>
    /// (<see cref="SquadManeuverRunner.Suppress"/>: aimed when he sees someone there, blind at the point when not — 📌 the measured cause: a
    /// base of fire that only fought what it SAW rarely saw a man in a window at 120–250 m). Ammunition-paced by <c>Suppress</c>'s own cadence
    /// (blind rounds ×2.5 slower). It reports SET once firing — the platoon's <c>SupportSet</c> reads it — and runs until the order is
    /// replaced: the SHIFT (A-3) is a new order with new posts, the LIFT (DA-11) an order to defend.
    /// ⭐ R-2084: a member is ordered only when his order is dirty (a phase change) or no longer stands — no JSON per frame.
    /// </summary>
    public static class SupportByFireBehavior
    {
        /// <summary>The squad is at its support point (m).</summary>
        public const float ArriveMetres = 12f;
        /// <summary>Firing starts after this long on the way even short of the point (s): a base of fire that cannot reach its point still fires.</summary>
        public const double ArriveTimeoutSeconds = 60.0;
        /// <summary>⭐ <c>CE-2436</c> AU-2 (A-8) — suppression is ESTABLISHED after this long firing (s) …</summary>
        public const double EstablishSeconds = 15.0;
        /// <summary>… with at least this many rounds spent per man in the fight (a squad whose men carry no counted weapon: the time alone).</summary>
        public const int EstablishRoundsPerMan = 3;

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

        [Behavior(BehaviorNames.SupportByFire, Params = typeof(SupportByFireParamsJsonDto), Categories = BehaviorCategory.Commander,
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
            }
            if (ws.Phase == 0)
            {
                var centre = StaffComposerSystem.CentreOf(world, self);
                float dx = centre.X - p.Position.X, dy = centre.Y - p.Position.Y;
                if (dx * dx + dy * dy <= ArriveMetres * ArriveMetres || now - ws.Since >= ArriveTimeoutSeconds)
                {
                    ws.Phase = 1;
                    ws.FiringSince = (SimInstant)now;
                    ws.RoundsAtFiring = StaffComposerSystem.RoundsOf(world, self, out _);
                    for (int i = 0; i < CommandPicture.MaxSubordinates; i++) ws.Dirty[i] = 1;
                }
            }
            for (int i = 0; i < n; i++)
            {
                var member = CommandPictureOps.Subordinate(world, self, i);
                if (member.IsNull || !world.IsAlive(member) || !Injury.IsInTheFight(world, member)) continue;
                if (ws.Dirty[i] == 0 && CommandOrders.Stands(world, self, member)) continue;
                ws.Dirty[i] = 0;
                var at = world.HasComponent<SimTransform>(member) ? (Vector3)world.GetComponentRO<SimTransform>(member).Position : p.Position;
                if (ws.Phase == 0) SquadManeuverRunner.Hold(world, self, member, p.Position, p.Speed);
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

        [Behavior(BehaviorNames.PlatoonSupportByFire, Params = typeof(PlatoonSupportByFireParamsJsonDto), Categories = BehaviorCategory.Commander,
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
                CommandOrders.Assign(world, self, squad, BehaviorNames.SupportByFire, System.Text.Json.JsonSerializer.Serialize(new SupportByFireParamsJsonDto
                {
                    Position = at, Speed = p.Speed, CooldownSeconds = p.CooldownSeconds,
                    Post1 = p.Posts > 0 ? p.P0 : default, Post2 = p.Posts > 1 ? p.P1 : default, Post3 = p.Posts > 2 ? p.P2 : default, Post4 = p.Posts > 3 ? p.P3 : default,
                }, json), newOrder: true);
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
