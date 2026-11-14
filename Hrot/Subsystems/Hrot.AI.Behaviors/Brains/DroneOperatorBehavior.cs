using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Combat.Components;
using Hrot.AI.Behaviors.Orders;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-7026</c> — the operator's stock (up to four drones) and where he sends them; his working state.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DroneOperatorParams
    {
        public Entity Drone0, Drone1, Drone2, Drone3;
        public Vector3 Point;
        public float IntervalSeconds;
        /// <summary>The stock index in the air (<see cref="DroneOperatorBehavior.None"/> = none).</summary>
        public byte Current;
        /// <summary>Bit k = stock drone k was launched (and is not to be sent again).</summary>
        public byte Launched;
        public SimInstant NextAt;
        /// <summary>⭐ <c>CE-7027</c> — sorties flown, 8 bits a stock drone (drone k in bits 8k…8k+7).</summary>
        public uint Sorties;
        /// <summary>⭐ <c>CE-7027</c> — 1 once the drone in the air was seen running its order (until then "not busy" means "not yet").</summary>
        public byte SeenBusy;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-7026</c> (City Battle §11 R-1a) — the DRONE OPERATOR: one drone in the air at a time. He LAUNCHES the next drone of his stock — an
    /// authored drone landed beside him (on its gear, <c>Airborne = 0</c>) — by ORDERING it to strike the area (<c>AirPlatformStrike</c>: the
    /// FPV's Dive profile takes off, searches the point, picks, dives); once it is gone (detonated, shot down, its battery out) he launches the
    /// next after <see cref="DefaultIntervalSeconds"/>; the stock spent and none in the air ⇒ Success.
    /// <para>⚠ DEVIATION from the row ("a launch creates the drone through the ELM"), lane default R-7007: the stock is AUTHORED drones, so a
    /// launch is an order — no runtime creation, no new component, no new system; a scenario places a team's drones beside its operator.</para>
    /// <para>⭐ Parallel (R-4212): the operator changes only his own state; a drone gets an ORDER (<see cref="CommandOrders.Assign"/>, a command);
    /// the drone's liveness (<see cref="Health"/>) is a declared read.</para>
    /// </summary>
    public static class DroneOperatorBehavior
    {
        public const byte None = 0xFF;
        public const float DefaultIntervalSeconds = 20f;

        [StructLayout(LayoutKind.Sequential)]
        public struct Blackboard
        {
            public DroneOperatorParams Operate;
        }

        [BehaviorResolver(BehaviorNames.DroneOperator)]
        public static void Resolve(in DroneOperatorOrder order, ref Blackboard block, EntityRepository world, Entity self)
        {
            var at = (Vector3)order.Location;
            block.Operate = new DroneOperatorParams
            {
                Drone0 = order.Drone1.Ref.Resolve(world), Drone1 = order.Drone2.Ref.Resolve(world),
                Drone2 = order.Drone3.Ref.Resolve(world), Drone3 = order.Drone4.Ref.Resolve(world),
                Point = new Vector3(at.X, at.Y, Fdp.Toolkit.World.WorldQuery.Of(world)?.GroundHeightAt(at.X, at.Y) ?? at.Z),
                IntervalSeconds = order.IntervalSeconds > 0f ? order.IntervalSeconds : DefaultIntervalSeconds,
                Current = None,
            };
        }

        [Behavior(BehaviorNames.DroneOperator, Params = typeof(DroneOperatorOrder), Categories = BehaviorCategory.Infantry,
                  Description = "Launches his drones one at a time at an area (a drone operator).")]
        [BTreeDefinition(BehaviorNames.DroneOperator, Curated = true, Group = "Library/Combat", ParamsType = typeof(Blackboard))]
        public static BTreeBuilder<Blackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<Blackboard, BTreeContext>()
                .Action(bb => bb.Operate, Operate);

        /// <summary>One tick: the drone in the air still flies ⇒ wait; it is gone ⇒ the next after the interval; none left ⇒ Success.</summary>
        [SharedAiAction]
        public static NodeStatus Operate(ref DroneOperatorParams p, Entity self, EntityRepository world)
        {
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;
            if (p.Current != None)
            {
                var flying = Stock(in p, p.Current);
                if (Flying(world, flying))
                {
                    // ⭐ CE-7027 — a drone that ENDED its order alive (the bomber back from its drop, a recon down on its battery) is done
                    //   too; one with rounds left for another sortie is sent again (the bomber's second grenade)
                    if (CommandOrders.Busy(world, self, flying)) { p.SeenBusy = 1; return NodeStatus.Running; }
                    if (p.SeenBusy == 0) return NodeStatus.Running;   // the order has not reached it yet
                    if (SortiesOf(in p, p.Current) < Math.Max(1, TemplateArms.Rounds(world, flying)))
                        p.Launched &= (byte)~(1 << p.Current);
                }
                p.Current = None;
                p.SeenBusy = 0;
                p.NextAt = (SimInstant)(now + p.IntervalSeconds);
            }
            if (now < p.NextAt) return NodeStatus.Running;
            for (byte k = 0; k < 4; k++)
            {
                if ((p.Launched & (1 << k)) != 0) continue;
                var drone = Stock(in p, k);
                if (!Flying(world, drone)) { p.Launched |= (byte)(1 << k); continue; }   // lost before it flew: skip it
                // ⭐ CE-7028 — an UNARMED drone (no mount: the recon quadcopter) is sent to watch the area; an armed one to strike it
                if (Unarmed(world, drone))
                    CommandOrders.Assign(world, self, drone, BehaviorNames.DroneRecon,
                        JsonSerializer.Serialize(new DroneReconOrder { Location = p.Point }, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed), newOrder: true);
                else
                    CommandOrders.Assign(world, self, drone, BehaviorNames.AirPlatformStrike,
                        JsonSerializer.Serialize(new StrikeOrder { Location = p.Point }, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed), newOrder: true);
                p.Launched |= (byte)(1 << k);
                p.Sorties += 1u << (8 * k);
                p.Current = k;
                return NodeStatus.Running;
            }
            return NodeStatus.Success;
        }

        /// <summary>⭐ CE-7028 — a drone whose KNOWN type carries no weapon mount (the recon quadcopter); an unknown type keeps the strike.</summary>
        private static bool Unarmed(EntityRepository world, Entity drone) => TemplateArms.KnownUnarmed(world, drone);

        private static int SortiesOf(in DroneOperatorParams p, int k) => (int)((p.Sorties >> (8 * k)) & 0xFF);

        private static Entity Stock(in DroneOperatorParams p, int k) => k switch { 0 => p.Drone0, 1 => p.Drone1, 2 => p.Drone2, _ => p.Drone3 };

        /// <summary>A drone that can still fly: alive, not destroyed.</summary>
        private static bool Flying(EntityRepository world, Entity drone)
            => !drone.IsNull && world.IsAlive(drone)
               && !(world.HasComponent<Health>(drone) && world.GetComponentRO<Health>(drone).Current <= 0f);
    }

    /// <summary>⭐ <c>CE-7026</c> — the operator's order: his stock (up to four drones landed beside him) and the area to send them; a zero field keeps its default.</summary>
    [Fdp.Core.ParamsDto]
    [StructLayout(LayoutKind.Sequential)]
    public struct DroneOperatorOrder
    {
        public const string BehaviorId = BehaviorNames.DroneOperator;

        [System.Text.Json.Serialization.JsonPropertyName("Drone1")] public Fdp.Toolkit.Replication.EntityLink Drone1;
        [System.Text.Json.Serialization.JsonPropertyName("Drone2")] public Fdp.Toolkit.Replication.EntityLink Drone2;
        [System.Text.Json.Serialization.JsonPropertyName("Drone3")] public Fdp.Toolkit.Replication.EntityLink Drone3;
        [System.Text.Json.Serialization.JsonPropertyName("Drone4")] public Fdp.Toolkit.Replication.EntityLink Drone4;

        /// <summary>The area the drones search and strike.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("Location")] public Fdp.Toolkit.Replication.PointLink Location;

        /// <summary>Seconds between one drone gone and the next launched; 0 = 20.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("IntervalSeconds")] public float IntervalSeconds;
    }
}
