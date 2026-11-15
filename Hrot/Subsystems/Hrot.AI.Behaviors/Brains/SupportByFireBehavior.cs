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
            if (ws.Phase == 1 && ws.SetSent == 0)
            {
                ws.SetSent = 1;
                CommandNodes.ReportProgress(world, self, ProgressKind.Set, p.Position);   // the platoon's SupportSet reads it
            }
            return NodeStatus.Running;
        }
    }
}
