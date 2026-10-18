using System.Numerics;
using Fdp.Core;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;

namespace Hrot.MuscleCharacter.Animation.Look
{
    /// <summary>
    /// ⭐ <c>CE-3252</c> (LD-1, LD-G, R-2049) — the Brain's one way to point a person's eyes: writes the existing <see cref="LookAtChannel"/>
    /// (a fresh <c>ActionInstanceId</c> per NEW request, so a behaviour may call it every tick), which the wire carries to the node that owns
    /// the body, where the look-at executor sets the wanted look and the gait / <c>LookFollowSystem</c> turn the head and the body.
    /// Behaviors' <c>LookAt</c> helper (LD-3) calls it. 📄 docs/designs/look-direction/DESIGN_Look_Direction_And_FOV.md §3.2a.
    /// </summary>
    public static class LookRequest
    {
        /// <summary>The blend the head takes to a new look (s).</summary>
        public const float DefaultBlendSeconds = 0.2f;

        /// <summary>Look at <paramref name="point"/>. False when the unit cannot carry a look (no <see cref="LookAtChannel"/>). The same point
        /// again (within 5 cm) changes nothing.</summary>
        public static unsafe bool AtPoint(EntityRepository world, Entity self, Vector3 point, byte priority = 0, uint behaviorInstance = 0)
        {
            if (!CanCarry(world, self)) return false;
            ref var ch = ref world.GetComponentRW<LookAtChannel>(self);
            if (ch.ActiveAction == LookAtActionIds.LookAtPoint)
            {
                LookAtPointParams old;
                fixed (byte* src = ch.Params) old = *(LookAtPointParams*)src;
                if (Vector3.DistanceSquared(new Vector3(old.WorldPointX, old.WorldPointY, old.WorldPointZ), point) < 0.0025f) return true;
            }
            var p = new LookAtPointParams { WorldPointX = point.X, WorldPointY = point.Y, WorldPointZ = point.Z, BlendInTime = DefaultBlendSeconds, Priority = priority };
            fixed (byte* dst = ch.Params) *(LookAtPointParams*)dst = p;
            ch.ActiveAction = LookAtActionIds.LookAtPoint;
            ch.BehaviorInstanceId = behaviorInstance;
            ch.ActionInstanceId++;
            return true;
        }

        /// <summary>Look at <paramref name="target"/> (followed as it moves). The same target again changes nothing.</summary>
        public static unsafe bool AtEntity(EntityRepository world, Entity self, Entity target, byte priority = 0, uint behaviorInstance = 0)
        {
            if (!CanCarry(world, self) || target.IsNull) return false;
            ref var ch = ref world.GetComponentRW<LookAtChannel>(self);
            uint id = (uint)target.Index;
            if (ch.ActiveAction == LookAtActionIds.LookAtEntity)
            {
                LookAtEntityParams old;
                fixed (byte* src = ch.Params) old = *(LookAtEntityParams*)src;
                if (old.TargetEntityId == id) return true;
            }
            var p = new LookAtEntityParams { TargetEntityId = id, BlendInTime = DefaultBlendSeconds, Priority = priority };
            fixed (byte* dst = ch.Params) *(LookAtEntityParams*)dst = p;
            ch.ActiveAction = LookAtActionIds.LookAtEntity;
            ch.BehaviorInstanceId = behaviorInstance;
            ch.ActionInstanceId++;
            return true;
        }

        /// <summary>Stop looking: the head returns to the body. Nothing requested ⇒ nothing done.</summary>
        public static unsafe bool Release(EntityRepository world, Entity self, uint behaviorInstance = 0)
        {
            if (!CanCarry(world, self)) return false;
            ref var ch = ref world.GetComponentRW<LookAtChannel>(self);
            if (ch.ActiveAction == 0 || ch.ActiveAction == LookAtActionIds.ReleaseLook) return true;
            var p = new ReleaseLookParams { BlendOutTime = DefaultBlendSeconds };
            fixed (byte* dst = ch.Params) *(ReleaseLookParams*)dst = p;
            ch.ActiveAction = LookAtActionIds.ReleaseLook;
            ch.BehaviorInstanceId = behaviorInstance;
            ch.ActionInstanceId++;
            return true;
        }

        /// <summary>Whether <paramref name="self"/> can carry a look at all.</summary>
        public static bool CanCarry(EntityRepository world, Entity self)
            => world.IsComponentTypeRegistered<LookAtChannel>() && world.HasComponent<LookAtChannel>(self);
    }
}
