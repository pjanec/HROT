using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Orders
{
    /// <summary>⭐ <c>CE-4612</c> (R-2088) — what a unit is ORDERED to do, as a typed value: its kind, who ordered it, its request id, the
    /// behaviour fulfilling it, and whether a reaction has paused it. <see cref="None"/> when it has no order (its SOP, idle).</summary>
    public readonly struct CurrentOrder
    {
        public readonly OrderKind Kind;
        public readonly BehaviorOrigin Origin;
        /// <summary>The order's request id (R-2090: its one identity).</summary>
        public readonly Guid RequestId;
        public readonly int BehaviorHash;
        /// <summary>A reaction runs over it (the order is the paused task).</summary>
        public readonly bool Paused;

        public CurrentOrder(OrderKind kind, BehaviorOrigin origin, Guid requestId, int behaviorHash, bool paused)
        {
            Kind = kind; Origin = origin; RequestId = requestId; BehaviorHash = behaviorHash; Paused = paused;
        }

        public static CurrentOrder None => default;
        /// <summary>The unit has an order (running or paused).</summary>
        public bool Exists => BehaviorHash != 0;
        /// <summary>Its kind is <paramref name="a"/> or <paramref name="b"/>.</summary>
        public bool IsOneOf(OrderKind a, OrderKind b) => Exists && (Kind == a || Kind == b);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4612</c> (R-4251 R-D11 A′ ②; R-2088; 📄 docs/DESIGN_Orders_Params_Entity_Classes.md §15) — a unit's <see cref="CurrentOrder"/>,
    /// DERIVED from its task slot: the running task when a superior or an operator started it, else the task a reaction PAUSED when one of them
    /// started that. Nothing is stored twice — the slot owns what runs and who started it, the start record and the paused task carry the request id —
    /// so it cannot disagree with what the unit does. Allocation-free; a read of another unit's slot is declared (<see cref="BrainReads"/>).
    /// </summary>
    public static class CurrentOrders
    {
        public static CurrentOrder Of(EntityRepository world, Entity unit)
        {
            if (!world.IsAlive(unit) || !world.HasComponent<BehaviorState>(unit)) return CurrentOrder.None;
            ref readonly var bs = ref world.GetComponentRO<BehaviorState>(unit);
            if (bs.ActiveBehaviorHash != BehaviorIds.None && IsOrderOrigin(bs.Origin))
                return new CurrentOrder(OrderKinds.Of(bs.ActiveBehaviorHash), bs.Origin, RunningRequestId(world, unit, bs.InstanceId), bs.ActiveBehaviorHash, false);
            if (bs.Origin == BehaviorOrigin.Reaction && world.TryGetTable(typeof(PausedTask), out _)
                && Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem.PausedTaskOf(world, unit) is { } paused
                && paused.Hash != BehaviorIds.None && IsOrderOrigin(paused.Origin))
                return new CurrentOrder(OrderKinds.Of(paused.Hash), paused.Origin, paused.RequestId, paused.Hash, true);
            return CurrentOrder.None;
        }

        /// <summary>A task a superior or an operator started is an order; an SOP's, a reaction's or the unit's own choice is not.</summary>
        public static bool IsOrderOrigin(BehaviorOrigin origin) => origin == BehaviorOrigin.Superior || origin == BehaviorOrigin.Operator;

        private static Guid RunningRequestId(EntityRepository world, Entity unit, uint instance)
        {
            if (!world.TryGetTable(typeof(BehaviorStartRecord), out _) || !world.HasManagedComponent<BehaviorStartRecord>(unit)) return Guid.Empty;
            var record = ((ISimulationView)world).GetManagedComponentRO<BehaviorStartRecord>(unit);
            return record != null && record.InstanceId == instance ? record.RequestId : Guid.Empty;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-4612</c> (R-2089) — a behaviour's <see cref="OrderKind"/>, by its registry hash: its <c>[Behavior(Fulfils = …)]</c>, else its params
    /// DTO's <c>[Order]</c> name (<see cref="ParamsDtoRegistry.OrderNameOf"/>). Built ONCE (<see cref="Build"/> — the loader and the brain phase's
    /// serial pre-pass call it; a first read builds it under a lock), immutable after; 0 = no kind.
    /// </summary>
    public static class OrderKinds
    {
        private static volatile Dictionary<int, OrderKind>? s_byHash;
        private static readonly object Gate = new();

        public static OrderKind Of(int behaviourHash)
        {
            var table = s_byHash ?? Build();
            return table.TryGetValue(behaviourHash, out var k) ? k : default;
        }

        /// <summary>Builds the table from the default params registry (idempotent). ⛔ A <c>Fulfils</c> that names no <see cref="OrderKind"/> throws.</summary>
        public static Dictionary<int, OrderKind> Build()
        {
            if (s_byHash != null) return s_byHash;
            lock (Gate)
            {
                if (s_byHash != null) return s_byHash;
                var registry = ParamsDtoRegistry.Default;
                var table = new Dictionary<int, OrderKind>();
                foreach (var d in registry.Behaviors)
                {
                    if (!string.IsNullOrEmpty(d.Fulfils) && !Enum.TryParse<OrderKind>(d.Fulfils, out _))
                        throw new InvalidOperationException(
                            $"CE-4612: [Behavior(\"{d.Name}\", Fulfils = \"{d.Fulfils}\")] names no OrderKind (docs/DESIGN_Orders_Params_Entity_Classes.md §15, R-2089).");
                    var name = registry.OrderNameOf(d.Name);
                    if (name != null && Enum.TryParse<OrderKind>(name, out var kind)) table[BehaviorHash.FromName(d.Name)] = kind;
                }
                s_byHash = table;
                return table;
            }
        }
    }
}
