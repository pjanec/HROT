using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>The outcome of <see cref="Embarkation.TryEmbark"/>.</summary>
    public enum EmbarkResult : byte
    {
        /// <summary>The passenger is now aboard.</summary>
        Applied,
        /// <summary>The passenger was already aboard this vehicle — nothing to write.</summary>
        AlreadyAboard,
        /// <summary>The vehicle has no free seat.</summary>
        Full,
        /// <summary>Either entity is gone, or the vehicle carries no passengers.</summary>
        Invalid,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3110</c> I-2a (R-224) — THE embarkation rules: the one place that writes a vehicle's <see cref="PassengerBuffer"/>, a
    /// passenger's <see cref="IsEmbarkedTag"/> and the capabilities boarding strips. Its callers: <c>EmbarkationSystem</c> (the
    /// commands, applied on the vehicle's owner) and scenario load (genesis, inside its own transaction). ⛔ It used to be written three
    /// times (<c>EmbarkExecutor</c>, <c>EditorCargoSystem</c>, genesis) and left twice, and the copies disagreed.
    /// 📄 docs/DESIGN_Entity_Interactions.md §7, §7.1a.
    /// </summary>
    public static class Embarkation
    {
        /// <summary>Hull radius assumed when the vehicle has no collider (m).</summary>
        public const float DefaultHullRadius = 2f;
        /// <summary>Gap between the hull and the dismounted column (m).</summary>
        public const float DismountClearance = 1.5f;
        /// <summary>Spacing of the dismounted column along the vehicle (m).</summary>
        public const float DismountSpacing = 1.5f;

        /// <summary>The capabilities a seat takes away (and leaving gives back).</summary>
        public const ActorCapabilities StrippedAboard = ActorCapabilities.CanMove | ActorCapabilities.CanShoot;

        /// <summary>
        /// True when THIS node arbitrates boarding for <paramref name="vehicle"/> (R-224: the vehicle's owner). With no
        /// <see cref="NetworkAuthority"/> (one node, the editor) every vehicle is ours — ⛔ except a GHOST, a replica whose master has not
        /// arrived (applying there would fork the passenger list). ⚠ I-2a gates on the vehicle's PRIMARY authority; I-2b moves it to the
        /// <c>PassengerBuffer</c> descriptor's key, as doors gate on theirs (<c>DoorCommandSystem.OwnsDoor</c>).
        /// </summary>
        public static bool OwnsVehicle(EntityRepository repo, Entity vehicle)
        {
            if (!repo.IsAlive(vehicle) || !repo.HasComponent<PassengerBuffer>(vehicle)) return false;
            if (!(repo.IsComponentTypeRegistered<NetworkAuthority>() && repo.HasComponent<NetworkAuthority>(vehicle))
                && repo.GetLifecycleState(vehicle) == EntityLifecycle.Ghost) return false;
            return Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(repo, vehicle);
        }

        /// <summary>Is <paramref name="passenger"/> in <paramref name="vehicle"/>'s list?</summary>
        public static bool IsAboard(EntityRepository repo, Entity passenger, Entity vehicle)
            => SlotOf(repo, passenger, vehicle) >= 0;

        /// <summary>Boards <paramref name="passenger"/> onto <paramref name="vehicle"/>: the list, the tag, the stripped capabilities.
        /// Idempotent — a passenger already aboard is <see cref="EmbarkResult.AlreadyAboard"/> (its own side is made consistent).</summary>
        public static EmbarkResult TryEmbark(EntityRepository repo, Entity passenger, Entity vehicle)
        {
            if (!repo.IsAlive(passenger) || !repo.IsAlive(vehicle) || !repo.HasComponent<PassengerBuffer>(vehicle)) return EmbarkResult.Invalid;
            if (IsAboard(repo, passenger, vehicle)) { BoardSide(repo, passenger, vehicle); return EmbarkResult.AlreadyAboard; }
            ref var buffer = ref repo.GetComponentRW<PassengerBuffer>(vehicle);
            if (buffer.Count >= PassengerBuffer.Capacity) return EmbarkResult.Full;
            buffer.Passengers[buffer.Count++] = passenger;
            BoardSide(repo, passenger, vehicle);
            return EmbarkResult.Applied;
        }

        /// <summary>
        /// <paramref name="passenger"/> leaves its vehicle (<paramref name="vehicle"/>, or the one its tag names when null): out of the
        /// list, the tag gone, the capabilities back, and standing BESIDE the hull at the seat it had (the CE-321 column). False when it
        /// was not aboard.
        /// </summary>
        public static bool Disembark(EntityRepository repo, Entity passenger, Entity vehicle = default)
        {
            if (!repo.IsAlive(passenger)) return false;
            if (vehicle.IsNull && Has<IsEmbarkedTag>(repo, passenger)) vehicle = repo.GetComponentRO<IsEmbarkedTag>(passenger).VehicleEntity;
            int slot = SlotOf(repo, passenger, vehicle);
            if (slot < 0) return false;
            ref var buffer = ref repo.GetComponentRW<PassengerBuffer>(vehicle);
            var at = DismountPoint(repo, vehicle, slot, buffer.Count);
            buffer.Passengers[slot] = buffer.Passengers[buffer.Count - 1];
            buffer.Passengers[buffer.Count - 1] = Entity.Null;
            buffer.Count--;
            LeaveSide(repo, passenger, at);
            return true;
        }

        /// <summary>Every live passenger leaves <paramref name="vehicle"/>, in a column beside the hull (CE-321); the dead stay where
        /// they are. Returns how many left.</summary>
        public static int EjectAll(EntityRepository repo, Entity vehicle)
        {
            if (!repo.IsAlive(vehicle) || !repo.HasComponent<PassengerBuffer>(vehicle)) return 0;
            ref var buffer = ref repo.GetComponentRW<PassengerBuffer>(vehicle);
            int left = 0;
            for (int i = 0; i < buffer.Count; i++)
            {
                var passenger = buffer.Passengers[i];
                buffer.Passengers[i] = Entity.Null;
                if (!repo.IsAlive(passenger)) continue;   // killed aboard — skipped, not an error
                LeaveSide(repo, passenger, DismountPoint(repo, vehicle, i, buffer.Count));
                left++;
            }
            buffer.Count = 0;
            return left;
        }

        /// <summary>
        /// ⭐ CE-321 — where seat <paramref name="index"/> of <paramref name="count"/> stands on leaving: a column along the vehicle's
        /// RIGHT side, <see cref="DismountSpacing"/> apart, <see cref="DismountClearance"/> clear of its collider, turned with its
        /// heading (forward = Transform(UnitX, rotation); X east, Y north). 🔴 It used to be a fixed WORLD offset (−4 m in Y): 0.5 m
        /// outside a 3.5 m APC's collider and behind it whatever its heading — their fire then killed their own APC.
        /// </summary>
        public static Vector3 DismountPoint(EntityRepository repo, Entity vehicle, int index, int count)
        {
            if (!Has<SimTransform>(repo, vehicle)) return Vector3.Zero;
            var tf = repo.GetComponentRO<SimTransform>(vehicle);
            var fwd3 = Vector3.Transform(Vector3.UnitX, tf.Rotation);
            var forward = new Vector2(fwd3.X, fwd3.Y);
            forward = forward.LengthSquared() > 1e-6f ? Vector2.Normalize(forward) : Vector2.UnitX;
            var right = new Vector2(forward.Y, -forward.X);
            float hull = repo.IsComponentTypeRegistered<Fdp.Toolkit.Physics.Components.PhysicsCollider>()
                         && repo.HasComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>(vehicle)
                ? repo.GetComponentRO<Fdp.Toolkit.Physics.Components.PhysicsCollider>(vehicle).Radius
                : DefaultHullRadius;
            var along = forward * ((index - (count - 1) / 2f) * DismountSpacing);
            var lateral = right * (hull + DismountClearance);
            return tf.Position + new Vector3(along.X + lateral.X, along.Y + lateral.Y, 0f);
        }

        private static int SlotOf(EntityRepository repo, Entity passenger, Entity vehicle)
        {
            if (vehicle.IsNull || !repo.IsAlive(vehicle) || !repo.HasComponent<PassengerBuffer>(vehicle)) return -1;
            ref readonly var buffer = ref repo.GetComponentRO<PassengerBuffer>(vehicle);
            for (int i = 0; i < buffer.Count; i++) if (buffer.Passengers[i] == passenger) return i;
            return -1;
        }

        private static void BoardSide(EntityRepository repo, Entity passenger, Entity vehicle)
        {
            if (Has<ActorCapabilityState>(repo, passenger))
                repo.GetComponentRW<ActorCapabilityState>(passenger).Capabilities &= ~StrippedAboard;
            repo.SetComponent(passenger, new IsEmbarkedTag { VehicleEntity = vehicle });
        }

        private static void LeaveSide(EntityRepository repo, Entity passenger, Vector3 at)
        {
            if (Has<SimTransform>(repo, passenger)) repo.GetComponentRW<SimTransform>(passenger).Position = at;
            if (Has<ActorCapabilityState>(repo, passenger))
                repo.GetComponentRW<ActorCapabilityState>(passenger).Capabilities |= StrippedAboard;
            if (Has<IsEmbarkedTag>(repo, passenger)) repo.RemoveComponent<IsEmbarkedTag>(passenger);
        }

        private static bool Has<T>(EntityRepository repo, Entity e) where T : unmanaged
            => repo.IsComponentTypeRegistered<T>() && repo.HasComponent<T>(e);
    }
}
