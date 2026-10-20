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
using Fdp.Toolkit.Perception.Components;
using Hrot.AI.Behaviors.Orders;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-2308</c> (SP-1) — the spotter's order: the IF-3 mission's fields plus how far he looks and how long he rests.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SpotForFireParams
    {
        public ObserveFireParams Observe;
        public float MaxMetres;
        public float RestSeconds;
        /// <summary>0 = watching · 1 = a mission runs (<see cref="Observe"/>).</summary>
        public byte Observing;
        public double RestUntil;
        /// <summary>Missions started (a test / diagnostics probe).</summary>
        public int Missions;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2308</c> (SP-1…SP-5, Urban Battle §4 ⑥; 📄 docs/designs/indirect-fire/DESIGN_Indirect_Fire.md §3.11) — <c>SpotForFire</c>: an
    /// observer who picks his OWN target. He watches; when his memory holds a hostile within <see cref="SpotForFireParams.MaxMetres"/>
    /// (<see cref="FireTargets.Pick"/>) he runs the IF-3 mission on where it is (<see cref="PersonObserveFireBehavior.Observe"/>, unchanged);
    /// when it ends, either way, he rests <see cref="SpotForFireParams.RestSeconds"/> and watches again. Running for good.
    /// </summary>
    public static class SpotForFireBehavior
    {
        public const float DefaultMaxMetres = 1500f;
        public const float DefaultRestSeconds = 30f;

        [StructLayout(LayoutKind.Sequential)]
        public struct SpotForFireBlackboard
        {
            public SpotForFireParams Spot;
        }

        [BehaviorResolver(BehaviorNames.SpotForFire)]
        public static void Resolve(in SpotForFireOrder order, ref SpotForFireBlackboard block, EntityRepository world, Entity self)
        {
            block.Spot = new SpotForFireParams
            {
                Observe = new ObserveFireParams
                {
                    Battery = order.Battery.Resolve(world), Rounds = order.Rounds, Sheaf = (byte)order.Sheaf, SheafWidthM = order.SheafWidthM,
                    Control = (byte)order.Control, Locating = (byte)order.Locating,
                },
                MaxMetres = order.MaxMetres > 0f ? order.MaxMetres : DefaultMaxMetres,
                RestSeconds = order.RestSeconds > 0f ? order.RestSeconds : DefaultRestSeconds,
            };
        }

        [Behavior(BehaviorNames.SpotForFire, Params = typeof(SpotForFireOrder), Categories = BehaviorCategory.Infantry)]
        [BTreeDefinition(BehaviorNames.SpotForFire, Curated = true, Group = "Library/Orders", ParamsType = typeof(SpotForFireBlackboard))]
        public static BTreeBuilder<SpotForFireBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<SpotForFireBlackboard, BTreeContext>()
                .Action(bb => bb.Spot, Spot);

        /// <summary>One tick: watch and pick, or run the mission; rest after it.</summary>
        [SharedAiAction]
        public static NodeStatus Spot(ref SpotForFireParams p, Entity self, EntityRepository world)
        {
            if (p.Observe.Battery.IsNull || !world.IsAlive(p.Observe.Battery)) return NodeStatus.Failure;
            double now = HitModel.Now(world);
            if (p.Observing != 0)
            {
                var status = PersonObserveFireBehavior.Observe(ref p.Observe, self, world);
                if (status == NodeStatus.Running) return NodeStatus.Running;
                p.Observing = 0;
                p.RestUntil = now + p.RestSeconds;   // SP-4 — either end
                return NodeStatus.Running;
            }
            if (now < p.RestUntil || !world.HasComponent<SimTransform>(self)) return NodeStatus.Running;
            var target = FireTargets.Pick(world, self, p.MaxMetres, out var at);
            if (target.IsNull) return NodeStatus.Running;
            var battery = p.Observe.Battery;
            p.Observe = new ObserveFireParams
            {
                Target = at, Battery = battery, Rounds = p.Observe.Rounds, Sheaf = p.Observe.Sheaf, SheafWidthM = p.Observe.SheafWidthM,
                Control = p.Observe.Control, Locating = p.Observe.Locating,
            };
            p.Observing = 1;
            p.Missions++;
            var first = PersonObserveFireBehavior.Observe(ref p.Observe, self, world);
            if (first != NodeStatus.Running) { p.Observing = 0; p.RestUntil = now + p.RestSeconds; }
            return NodeStatus.Running;
        }
    }

    /// <summary>⭐ <c>CE-2308</c> (SP-2, SP-3) — what a spotter calls fire on, from HIS memory: an identified, live, hostile (another force, not
    /// neutral) contact within <c>maxMetres</c>, scored by the hostile contacts within <see cref="GroupMetres"/> of it (itself included), a
    /// vehicle counting <see cref="VehicleWeight"/>; the best, at the place it was SEEN (its remembered position).</summary>
    public static class FireTargets
    {
        public const float GroupMetres = 30f;
        public const float VehicleWeight = 2f;

        public static unsafe Entity Pick(EntityRepository world, Entity observer, float maxMetres, out Vector3 at)
        {
            at = default;
            if (!world.IsComponentTypeRegistered<TargetMemory>() || !world.HasComponent<TargetMemory>(observer)
                || !world.HasComponent<SimTransform>(observer)) return Entity.Null;
            ref readonly var memory = ref world.GetComponentRO<TargetMemory>(observer);
            var eye = world.GetComponentRO<SimTransform>(observer).Position;
            int n = Math.Min(memory.Count, 32);
            Span<int> live = stackalloc int[n];
            Span<float> weight = stackalloc float[n];
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                if (TargetMemory.IsAnonymous(in memory, i)) continue;
                var e = new Entity((ulong)memory.EntityIds[i]);
                if (!world.IsAlive(e) || !Injury.IsInTheFight(world, e) || !KillZone.Hostile(world, observer, e)) continue;
                if (new Vector2(memory.PositionsX[i] - eye.X, memory.PositionsY[i] - eye.Y).Length() > maxMetres) continue;
                live[k] = i;
                weight[k++] = Fdp.Toolkit.Spatial.Eqs.VehicleCover.IsVehicle(world, e) ? VehicleWeight : 1f;
            }
            int best = -1; float bestScore = 0f;
            for (int a = 0; a < k; a++)
            {
                var pa = new Vector2(memory.PositionsX[live[a]], memory.PositionsY[live[a]]);
                float score = 0f;
                for (int b = 0; b < k; b++)
                    if (Vector2.Distance(pa, new Vector2(memory.PositionsX[live[b]], memory.PositionsY[live[b]])) <= GroupMetres) score += weight[b];
                if (score > bestScore) { bestScore = score; best = a; }
            }
            if (best < 0) return Entity.Null;
            int slot = live[best];
            var target = new Entity((ulong)memory.EntityIds[slot]);
            float z = world.HasComponent<SimTransform>(target) ? world.GetComponentRO<SimTransform>(target).Position.Z : eye.Z;
            at = new Vector3(memory.PositionsX[slot], memory.PositionsY[slot], z);
            return target;
        }
    }
}

namespace Hrot.AI.Behaviors.Orders
{
    /// <summary>
    /// ⭐ <c>CE-2308</c> (SP-1; 📄 docs/designs/indirect-fire/DESIGN_Indirect_Fire.md §3.11) — the authored contract of <c>SpotForFire</c>: the
    /// battery he calls, how far he looks, the fire-for-effect mission (as <see cref="ObserveFireOrder"/>) and how long he rests between
    /// missions. A zero field keeps its default.
    /// </summary>
    [Fdp.Core.ParamsDto]
    [StructLayout(LayoutKind.Sequential)]
    public struct SpotForFireOrder
    {
        public const string BehaviorId = BehaviorNames.SpotForFire;

        /// <summary>The battery (or self-propelled gun) to call.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("Battery")]
        public Fdp.Toolkit.Replication.EntityRef Battery;

        /// <summary>He calls fire on what he sees within this distance (m); 0 = 1500.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("MaxMetres")]
        public float MaxMetres;

        /// <summary>The rest after each mission (s); 0 = 30.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("RestSeconds")]
        public float RestSeconds;

        /// <summary>How he locates a target (map / eye ≈ 150 m, laser ≈ 10 m).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("Locating")]
        public FireMissionLocating Locating;

        /// <summary>Fire for effect: rounds per gun; 0 = 3.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("Rounds")]
        public int Rounds;

        [System.Text.Json.Serialization.JsonPropertyName("Sheaf")]
        public FireMissionSheaf Sheaf;

        [System.Text.Json.Serialization.JsonPropertyName("SheafWidthM")]
        public float SheafWidthM;

        [System.Text.Json.Serialization.JsonPropertyName("Control")]
        public FireMissionControl Control;
    }
}
