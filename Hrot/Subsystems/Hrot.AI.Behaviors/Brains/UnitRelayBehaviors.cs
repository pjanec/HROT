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
        }

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
        /// replaced it); Success once every child was given it and none still runs it. 0 B in the steady state (an index loop, no new string).</summary>
        public static NodeStatus Relay(string kind, ref RelayState ws, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<UnitRoster>(self)) return NodeStatus.Running;
            int n = world.GetComponentRO<UnitRoster>(self).Count;
            if (n == 0) return NodeStatus.Running;
            string? json = null;
            bool busy = false;
            for (int slot = 0; slot < n; slot++)
            {
                var member = CommandPictureOps.Subordinate(world, self, slot);
                if (member.IsNull || !world.IsAlive(member)) continue;
                if (CommandOrders.Stands(world, self, member))
                {
                    if (CommandOrders.Busy(world, self, member)) busy = true;
                    continue;
                }
                json ??= OwnJson(world, self);
                CommandOrders.Order(world, self, member, kind, json);
                ws.Orders++;
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
