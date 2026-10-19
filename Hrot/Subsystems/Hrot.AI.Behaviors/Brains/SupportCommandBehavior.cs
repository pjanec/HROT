using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Squad.Command;
using Hrot.AI.Behaviors.Orders;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-2312</c> (HQ-6) — what an HQ's asset is, for the answers.</summary>
    public enum SupportAsset : byte { None = 0, AttackHelicopter = 1, StrikePlane = 2, Battery = 3, GroundUnit = 4 }

    /// <summary>⭐ <c>CE-2312</c> (HQ-6) — the HQ's task: how near an air defence counts, and how long a reported one stays a threat.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SupportCommandParams
    {
        public float AirDefenceMetres;
        public float AirDefenceFreshSeconds;
    }

    /// <summary>⭐ <c>CE-2312</c> — what the HQ keeps: the answers it gave, the air-defence strikes it ordered (a test / diagnostics probe).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SupportCommandState
    {
        public int Answered;
        public int AirDefenceStrikes;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2312</c> (HQ-6…HQ-8, Urban Battle §4 ①, §1 beats 6 and 8; 📄 docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md §8d)
    /// — <c>SupportCommand</c>, the headquarters' task: its roster holds the platoons and the support ASSETS; it answers the requests in its
    /// picture with what is FREE (an asset still running the order this run gave it is busy — <see cref="CommandOrders.Busy"/>, or ordered within <see cref="OrderGraceSeconds"/>).
    /// <b>FireSupport</b>: an air defence reported near the point ⇒ a strike plane on it first and the request waits; else an attack
    /// helicopter searches the point (<c>AirArmedSearch</c>), else a battery fires for effect on it. <b>Reinforce</b>: a free ground unit
    /// (not the requester) defends at the requester's point. Running for good. Nothing here reads another brain but the asset's busy state.
    /// </summary>
    public static class SupportCommandBehavior
    {
        public const float DefaultAirDefenceMetres = 2000f, DefaultAirDefenceFreshSeconds = 120f;
        /// <summary>The reinforcement's search radius at the point: a platoon, a squad (m).</summary>
        public const float PlatoonDefendMetres = 60f, SquadDefendMetres = 40f;

        [StructLayout(LayoutKind.Sequential)]
        public struct SupportCommandBlackboard
        {
            public SupportCommandParams Params;
            public SupportCommandState State;
        }

        [BehaviorResolver(BehaviorNames.SupportCommand)]
        public static void Resolve(in SupportCommandParamsJsonDto authored, ref SupportCommandBlackboard block, EntityRepository world, Entity self)
        {
            block.Params = new SupportCommandParams
            {
                AirDefenceMetres = authored.AirDefenceMetres > 0f ? authored.AirDefenceMetres : DefaultAirDefenceMetres,
                AirDefenceFreshSeconds = authored.AirDefenceFreshSeconds > 0f ? authored.AirDefenceFreshSeconds : DefaultAirDefenceFreshSeconds,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.SupportCommand, Params = typeof(SupportCommandParamsJsonDto), Categories = BehaviorCategory.Commander)]
        [BTreeDefinition(BehaviorNames.SupportCommand, Curated = true, Group = "Library/Platoon", ParamsType = typeof(SupportCommandBlackboard))]
        public static BTreeBuilder<SupportCommandBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<SupportCommandBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.State, Command);

        /// <summary>One tick: answer the oldest FireSupport, then the oldest Reinforce, with what is free.</summary>
        public static NodeStatus Command(ref SupportCommandParams p, ref SupportCommandState ws, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<CommandPicture>(self)) return NodeStatus.Running;
            var json = Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;
            float now = CommandPictureOps.Now(world);

            int fire = CommandPictureOps.FirstRequest(world, self, RequestKind.FireSupport);
            if (fire >= 0)
            {
                var point = world.GetComponentRO<CommandPicture>(self).Requests.Items[fire].At;
                if (AirDefenceNear(world, self, point, p.AirDefenceMetres, p.AirDefenceFreshSeconds, now, out var aa))
                {
                    // HQ-7 — the air defence first; the request waits while it stays reported
                    var plane = Free(world, self, SupportAsset.StrikePlane, Entity.Null);
                    if (!plane.IsNull)
                    {
                        CommandOrders.Order(world, self, plane, nameof(OrderKind.Strike),
                            JsonSerializer.Serialize(new StrikeOrder { Location = OrderGeo.ToGeo(world, aa) }, json), newOrder: true);
                        ws.AirDefenceStrikes++;
                    }
                }
                else
                {
                    var heli = Free(world, self, SupportAsset.AttackHelicopter, Entity.Null);
                    var battery = heli.IsNull ? Free(world, self, SupportAsset.Battery, Entity.Null) : Entity.Null;
                    if (!heli.IsNull)
                        CommandOrders.Assign(world, self, heli, BehaviorNames.AirArmedSearch,
                            JsonSerializer.Serialize(new AirArmedSearchOrder { Location = OrderGeo.ToGeo(world, point) }, json), newOrder: true);
                    else if (!battery.IsNull)
                        CommandOrders.Order(world, self, battery, nameof(OrderKind.Strike),
                            JsonSerializer.Serialize(new StrikeOrder { Location = OrderGeo.ToGeo(world, point), Method = FireMissionMethod.FireForEffect }, json),
                            newOrder: true);
                    if (!heli.IsNull || !battery.IsNull)
                    {
                        CommandPictureOps.Answered(world, self, fire);
                        ws.Answered++;
                    }
                }
            }

            int help = CommandPictureOps.FirstRequest(world, self, RequestKind.Reinforce);
            if (help >= 0)
            {
                var request = world.GetComponentRO<CommandPicture>(self).Requests.Items[help];
                var from = Fdp.Toolkit.Replication.Services.NetworkIdResolver.ResolveNetworkId(world, request.FromNetId);
                if (from.IsNull) from = SubordinateByNetId(world, self, request.FromNetId);
                var unit = Free(world, self, SupportAsset.GroundUnit, from);
                if (!unit.IsNull)
                {
                    bool platoon = IsUnitOfUnits(world, unit);
                    string behaviour = platoon ? BehaviorNames.PlatoonDefend : BehaviorNames.SquadDefend;
                    string order = platoon
                        ? JsonSerializer.Serialize(new PlatoonDefendParamsJsonDto { Position = request.At, SearchRadius = PlatoonDefendMetres }, json)
                        : JsonSerializer.Serialize(new SquadDefendParamsJsonDto { Position = request.At, SearchRadius = SquadDefendMetres }, json);
                    CommandOrders.Assign(world, self, unit, behaviour, order, newOrder: true);
                    CommandPictureOps.Answered(world, self, help);
                    ws.Answered++;
                }
            }
            return NodeStatus.Running;
        }

        /// <summary>HQ-7 — a fresh AirDefence element within <paramref name="metres"/> of <paramref name="point"/>, the nearest.</summary>
        private static bool AirDefenceNear(EntityRepository world, Entity self, Vector3 point, float metres, float freshSeconds, float now, out Vector3 at)
        {
            at = default;
            ref readonly var picture = ref world.GetComponentRO<CommandPicture>(self);
            float best = float.MaxValue;
            for (int i = 0; i < picture.Elements.Count; i++)
            {
                ref readonly var e = ref picture.Elements.Items[i];
                if (e.Count == 0 || (e.Equipment & ElementEquipment.AirDefence) == 0 || now - e.ObservedAt > freshSeconds) continue;
                float d = Vector2.Distance(new Vector2(e.At.X, e.At.Y), new Vector2(point.X, point.Y));
                if (d <= metres && d < best) { best = d; at = e.At; }
            }
            return best < float.MaxValue;
        }

        /// <summary>The first live, free asset of <paramref name="kind"/> in the HQ's roster (never <paramref name="not"/>), or Null.</summary>
        public static Entity Free(EntityRepository world, Entity self, SupportAsset kind, Entity not)
        {
            if (!world.HasComponent<UnitRoster>(self)) return Entity.Null;
            ref readonly var roster = ref world.GetComponentRO<UnitRoster>(self);
            for (int i = 0; i < roster.Count; i++)
            {
                var a = roster.SubordinateEntities[i];
                if (a.IsNull || a.Equals(not) || !world.IsAlive(a) || !CombatLife.IsAlive(world, a)) continue;
                if (KindOf(world, a) != kind || CommandOrders.Busy(world, self, a) || JustOrdered(world, self, a)) continue;
                return a;
            }
            return Entity.Null;
        }

        /// <summary>An order given this recently has not reached the asset's brain yet (it runs from the next frame): still busy (s).</summary>
        public const float OrderGraceSeconds = 2f;

        private static bool JustOrdered(EntityRepository world, Entity self, Entity a)
        {
            if (!world.HasComponent<CommandPicture>(self)) return false;
            ref readonly var picture = ref world.GetComponentRO<CommandPicture>(self);
            int row = CommandPictureOps.RowOf(picture, CommandPictureOps.NetIdOf(world, a));
            return row >= 0 && picture.Subordinates.Items[row].OrderedAt > 0f
                   && CommandPictureOps.Now(world) - picture.Subordinates.Items[row].OrderedAt < OrderGraceSeconds;
        }

        /// <summary>HQ-6 — an asset's kind: an armed rotorcraft · an armed fixed-wing · an artillery battery unit · another unit.</summary>
        public static SupportAsset KindOf(EntityRepository world, Entity a)
        {
            if (SquadManeuverRunner.IsUnit(world, a))
                return world.HasComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(a)
                       && world.GetComponentRO<Fdp.Toolkit.Replication.Components.TkbIdentity>(a).TkbType == Hrot.Map.Common.TkbEntityTypes.Unit_ArtilleryBattery
                    ? SupportAsset.Battery : SupportAsset.GroundUnit;
            if (CombatTkb.MountOf(world, a, 0) == null) return SupportAsset.None;
            var loco = Fdp.Toolkit.Motion.Locomotions.Of(world, a);
            return loco == Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft ? SupportAsset.AttackHelicopter
                 : loco == Fdp.Toolkit.Tkb.Domain.LocomotionClass.FixedWing ? SupportAsset.StrikePlane : SupportAsset.None;
        }

        private static bool IsUnitOfUnits(EntityRepository world, Entity unit)
        {
            if (!world.HasComponent<UnitRoster>(unit)) return false;
            ref readonly var roster = ref world.GetComponentRO<UnitRoster>(unit);
            for (int i = 0; i < roster.Count; i++)
                if (SquadManeuverRunner.IsUnit(world, roster.SubordinateEntities[i])) return true;
            return false;
        }

        /// <summary>A roster member whose picture id is <paramref name="netId"/> (a world with no network: its handle).</summary>
        private static Entity SubordinateByNetId(EntityRepository world, Entity self, long netId)
        {
            if (!world.HasComponent<UnitRoster>(self)) return Entity.Null;
            ref readonly var roster = ref world.GetComponentRO<UnitRoster>(self);
            for (int i = 0; i < roster.Count; i++)
                if (CommandPictureOps.NetIdOf(world, roster.SubordinateEntities[i]) == netId) return roster.SubordinateEntities[i];
            return Entity.Null;
        }
    }
}

namespace Hrot.AI.Behaviors.Orders
{
    /// <summary>⭐ <c>CE-2312</c> (HQ-6) — the authored contract of the HQ's <c>SupportCommand</c>. A zero field keeps its default.</summary>
    [Fdp.Core.ParamsDto]
    [StructLayout(LayoutKind.Sequential)]
    public struct SupportCommandParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.SupportCommand;

        /// <summary>An air defence reported within this of the fire support's point is struck first (m); 0 = 2000.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("AirDefenceMetres")]
        public float AirDefenceMetres;

        /// <summary>A reported air defence is a threat this long after it was observed (s); 0 = 120.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("AirDefenceFreshSeconds")]
        public float AirDefenceFreshSeconds;
    }
}
