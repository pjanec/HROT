using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Combat;
using Hrot.AI.Behaviors.Orders;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-2311</c> (HQ-9) — the armed search: where, how wide, how long a quiet sky over it ends the search.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AirArmedSearchParams
    {
        public Vector3 Centre;
        public float RadiusM;
        public SimDuration QuietSeconds;
        public float HeightAgl;
    }

    /// <summary>⭐ <c>CE-2311</c> — the search's working state: flying to / over the area (0) or attacking a pick (1).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AirArmedSearchState
    {
        public byte Stage;
        public FlyToParams Fly;
        /// <summary>When it last arrived over the area or ended an attack (0 = not yet over it): the quiet runs from here.</summary>
        public SimInstant QuietSince;
        public AirAttackParams Attack;
        public AirAttackState AttackState;
        /// <summary>Targets destroyed (a test / diagnostics probe).</summary>
        public int Kills;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2311</c> (HQ-9, Urban Battle §4 ③; 📄 docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md §8d) — <c>AirArmedSearch</c>:
    /// an aircraft searches an AREA for targets — it flies there (<see cref="AirOrderNodes.FlyTo"/>), and on the way and over it picks from
    /// its OWN memory within the radius (<see cref="DroneTargets.Pick"/>); a pick is attacked (<see cref="AirAttackNodes.Run"/> on the entity)
    /// until destroyed, then it picks again; nothing over the area for <see cref="AirArmedSearchParams.QuietSeconds"/> ⇒ Success. Failure
    /// when it cannot fly there or its attack fails (no weapon left).
    /// </summary>
    public static class AirArmedSearchBehavior
    {
        public const float DefaultRadiusM = 400f, DefaultQuietSeconds = 60f, DefaultHeightAgl = 150f;

        [StructLayout(LayoutKind.Sequential)]
        public struct AirArmedSearchBlackboard
        {
            public AirArmedSearchParams Search;
            public AirArmedSearchState State;
        }

        [BehaviorResolver(BehaviorNames.AirArmedSearch)]
        public static void Resolve(in AirArmedSearchOrder order, ref AirArmedSearchBlackboard block, EntityRepository world, Entity self)
        {
            var at = (System.Numerics.Vector3)order.Location;
            block.Search = new AirArmedSearchParams
            {
                Centre = new Vector3(at.X, at.Y, Fdp.Toolkit.World.WorldQuery.Of(world)?.GroundHeightAt(at.X, at.Y) ?? 0f),
                RadiusM = order.RadiusM > 0f ? order.RadiusM : DefaultRadiusM,
                QuietSeconds = order.QuietSeconds > 0f ? order.QuietSeconds : DefaultQuietSeconds,
                HeightAgl = order.HeightAgl > 0f ? order.HeightAgl : DefaultHeightAgl,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.AirArmedSearch, Params = typeof(AirArmedSearchOrder), Categories = BehaviorCategory.MilitaryApc, Classes = new[] { "AirPlatform" })]
        [BTreeDefinition(BehaviorNames.AirArmedSearch, Curated = true, Group = "Library/Combat", ParamsType = typeof(AirArmedSearchBlackboard))]
        public static BTreeBuilder<AirArmedSearchBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<AirArmedSearchBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Search, bb => bb.State, Search);

        /// <summary>One tick: attack the pick, or pick, or fly / hold over the area; see the type.</summary>
        public static NodeStatus Search(ref AirArmedSearchParams p, ref AirArmedSearchState ws, Entity self, EntityRepository world)
        {
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;
            if (ws.Stage == 1)
            {
                // ⭐ CE-10005 (R-10022) — two CAP pilots on one bandit: the higher-ranked one yields it when another bandit is free
                if (AirCombatNodes.IsFighter(world, self) && world.HasComponent<EntityInfo>(self)
                    && Fdp.Toolkit.Motion.AirArenaSystem.TakenByLowerRank(world, self, world.GetComponentRO<EntityInfo>(self).ForceId, ws.Attack.Target))
                {
                    var other = DroneTargets.Pick(world, self, p.Centre, p.RadiusM);
                    if (!other.IsNull && other != ws.Attack.Target
                        && !Fdp.Toolkit.Motion.AirArenaSystem.TakenByAnother(world, self, world.GetComponentRO<EntityInfo>(self).ForceId, other))
                    {
                        ws.Attack = new AirAttackParams { Target = other, Point = world.GetComponentRO<SimTransform>(other).Position };
                        ws.AttackState = default;
                    }
                }
                var status = AirAttackNodes.Run(ref ws.Attack, ref ws.AttackState, self, world);
                if (status == NodeStatus.Running) return NodeStatus.Running;
                if (status == NodeStatus.Failure) return NodeStatus.Failure;
                ws.Kills++;
                ws.Stage = 0;
                ws.Fly.Issued = 0;                     // back over the area
                ws.QuietSince = (SimInstant)(Math.Max(now, 1e-6));
                return NodeStatus.Running;
            }
            // ⭐ CE-10005 (AC-13, R-10022) — a FIGHTER on this search is on CAP: its radar searches (it is how it sees bandits beyond its eyes)
            if (AirCombatNodes.IsFighter(world, self)) AirCombatNodes.Radar(world, self, Entity.Null, now);
            var pick = DroneTargets.Pick(world, self, p.Centre, p.RadiusM);
            if (!pick.IsNull)
            {
                ws.Attack = new AirAttackParams { Target = pick, Point = world.GetComponentRO<SimTransform>(pick).Position };
                ws.AttackState = default;
                ws.Stage = 1;
                return NodeStatus.Running;
            }
            if (ws.Fly.Issued == 0) ws.Fly = new FlyToParams { X = p.Centre.X, Y = p.Centre.Y, HeightAgl = p.HeightAgl };
            var fly = AirOrderNodes.FlyTo(ref ws.Fly, self, world);
            if (fly == NodeStatus.Failure) return NodeStatus.Failure;
            if (fly == NodeStatus.Success && ws.QuietSince <= 0) ws.QuietSince = (SimInstant)(Math.Max(now, 1e-6));   // over the area
            if (ws.QuietSince > 0 && now - ws.QuietSince >= p.QuietSeconds) return NodeStatus.Success;  // the area is quiet
            return NodeStatus.Running;
        }
    }
}

namespace Hrot.AI.Behaviors.Orders
{
    /// <summary>
    /// ⭐ <c>CE-2311</c> (HQ-9; 📄 docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md §8d) — the authored contract of
    /// <c>AirArmedSearch</c>: search the area round <see cref="Location"/> for targets and attack what is found. A zero field keeps its default.
    /// </summary>
    [Fdp.Core.ParamsDto]
    [StructLayout(LayoutKind.Sequential)]
    public struct AirArmedSearchOrder
    {
        public const string BehaviorId = BehaviorNames.AirArmedSearch;

        /// <summary>The area's centre (geodetic).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("Location")]
        public Fdp.Toolkit.Replication.PointLink Location;

        /// <summary>The area's radius (m); 0 = 400.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("RadiusM")]
        public float RadiusM;

        /// <summary>Nothing found over the area this long ends the search (s); 0 = 60.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("QuietSeconds")]
        public SimDuration QuietSeconds;

        /// <summary>The search height above the ground (m); 0 = 150.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("HeightAgl")]
        public float HeightAgl;
    }
}
