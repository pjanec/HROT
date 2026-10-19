using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Components;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-2306</c> (DA-93…DA-95, Urban Battle §4 ④; 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §8e) —
    /// <c>TriggerIed</c>: the trigger man takes his phone in hand, stands still and watches; when HIS memory holds a hostile vehicle (or, with
    /// <see cref="TriggerIedParams.AnyTarget"/>, anyone hostile) within the kill zone of the charge, he asks it to go off (<see cref="Charges.Ask"/>,
    /// by himself — HI-6's phone). Success once asked; Failure with no phone, no charge, or the timeout. ⛔ It owns no burst (the charge's
    /// position owner bursts it) and reads no ground truth (DA-95: blind or kill him and the IED stays).
    /// </summary>
    public static class TriggerIedBehavior
    {
        public const float DefaultKillZoneMetres = 10f;

        [StructLayout(LayoutKind.Sequential)]
        public struct TriggerIedParams
        {
            /// <summary>The charge's network id (resolved each tick: a load re-allocates handles).</summary>
            public long Charge;
            public float KillZoneMetres;
            public byte AnyTarget;
            public float TimeoutSeconds;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TriggerIedState
        {
            /// <summary>When he began to watch (0 = not yet).</summary>
            public double Since;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TriggerIedBlackboard
        {
            public TriggerIedParams Params;
            public TriggerIedState State;
        }

        [BehaviorResolver(BehaviorNames.TriggerIed)]
        public static void Resolve(in TriggerIedParamsJsonDto authored, ref TriggerIedBlackboard block, EntityRepository world, Entity self)
        {
            block.Params = new TriggerIedParams
            {
                Charge = authored.Charge.NetworkId,
                KillZoneMetres = authored.KillZoneMetres > 0f ? authored.KillZoneMetres : DefaultKillZoneMetres,
                AnyTarget = authored.AnyTarget ? (byte)1 : (byte)0,
                TimeoutSeconds = Math.Max(0f, authored.TimeoutSeconds),
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.TriggerIed, Params = typeof(TriggerIedParamsJsonDto), Categories = BehaviorCategory.AllMilitary | BehaviorCategory.Civilian)]
        [BTreeDefinition(BehaviorNames.TriggerIed, Curated = true, Group = "Library/Orders", ParamsType = typeof(TriggerIedBlackboard))]
        public static BTreeBuilder<TriggerIedBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<TriggerIedBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.State, Watch);

        /// <summary>One tick: the phone in hand, still; a target in the kill zone ⇒ ask the charge. Success once asked.</summary>
        public static NodeStatus Watch(ref TriggerIedParams p, ref TriggerIedState ws, Entity self, EntityRepository world)
        {
            var charge = new Fdp.Toolkit.Replication.EntityRef(p.Charge).Resolve(world);
            if (charge.IsNull || !Charges.IsCharge(world, charge) || !world.HasComponent<SimTransform>(charge)) return NodeStatus.Failure;
            if (HeldItems.Of(world, self) != HeldItemKind.Phone && !HeldItems.Set(world, self, HeldItemKind.Phone)) return NodeStatus.Failure;
            double now = world.SimulationTime > 0 ? world.SimulationTime : 1e-6;
            if (ws.Since <= 0) ws.Since = now;
            LocomotionMoveTo.Stop(world, self);
            var at = world.GetComponentRO<SimTransform>(charge).Position;
            if (KillZone.Entered(world, self, at, p.KillZoneMetres, vehiclesOnly: p.AnyTarget == 0))
                return Charges.Ask(world, charge, self) ? NodeStatus.Success : NodeStatus.Failure;
            if (p.TimeoutSeconds > 0f && now - ws.Since >= p.TimeoutSeconds) return NodeStatus.Failure;
            return NodeStatus.Running;
        }
    }

    /// <summary>⭐ <c>CE-2306</c> (DA-93…DA-95) — "is a target in the kill zone?", from what the WATCHER remembers: an identified, live,
    /// hostile (another force, not neutral) contact within <c>metres</c> (flat) of the point; with <c>vehiclesOnly</c>, a vehicle.</summary>
    public static class KillZone
    {
        public static unsafe bool Entered(EntityRepository world, Entity watcher, Vector3 at, float metres, bool vehiclesOnly)
        {
            if (!world.IsComponentTypeRegistered<TargetMemory>() || !world.HasComponent<TargetMemory>(watcher)) return false;
            ref readonly var memory = ref world.GetComponentRO<TargetMemory>(watcher);
            for (int i = 0; i < memory.Count; i++)
            {
                if (TargetMemory.IsAnonymous(in memory, i)) continue;   // heard, never identified: he cannot tell what it is
                if (new Vector2(memory.PositionsX[i] - at.X, memory.PositionsY[i] - at.Y).Length() > metres) continue;
                var e = new Entity((ulong)memory.EntityIds[i]);
                if (!world.IsAlive(e) || !CombatLife.IsAlive(world, e) || !Hostile(world, watcher, e)) continue;
                if (vehiclesOnly && !Fdp.Toolkit.Spatial.Eqs.VehicleCover.IsVehicle(world, e)) continue;
                return true;
            }
            return false;
        }

        private static bool Hostile(EntityRepository world, Entity a, Entity b)
        {
            if (!world.IsComponentTypeRegistered<EntityInfo>() || !world.HasComponent<EntityInfo>(a) || !world.HasComponent<EntityInfo>(b)) return false;
            var mine = world.GetComponentRO<EntityInfo>(a).ForceId;
            var theirs = world.GetComponentRO<EntityInfo>(b).ForceId;
            return theirs != mine && theirs != ForceId.Neutral;
        }
    }
}
