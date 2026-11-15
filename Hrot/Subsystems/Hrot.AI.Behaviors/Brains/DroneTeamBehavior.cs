using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior;
using Hrot.AI.Behaviors.Orders;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-2436</c> AU-5 — the drone team's task: where its recon drone watches, where its armed drones strike, and whether the
    /// orders went out.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DroneTeamParams
    {
        public Vector3 Watch;
        public Vector3 Strike;
        /// <summary>1 once the drones and the operator have their orders (a new order to the team re-sends them).</summary>
        public byte Sent;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2436</c> AU-5 (A-6, R-2111; 📄 docs/designs/group-maneuvers/DESIGN_Assault_Under_Fire.md §9.6) — a company's DRONE TEAM in the
    /// attack (a squad: an operator and his drones). Its UNARMED drones are ordered <c>DroneRecon</c> over the objective — their sightings merge
    /// into the team's pool and the staff SPOTs them up, so the recon drone's reports ARE the company's target list (the observer chain,
    /// unchanged). Its operator is ordered <c>DroneOperator</c> with the ARMED drones on the strike point — one in the air at a time, the next
    /// when it is gone (CE-7026's rule, unchanged). A strike point of (0, 0, 0) leaves the operator idle. It runs until the company re-tasks it.
    /// <para>⭐ Parallel (R-4212): the commander changes only its own state; every drone and the operator get an ORDER (a command); the roster,
    /// the members' locomotion class and their type data are declared reads.</para>
    /// </summary>
    public static class DroneTeamBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Blackboard
        {
            public DroneTeamParams Team;
        }

        [BehaviorResolver(BehaviorNames.DroneTeam)]
        public static void Resolve(in DroneTeamParamsJsonDto authored, ref Blackboard block, EntityRepository world, Entity self)
            => block.Team = new DroneTeamParams { Watch = authored.Watch, Strike = authored.Strike };

        [Behavior(BehaviorNames.DroneTeam, Params = typeof(DroneTeamParamsJsonDto), Categories = BehaviorCategory.Commander,
                  Description = "A drone team watches the objective with its recon drone and strikes the reported posts with the others.")]
        [BTreeDefinition(BehaviorNames.DroneTeam, Curated = true, Group = "Library/Orders", ParamsType = typeof(Blackboard))]
        public static BTreeBuilder<Blackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<Blackboard, BTreeContext>()
                .Action(bb => bb.Team, Run);

        /// <summary>One tick: the orders out once; then it stands (Running) until re-tasked.</summary>
        [SharedAiAction]
        public static NodeStatus Run(ref DroneTeamParams p, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<UnitRoster>(self)) return NodeStatus.Failure;
            if (p.Sent != 0) return NodeStatus.Running;
            var json = Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;
            ref readonly var roster = ref world.GetComponentRO<UnitRoster>(self);
            Entity op = Entity.Null;
            int armed = 0;
            var order = new DroneOperatorOrder { Location = p.Strike };
            for (int i = 0; i < roster.Count; i++)
            {
                var m = roster.SubordinateEntities[i];
                if (m.IsNull || !world.IsAlive(m)) continue;
                if (!IsDrone(world, m))
                {
                    if (op.IsNull && Fdp.Toolkit.Squad.CombatEffective.IsInTheFight(world, m)) op = m;
                    continue;
                }
                if (TemplateArms.KnownUnarmed(world, m))
                {
                    CommandOrders.Assign(world, self, m, BehaviorNames.DroneRecon, JsonSerializer.Serialize(new DroneReconOrder { Location = p.Watch }, json));
                    continue;
                }
                if (armed >= 4) continue;
                int nid = Fdp.Toolkit.Replication.Services.NetworkIdResolver.RuntimeNetworkIdOf(world, m);
                if (nid == 0) continue;
                var link = new Fdp.Toolkit.Replication.EntityLink(new Fdp.Toolkit.Replication.EntityRef(nid));
                switch (armed++) { case 0: order.Drone1 = link; break; case 1: order.Drone2 = link; break; case 2: order.Drone3 = link; break; default: order.Drone4 = link; break; }
            }
            if (!op.IsNull && armed > 0 && p.Strike != Vector3.Zero)
                CommandOrders.Assign(world, self, op, BehaviorNames.DroneOperator, JsonSerializer.Serialize(order, json));
            p.Sent = 1;
            return NodeStatus.Running;
        }

        /// <summary>A drone: an aircraft member (the quadcopters) — the one rule, <see cref="Fdp.Toolkit.Motion.Locomotions.IsAircraft"/>.</summary>
        public static bool IsDrone(EntityRepository world, Entity unit) => Fdp.Toolkit.Motion.Locomotions.IsAircraft(world, unit);

        /// <summary>⭐ AU-5 — is <paramref name="unit"/> a DRONE TEAM: a unit whose roster holds a drone.</summary>
        public static bool IsDroneTeam(EntityRepository world, Entity unit)
        {
            if (unit.IsNull || !world.IsAlive(unit) || !world.HasComponent<UnitRoster>(unit)) return false;
            ref readonly var roster = ref world.GetComponentRO<UnitRoster>(unit);
            for (int i = 0; i < roster.Count; i++)
            {
                var m = roster.SubordinateEntities[i];
                if (!m.IsNull && world.IsAlive(m) && IsDrone(world, m)) return true;
            }
            return false;
        }
    }
}
