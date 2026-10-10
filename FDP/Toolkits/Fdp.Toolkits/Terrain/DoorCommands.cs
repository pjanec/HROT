using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>⭐ Buildings Stage 5d — what an actor does to a door (📄 docs/DESIGN_Building_Interiors.md §3a "Doors", §3j "5d").</summary>
    public enum DoorVerb : byte { Open = 0, Close = 1, Lock = 2, Unlock = 3, Breach = 4 }

    /// <summary>The outcome of applying a <see cref="DoorVerb"/> to a door in a given state.</summary>
    public enum DoorVerbResult : byte
    {
        /// <summary>The door changes state.</summary>
        Applied,
        /// <summary>The door is already in the state the verb leads to — success, nothing to write.</summary>
        AlreadyDone,
        /// <summary>The verb cannot act on a door in this state (open a locked door; close a destroyed one).</summary>
        Refused,
    }

    /// <summary>
    /// ⭐ 5d — THE door state machine: the one place that says what each verb does to each state. The owner's applier
    /// (<see cref="DoorCommandSystem"/>) writes with it, and the actor's executor uses it to know what it is waiting for.
    /// <code>
    ///            Open      Close     Lock      Unlock    Breach
    /// Open       done      Closed    Locked    done      Destroyed
    /// Closed     Open      done      Locked    done      Destroyed
    /// Locked     refused   done      done      Closed    Destroyed
    /// Destroyed  done      refused   refused   refused   done
    /// </code>
    /// ⚠ Locking an open door shuts it too (one action). Opening a destroyed door is "done": the doorway is already passable.
    /// </summary>
    public static class DoorRules
    {
        /// <summary>The state <paramref name="verb"/> leads to from <paramref name="from"/>, and whether that is a change.</summary>
        public static DoorVerbResult Apply(TerrainDoorState from, DoorVerb verb, out TerrainDoorState to)
        {
            to = from;
            switch (verb)
            {
                case DoorVerb.Open:
                    if (from == TerrainDoorState.Locked) return DoorVerbResult.Refused;
                    if (from is TerrainDoorState.Open or TerrainDoorState.Destroyed) return DoorVerbResult.AlreadyDone;
                    to = TerrainDoorState.Open; return DoorVerbResult.Applied;
                case DoorVerb.Close:
                    if (from == TerrainDoorState.Destroyed) return DoorVerbResult.Refused;
                    if (from is TerrainDoorState.Closed or TerrainDoorState.Locked) return DoorVerbResult.AlreadyDone;
                    to = TerrainDoorState.Closed; return DoorVerbResult.Applied;
                case DoorVerb.Lock:
                    if (from == TerrainDoorState.Destroyed) return DoorVerbResult.Refused;
                    if (from == TerrainDoorState.Locked) return DoorVerbResult.AlreadyDone;
                    to = TerrainDoorState.Locked; return DoorVerbResult.Applied;
                case DoorVerb.Unlock:
                    if (from == TerrainDoorState.Destroyed) return DoorVerbResult.Refused;
                    if (from != TerrainDoorState.Locked) return DoorVerbResult.AlreadyDone;
                    to = TerrainDoorState.Closed; return DoorVerbResult.Applied;
                case DoorVerb.Breach:
                    if (from == TerrainDoorState.Destroyed) return DoorVerbResult.AlreadyDone;
                    to = TerrainDoorState.Destroyed; return DoorVerbResult.Applied;
                default:
                    return DoorVerbResult.Refused;
            }
        }

        /// <summary>
        /// How long the ACTOR spends on <paramref name="verb"/> before the command is sent (seconds) — "opening takes animation
        /// time" (§3a). ⚠ v1 constants, stated here once; they move to the TKB with the actor's animation set.
        /// </summary>
        public static float ActionSeconds(DoorVerb verb) => verb switch
        {
            DoorVerb.Open   => 1.0f,
            DoorVerb.Close  => 1.0f,
            DoorVerb.Lock   => 2.0f,
            DoorVerb.Unlock => 2.0f,
            DoorVerb.Breach => 3.0f,
            _               => 1.0f,
        };

        /// <summary>How near the door (its doorway centre, horizontally) the actor must stand to act on it — "adjacent" (§3a).</summary>
        public const float ReachMetres = 2.0f;

        /// <summary>How far above/below the door's sill the actor may stand (a different storey is not adjacent).</summary>
        public const float ReachHeightMetres = 1.5f;
    }

    /// <summary>
    /// ⭐ 5d — "do <see cref="Verb"/> to <see cref="Door"/>", raised by the actor's executor (or the mover at a door waypoint) on ITS
    /// node. The door's OWNER applies it (<see cref="DoorCommandSystem"/>); on any other node the <c>EntityDoorCommand</c> translators
    /// carry it to the owner, republished there with <see cref="IsRemote"/> set. The result reaches every node as the door's
    /// replicated <see cref="DoorState"/> — no reply message.
    /// </summary>
    [EventId(TerrainEventIds.DoorCommand)]
    [StructLayout(LayoutKind.Sequential)]
    public struct DoorCommandEvent
    {
        public Entity Door;
        public DoorVerb Verb;
        /// <summary>Who acts (for logs and, later, permissions/keys); <see cref="Entity.Null"/> for an operator.</summary>
        public Entity Actor;
        /// <summary>True when this event came off the network — its egress must not send it again.</summary>
        public bool IsRemote;
    }

    /// <summary>Event ids of the terrain toolkit (range 5100–5199; 5100 free by a census of every event id, <c>2026-10-08</c>).</summary>
    public static class TerrainEventIds
    {
        public const int DoorCommand = 5100;
    }

    /// <summary>
    /// ⭐ 5d — the door's OWNER applies door commands: <see cref="DoorRules"/> on the door's current <see cref="DoorState"/>, written
    /// only when it changes. Every other node skips the command (its translator has sent it to the owner). The owner's
    /// <c>EntityDoorStateEgressTranslator</c> then publishes the new state to every node, as for any door change.
    /// <para>Gate: the node that CLAIMS <see cref="DoorState"/> — the same test <c>HealthApplicationSystem</c> uses for Health; with no
    /// <see cref="NetworkAuthority"/> (one node, the editor) this node owns every door.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class DoorCommandSystem : IEcsModuleSystem
    {
        /// <summary>
        /// The wire ordinal of the door-state descriptor (<c>EDescriptorType.dtDoorState</c> in the NED assembly, which this toolkit
        /// cannot reference). ⚠ A rail in the SimHost suite pins the two equal.
        /// </summary>
        public const long DoorStateDescriptorOrdinal = 120;

        /// <summary>The ownership key a door's state is published under — the applier and the command egress both gate on it.</summary>
        public static readonly long DoorStateKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DoorStateDescriptorOrdinal, 0);

        /// <summary>Commands this system applied (a rail and a diagnostics counter read it).</summary>
        public long Applied { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.Bus.IsRegistered<DoorCommandEvent>() || !repo.IsComponentTypeRegistered<DoorState>()) return;
            var events = repo.Bus.Read<DoorCommandEvent>();
            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var cmd = ref events[i];
                if (!OwnsDoor(repo, cmd.Door)) continue;
                var current = repo.GetComponentRO<DoorState>(cmd.Door).State;
                if (DoorRules.Apply(current, cmd.Verb, out var next) != DoorVerbResult.Applied) continue;
                repo.SetComponent(cmd.Door, new DoorState { State = next });
                Applied++;
            }
        }

        /// <summary>
        /// True when this node is the one that writes <paramref name="door"/>'s state — the owner of its door-state descriptor
        /// (<see cref="DoorStateKey"/>), the same test its egress publishes under. With no
        /// <see cref="NetworkAuthority"/> (one node, the editor) every door is ours — ⛔ except a GHOST, a replica whose master has
        /// not arrived yet (it carries no authority component until then, and applying there would fork the door's state).
        /// </summary>
        public static bool OwnsDoor(EntityRepository repo, Entity door)
        {
            if (!repo.IsAlive(door) || !repo.HasComponent<DoorState>(door)) return false;
            if (!(repo.IsComponentTypeRegistered<NetworkAuthority>() && repo.HasComponent<NetworkAuthority>(door))
                && repo.GetLifecycleState(door) == EntityLifecycle.Ghost) return false;
            return Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(repo, door, DoorStateKey);
        }
    }
}
