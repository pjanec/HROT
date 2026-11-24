using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Squad.Command;
using Hrot.AI.Behaviors.Orders;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-8307</c> (R-8392, the review's F1 (b); 📄 docs/designs/aggregate-units/DESIGN_Split_One_Level.md §6 Q-e) — THE ONE RELAY: a split
    /// platoon / company (a physical command unit over stochastic or physical children) given an order no echelon play of its own fulfils (Move,
    /// BreakContact — a stochastic platoon's March / Dash run as Move, X2-J) passes the SAME order on, by KIND (<see cref="CommandOrders.Order"/>,
    /// the routing table resolves it for each child at the child's own resolution), with the SAME params (its start record's JSON), to every child
    /// in its roster; the play ends when every child has finished it. ⭐ One implementation (<see cref="Relay"/>) for every kind and echelon; the
    /// registered plays differ only in the neutral DTO they read (R-8300) and the kind they fulfil (X2C2).
    /// </summary>
    public static class UnitRelayBehaviors
    {
        public const string UnitRelayMove = "UnitRelayMove";
        public const string UnitRelayBreakContact = "UnitRelayBreakContact";

        [StructLayout(LayoutKind.Sequential)]
        public struct RelayState
        {
            public int Orders;
            /// <summary>⭐ CE-4685 — the roster slots seen RUNNING the relayed order at least once (bit per slot).</summary>
            public ulong Started;
            /// <summary>⭐ CE-4685 — sim time of the last order given (s).</summary>
            public double OrderedAt;
        }

        /// <summary>⭐ CE-4685 — a child given the order but not yet running it counts as busy this long (sim s): the order reaches its
        /// <c>BehaviorState</c> a frame or two later, through the intent bus. 📌 Measured on <c>llm-company-aggregated</c>: without it the relay ended
        /// 2 frames after ordering ("UnitRelayMove ended: Success" at 38.07 s, the squads' moves started at 38.10 s and were cleared with it at
        /// 38.12 s) — every split platoon stopped where it split. A child that never starts still lets the relay end after it (never a hang).</summary>
        public const double StartGraceSeconds = 3.0;

        [StructLayout(LayoutKind.Sequential)]
        public struct RelayArgs
        {
            public int Unused;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct UnitRelayBlackboard
        {
            public RelayArgs Order;
            public RelayState State;
        }

        [BehaviorResolver(UnitRelayMove)]
        public static void ResolveMove(in MoveOrder authored, ref UnitRelayBlackboard block, EntityRepository world, Entity self) => block = default;

        [BehaviorResolver(UnitRelayBreakContact)]
        public static void ResolveBreakContact(in BreakContactOrder authored, ref UnitRelayBlackboard block, EntityRepository world, Entity self) => block = default;

        [Behavior(UnitRelayMove, Params = typeof(MoveOrder), Fulfils = nameof(OrderKind.Move), Categories = BehaviorCategory.Commander)]
        [BTreeDefinition(UnitRelayMove, Curated = true, Group = "Library/Orders", ParamsType = typeof(UnitRelayBlackboard))]
        public static BTreeBuilder<UnitRelayBlackboard, BTreeContext> BuildMove()
            => new BTreeBuilder<UnitRelayBlackboard, BTreeContext>().StatefulAction(bb => bb.Order, bb => bb.State, RunMove);

        [Behavior(UnitRelayBreakContact, Params = typeof(BreakContactOrder), Fulfils = nameof(OrderKind.BreakContact), Categories = BehaviorCategory.Commander)]
        [BTreeDefinition(UnitRelayBreakContact, Curated = true, Group = "Library/Orders", ParamsType = typeof(UnitRelayBlackboard))]
        public static BTreeBuilder<UnitRelayBlackboard, BTreeContext> BuildBreakContact()
            => new BTreeBuilder<UnitRelayBlackboard, BTreeContext>().StatefulAction(bb => bb.Order, bb => bb.State, RunBreakContact);

        public static NodeStatus RunMove(ref RelayArgs p, ref RelayState ws, Entity self, EntityRepository world) => Relay(nameof(OrderKind.Move), ref ws, self, world);

        public static NodeStatus RunBreakContact(ref RelayArgs p, ref RelayState ws, Entity self, EntityRepository world) => Relay(nameof(OrderKind.BreakContact), ref ws, self, world);

        /// <summary>Order every child in the roster <paramref name="kind"/> with the unit's own order params (once; again only if another run
        /// replaced it); Success once every child was given it and none still runs it — ⭐ CE-4685: a child given it but not yet running it is
        /// still busy (<see cref="StartGraceSeconds"/>). 0 B in the steady state (an index loop, no new string).</summary>
        public static NodeStatus Relay(string kind, ref RelayState ws, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<UnitRoster>(self)) return NodeStatus.Running;
            int n = world.GetComponentRO<UnitRoster>(self).Count;
            if (n == 0) return NodeStatus.Running;
            string? json = null;
            bool busy = false;
            double now = Objectives.ObjectiveHost.Now(world);
            for (int slot = 0; slot < n; slot++)
            {
                var member = CommandPictureOps.Subordinate(world, self, slot);
                if (member.IsNull || !world.IsAlive(member)) continue;
                ulong bit = slot < 64 ? 1UL << slot : 0UL;
                if (CommandOrders.Stands(world, self, member))
                {
                    if (CommandOrders.Busy(world, self, member)) { busy = true; ws.Started |= bit; }
                    else if ((ws.Started & bit) == 0 && now - ws.OrderedAt < StartGraceSeconds) busy = true;   // given, not yet started (CE-4685)
                    continue;
                }
                json ??= OwnJson(world, self);
                CommandOrders.Order(world, self, member, kind, json);
                ws.Orders++;
                ws.Started &= ~bit;
                ws.OrderedAt = now;
                busy = true;
            }
            return busy || ws.Orders == 0 ? NodeStatus.Running : NodeStatus.Success;
        }

        /// <summary>The unit's own order params: its start record's JSON (the order as given), else <c>"{}"</c>.</summary>
        private static string OwnJson(EntityRepository world, Entity self)
        {
            if (!world.TryGetTable(typeof(BehaviorStartRecord), out _) || !world.HasManagedComponent<BehaviorStartRecord>(self)) return "{}";
            var record = ((ISimulationView)world).GetManagedComponentRO<BehaviorStartRecord>(self);
            return record?.JsonParams ?? "{}";
        }
    }
}
