using System.Numerics;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fbt.Kernel;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Stance;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>AQ88</c> B (R-265) — the body a move is made in. 📄 docs/blueprints/Architect_Question_88_Soldier_Speed_And_Retreat_Under_Fire.md §5.</summary>
    public enum MoveGait : byte
    {
        /// <summary>A soldier's default: crouched while THREATENED (<see cref="TacticalGait.Threatened"/>), upright otherwise.</summary>
        Tactical = 0,
        /// <summary>Upright whatever happens — a civilian runs upright (§4 B).</summary>
        Upright = 1,
        /// <summary>Crouched whatever happens — a CAUTIOUS move (a danger-area crossing).</summary>
        Crouched = 2,
        /// <summary>Leave the stance alone — the caller manages it (PeekAndFire's side-steps between hide and peek).</summary>
        Keep = 3,
    }

    /// <summary>⭐ <c>AQ88</c> B (R-265) — the ONE rule for the body a moving unit holds: <i>"run in slightly crouched pose whenever
    /// they feel threatened or are just cautious"</i>. The crouch also halves the top speed (<see cref="StanceSpeed"/>).</summary>
    public static class TacticalGait
    {
        /// <summary>How long a hit or a near miss keeps a unit threatened after it stops remembering anyone (s).</summary>
        public const double ShotAtSeconds = 10.0;

        /// <summary>The blend into the moving stance (s).</summary>
        public const float BlendSeconds = 0.3f;

        /// <summary>Threatened = the unit remembers a live contact (seen or heard), or was hit / near-missed within
        /// <see cref="ShotAtSeconds"/>. Reuses the memory and <see cref="RecentSenses"/>; adds no state.</summary>
        public static unsafe bool Threatened(EntityRepository world, Entity self)
        {
            if (RecentSensesOf.Within(world, self, SensorChange.Hit, ShotAtSeconds)
                || RecentSensesOf.Within(world, self, SensorChange.NearMiss, ShotAtSeconds)) return true;
            if (!world.HasComponent<TargetMemory>(self)) return false;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(self);
            for (int i = 0; i < mem.Count; i++)
            {
                if (TargetMemory.IsAnonymous(in mem, i)) return true;   // heard — "something about here" is a threat
                var e = new Entity((ulong)mem.EntityIds[i]);
                if (CombatLife.IsAlive(world, e)) return true;   // exists and not dead (CE-2120: a corpse is no threat)
            }
            return false;
        }

        /// <summary>The stance <paramref name="gait"/> asks for, or null for <see cref="MoveGait.Keep"/>.</summary>
        public static StanceId? StanceFor(EntityRepository world, Entity self, MoveGait gait) => gait switch
        {
            MoveGait.Keep     => null,
            MoveGait.Upright  => StanceId.Standing,
            MoveGait.Crouched => StanceId.Crouched,
            _                 => Threatened(world, self) ? StanceId.Crouched : StanceId.Standing,
        };
    }

    /// <summary>
    /// ⭐ <c>CE-2092</c> — the ONE way a C# node moves its own unit: a <see cref="NavigationConstants.ActionIdMoveTo"/> on the
    /// unit's <see cref="LocomotionChannel"/>, which <c>MoveToExecutor</c> turns into a pathed move (<c>PathToPoint</c>,
    /// CE-3026). Factored out of <see cref="EqsCombatNodes.Action_MoveToOptimalCover"/> so the tactics nodes do not carry a
    /// second copy of the channel write. 📄 <c>docs/DESIGN_Eqs_Consuming_Behaviours.md</c> §2.1.
    /// </summary>
    public static class LocomotionMoveTo
    {
        /// <summary>Stamps the channel with the unit's running behaviour, so arbitration does not stomp the command.</summary>
        public static void StampOwner(EntityRepository world, Entity self, ref LocomotionChannel channel)
        {
            if (world.HasComponent<BehaviorState>(self))
                channel.BehaviorInstanceId = world.GetComponent<BehaviorState>(self).InstanceId;
        }

        /// <summary>Issues a NEW MoveTo to <paramref name="destination"/> (a new action instance, so the executor re-enters).
        /// False when the unit has no locomotion channel.</summary>
        /// <remarks>⭐ <c>AQ88</c> B (R-265) — <paramref name="gait"/> sets the body the move is made in (<see cref="TacticalGait"/>); a
        /// unit that cannot carry a stance just moves.</remarks>
        /// <remarks>⭐ <c>CE-2167</c> (V5) — <paramref name="reverseAllowed"/>: a vehicle may BACK to the destination (it keeps facing its
        /// way — out of a firing position and back into the hide, Exposure Cycle §4–§5); a person turns and walks as before.</remarks>
        /// <remarks>⭐ <c>CE-2230</c> — <paramref name="onSurface"/>: the destination is a place on the map (x, y), its height the surface's
        /// there (<c>FlagDestinationOnSurface</c>, R-252) — a commander's 2-D slot, not a 3-D point.</remarks>
        public static unsafe bool Issue(EntityRepository world, Entity self, Vector3 destination, float speed, float arrivalRadius,
                                        MoveGait gait = MoveGait.Tactical, bool reverseAllowed = false, bool onSurface = false)
        {
            if (!world.HasComponent<LocomotionChannel>(self)) return false;
            if (TacticalGait.StanceFor(world, self, gait) is StanceId stance)
                StanceRequest.Set(world, self, stance, TacticalGait.BlendSeconds);
            ref var channel = ref world.GetComponentRW<LocomotionChannel>(self);
            StampOwner(world, self, ref channel);
            unchecked { channel.ActionInstanceId++; }
            channel.ActiveAction = NavigationConstants.ActionIdMoveTo;
            channel.Status = NodeStatus.Running;
            var moveToParams = new MoveToParams
            {
                Destination    = destination,
                ArrivalRadius  = arrivalRadius,
                Speed          = speed,
                ReverseAllowed = reverseAllowed ? (byte)1 : (byte)0,
                Flags          = onSurface ? NavigationConstants.FlagDestinationOnSurface : (byte)0,
            };
            fixed (byte* dst = channel.Params)
            {
                *(MoveToParams*)dst = moveToParams;
            }
            return true;
        }

        /// <summary>⭐ <c>CE-3079</c> H4 — stops the unit's MoveTo, if one is active (a new action instance so the executor lets go).
        /// The ONE stop: <see cref="EqsTacticsNodes"/>, <see cref="PostureNodes"/> and <see cref="DangerAreaNodes"/> route through it.</summary>
        public static void Stop(EntityRepository world, Entity self)
        {
            if (!world.HasComponent<LocomotionChannel>(self)) return;
            ref var loco = ref world.GetComponentRW<LocomotionChannel>(self);
            if (loco.ActiveAction != NavigationConstants.ActionIdMoveTo) return;
            loco.ActiveAction = 0;
            unchecked { loco.ActionInstanceId++; }
        }

        /// <summary>The status of the unit's MoveTo: Running / Success (arrived) / Failure; Failure when no MoveTo is active.</summary>
        public static NodeStatus Status(EntityRepository world, Entity self)
        {
            if (!world.HasComponent<LocomotionChannel>(self)) return NodeStatus.Failure;
            ref readonly var channel = ref world.GetComponentRO<LocomotionChannel>(self);
            return channel.ActiveAction == NavigationConstants.ActionIdMoveTo ? channel.Status : NodeStatus.Failure;
        }
    }
}
