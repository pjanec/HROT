using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Contracts;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ <c>CE-3233</c> (civilians B-11b, AW-12; docs/DESIGN_Civilians_And_Crowd_Control.md §7.13 ST-8) — "detonate <see cref="Charge"/>":
    /// raised by whoever holds its trigger (its wearer's brain, an IED's trigger man); applied by the node that owns the charge's position
    /// (the interaction transport carries it there, kind 12).
    /// </summary>
    [EventId(CombatConstants.DetonateChargeEventId)]
    [StructLayout(LayoutKind.Sequential)]
    public struct DetonateChargeEvent
    {
        public Entity Charge;
        /// <summary>Who set it off (the detonation names him as its shooter); null = the charge itself.</summary>
        public Entity By;
        /// <summary>Set by the transport's ingress (it is never sent back out).</summary>
        public bool IsRemote;
    }

    /// <summary>
    /// ⭐ <c>CE-3233</c> (B-11b, §7.13 ST-8) — an explosive CHARGE: a weapon system that is never fired, only DETONATED where it stands — worn
    /// (a carried thing stands at its carrier, <c>EmbarkedStateSystem</c>), placed on the ground, or in a vehicle's cargo station. The burst is
    /// its mount-0 round's <see cref="DetonationNotification"/> (the FPV drone's pattern, <c>DroneImpactSystem</c>), published by the node
    /// that owns its position; the charge is then gone.
    /// </summary>
    public static class Charges
    {
        /// <summary>Is <paramref name="thing"/> an explosive charge?</summary>
        public static bool IsCharge(EntityRepository world, Entity thing)
            => world.IsAlive(thing) && world.IsComponentTypeRegistered<WeaponSystemParams>() && world.HasComponent<WeaponSystemParams>(thing)
               && world.GetComponentRO<WeaponSystemParams>(thing).Charge != 0;

        /// <summary>
        /// Ask the charge's position owner to set it off (in one world <see cref="ChargeSystem"/> does so next frame). ⭐ <c>CE-3234</c> (B-12,
        /// HI-6) — a man who does not carry it sets it off by PHONE: false unless he holds one; his own vest, or no one (a timer), needs none.
        /// </summary>
        public static bool Ask(EntityRepository world, Entity charge, Entity by)
        {
            if (!world.Bus.IsRegistered<DetonateChargeEvent>() || charge.IsNull) return false;
            if (!by.IsNull && CarrierOf(world, charge) != by && HeldItems.Of(world, by) != HeldItemKind.Phone) return false;
            world.Bus.Publish(new DetonateChargeEvent { Charge = charge, By = by });
            return true;
        }

        /// <summary>
        /// Set <paramref name="charge"/> off — on the node that owns its position: its round bursts where it stands and the charge is removed
        /// (out of its carrier's list first, where this node owns that list). False when it is no charge, not this node's, or has no round.
        /// </summary>
        public static bool Detonate(EntityRepository world, Entity charge, Entity by = default)
        {
            if (!IsCharge(world, charge) || !Embarkation.OwnsPosition(world, charge) || !world.HasComponent<SimTransform>(charge)) return false;
            var mount = CombatTkb.MountOf(world, charge, 0);
            if (mount == null || mount.AmmoGuid == 0 || !world.Bus.IsRegistered<DetonationNotification>()) return false;
            long ammo = unchecked((long)mount.AmmoGuid);
            var (pen, dmg) = CombatTkb.RoundOf(world, mount, ammo);
            var at = world.GetComponentRO<SimTransform>(charge).Position;
            var shooter = !by.IsNull ? by : CarrierOf(world, charge) is { IsNull: false } carrier ? carrier : charge;
            world.Bus.Publish(new DetonationNotification
            {
                Shooter = shooter, Target = Entity.Null, HitX = at.X, HitY = at.Y, HitZ = at.Z, Penetration = pen, Damage = dmg, Ammo = ammo,
            });
            Release(world, charge);
            world.DestroyEntity(charge);
            return true;
        }

        private static Entity CarrierOf(EntityRepository world, Entity thing)
            => world.IsComponentTypeRegistered<IsEmbarkedTag>() && world.HasComponent<IsEmbarkedTag>(thing)
                ? world.GetComponentRO<IsEmbarkedTag>(thing).VehicleEntity : Entity.Null;

        /// <summary>Out of the carrier's list where this node owns it (another node's list drops it when the charge's dispose arrives).</summary>
        private static void Release(EntityRepository world, Entity charge)
        {
            var carrier = CarrierOf(world, charge);
            if (carrier.IsNull || !Embarkation.OwnsVehicle(world, carrier)) return;
            ref var buffer = ref world.GetComponentRW<PassengerBuffer>(carrier);
            for (int i = 0; i < PassengerBuffer.Capacity; i++)
                if (buffer.Passengers[i] == charge) { buffer.Passengers[i] = Entity.Null; buffer.SetMode(i, CarryMode.Seated); }
        }
    }

    /// <summary>
    /// ⭐ <c>CE-3233</c> (B-11b, §7.13 ST-8) — applies <see cref="DetonateChargeEvent"/> where the charge's position is owned. In the
    /// <c>Interactions</c> capability on every node.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class ChargeSystem : IEcsModuleSystem
    {
        /// <summary>Charges set off on this node (rails read it).</summary>
        public long Detonated { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository world || !world.Bus.IsRegistered<DetonateChargeEvent>()) return;
            var asks = world.Bus.Read<DetonateChargeEvent>();
            for (int i = 0; i < asks.Length; i++)
                if (Charges.Detonate(world, asks[i].Charge, asks[i].By)) Detonated++;
        }
    }
}
