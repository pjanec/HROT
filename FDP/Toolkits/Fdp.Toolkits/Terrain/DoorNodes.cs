using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;
using Fdp.Toolkit.Navigation;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>The params of <see cref="DoorNodes.MoveToDoor"/>: which door, how fast.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MoveToDoorParams
    {
        public TerrainObjectRef Door;
        /// <summary>Walking speed (m/s); 0 ⇒ <see cref="DoorNodes.DefaultSpeed"/>.</summary>
        public float Speed;
        /// <summary>Runtime: the locomotion activation this node started (0 = none) — never authored.</summary>
        [JsonIgnore] public uint Started;
    }

    /// <summary>The params of <see cref="DoorNodes.OperateDoor"/>: which door, which verb.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct OperateDoorParams
    {
        public TerrainObjectRef Door;
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public DoorVerb Verb;
        /// <summary>Runtime: the interaction activation this node started (0 = none) — never authored.</summary>
        [JsonIgnore] public uint Started;
    }

    /// <summary>
    /// ⭐⭐ Buildings Stage 5d-2 (📄 docs/DESIGN_Building_Interiors.md §3j "5d-2 as built") — a behaviour names a DOOR by its terrain
    /// key and acts on it. Two shared nodes (BTree, HSM, blueprint — CE-504), composable: walk to it, then any verb.
    /// <list type="bullet">
    ///   <item><description><see cref="MoveToDoor"/> — walks to the doorway's near side (on the actor's side of the wall) through the
    ///   locomotion channel; Success once the actor is within <see cref="DoorRules.ReachMetres"/>.</description></item>
    ///   <item><description><see cref="OperateDoor"/> — puts the verb's door action (ids 4–8) and the resolved door on the
    ///   interaction channel; the <see cref="DoorActionExecutor"/> does the rest (reach, action time, ONE command to the owner,
    ///   the replicated answer) and this node forwards its result.</description></item>
    /// </list>
    /// ⛔ A key that resolves to no door on this node fails the node (logged) — never a silent Running.
    /// </summary>
    /// <summary>Log category of the door nodes.</summary>
    public sealed class DoorPassageLog { }

    public static class DoorNodes
    {
        /// <summary>Walking speed when the params give none (m/s).</summary>
        public const float DefaultSpeed = 1.5f;
        /// <summary>How far off the doorway centre (m, on the actor's side) the walk aims.</summary>
        public const float ApproachMetres = 1.0f;

        [SharedAiAction]
        public static unsafe NodeStatus MoveToDoor(ref MoveToDoorParams p, Entity self, EntityRepository world)
        {
            var door = p.Door.Resolve(world);
            if (door.IsNull || !world.HasComponent<DoorState>(door))
            {
                Fdp.Core.Logging.FdpLog<DoorPassageLog>.Warn($"MoveToDoor: no door '{p.Door}' on this node." + $" (entity {self.Index})");
                return NodeStatus.Failure;
            }
            if (DoorActionExecutor.InReach(world, self, door)) { p.Started = 0; return NodeStatus.Success; }
            if (!world.HasComponent<LocomotionChannel>(self) || !world.HasComponent<SimTransform>(self))
            {
                Fdp.Core.Logging.FdpLog<DoorPassageLog>.Warn("MoveToDoor: the entity has no LocomotionChannel / SimTransform." + $" (entity {self.Index})");
                return NodeStatus.Failure;
            }

            ref var channel = ref world.GetComponentRW<LocomotionChannel>(self);
            if (p.Started != 0 && channel.ActionInstanceId == p.Started && channel.ActiveAction == NavigationConstants.ActionIdMoveTo)
            {
                if (channel.Status == NodeStatus.Running) return NodeStatus.Running;
                p.Started = 0;
                return NodeStatus.Failure;   // the walk ended (arrived or failed) and the actor is still out of reach
            }

            var at = world.GetComponentRO<SimTransform>(self).Position;
            if (world.HasComponent<BehaviorState>(self)) channel.BehaviorInstanceId = world.GetComponent<BehaviorState>(self).InstanceId;
            unchecked { channel.ActionInstanceId++; if (channel.ActionInstanceId == 0) channel.ActionInstanceId = 1; }
            channel.ActiveAction = NavigationConstants.ActionIdMoveTo;
            channel.Status = NodeStatus.Running;
            Unsafe.As<byte, MoveToParams>(ref channel.Params[0]) = new MoveToParams
            {
                Destination = ApproachPoint(world, door, p.Door, new Vector2(at.X, at.Y)),
                ArrivalRadius = 0.5f,
                Speed = p.Speed > 0f ? p.Speed : DefaultSpeed,
            };
            p.Started = channel.ActionInstanceId;
            return NodeStatus.Running;
        }

        [SharedAiAction]
        public static unsafe NodeStatus OperateDoor(ref OperateDoorParams p, Entity self, EntityRepository world)
        {
            var door = p.Door.Resolve(world);
            if (door.IsNull || !world.HasComponent<DoorState>(door))
            {
                Fdp.Core.Logging.FdpLog<DoorPassageLog>.Warn($"OperateDoor: no door '{p.Door}' on this node." + $" (entity {self.Index})");
                return NodeStatus.Failure;
            }
            if (!world.HasComponent<InteractionChannel>(self))
            {
                Fdp.Core.Logging.FdpLog<DoorPassageLog>.Warn("OperateDoor: the entity has no InteractionChannel." + $" (entity {self.Index})");
                return NodeStatus.Failure;
            }

            ref var channel = ref world.GetComponentRW<InteractionChannel>(self);
            ushort action = ActionIdOf(p.Verb);
            if (p.Started != 0 && channel.ActionInstanceId == p.Started && channel.ActiveAction == action)
            {
                if (channel.Status == NodeStatus.Running) return NodeStatus.Running;
                var done = channel.Status;   // the executor's answer — reported ONCE, then the node is free to be run again
                p.Started = 0;
                return done;
            }

            if (world.HasComponent<BehaviorState>(self)) channel.BehaviorInstanceId = world.GetComponent<BehaviorState>(self).InstanceId;
            unchecked { channel.ActionInstanceId++; if (channel.ActionInstanceId == 0) channel.ActionInstanceId = 1; }
            channel.ActiveAction = action;
            channel.Status = NodeStatus.Running;
            Unsafe.As<byte, OpenDoorParams>(ref channel.Params[0]) = new OpenDoorParams { TargetDoor = door };
            p.Started = channel.ActionInstanceId;
            return NodeStatus.Running;
        }

        /// <summary>The interaction-channel action id of <paramref name="verb"/> (<see cref="BehaviorConstants.DoorActionExecutors"/>).</summary>
        public static ushort ActionIdOf(DoorVerb verb) => verb switch
        {
            DoorVerb.Open => BehaviorConstants.ActionIdOpenDoor,
            DoorVerb.Close => BehaviorConstants.ActionIdCloseDoor,
            DoorVerb.Lock => BehaviorConstants.ActionIdLockDoor,
            DoorVerb.Unlock => BehaviorConstants.ActionIdUnlockDoor,
            _ => BehaviorConstants.ActionIdBreachDoor,
        };

        /// <summary>
        /// The point <see cref="ApproachMetres"/> off the doorway's centre, on the side of the wall <paramref name="from"/> is on
        /// (the terrain's door definition gives the wall; without it, the point between the actor and the door).
        /// </summary>
        public static Vector3 ApproachPoint(EntityRepository world, Entity door, TerrainObjectRef key, Vector2 from)
        {
            var d = world.GetComponentRO<SimTransform>(door).Position;
            var c = new Vector2(d.X, d.Y);
            var terrain = world.HasSingletonManaged<TerrainWorld>() ? world.GetSingletonManaged<TerrainWorld>() : null;
            int i = key.DoorIndex(terrain);
            Vector2 n;
            if (i >= 0)
            {
                var def = terrain!.Doors[i];
                var panel = terrain.Panels[def.Panel];
                var along = panel.B - panel.A;
                n = along.LengthSquared() > 1e-6f ? Vector2.Normalize(new Vector2(-along.Y, along.X)) : Vector2.UnitY;
                c = def.Center;
                if (Vector2.Dot(from - c, n) < 0f) n = -n;
            }
            else
            {
                var away = from - c;
                n = away.LengthSquared() > 1e-6f ? Vector2.Normalize(away) : Vector2.UnitY;
            }
            var p = c + n * ApproachMetres;
            return new Vector3(p.X, p.Y, d.Z);
        }

    }
}
