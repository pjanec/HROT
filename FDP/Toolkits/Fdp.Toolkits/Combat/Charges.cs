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
            End(world, charge);
            return true;
        }

        /// <summary>
        /// ⭐ A REPLICATED charge ends through the one teardown path (<see cref="Fdp.Toolkit.NetworkSpawning.Events.DestroyEntityCommand"/>, Entity
        /// Creation §3.4c) so every replica goes too — a local destroy leaves them behind (measured on the cloud, <c>AreaEffectCloudSystem.End</c>).
        /// It is SPENT at once (no longer a charge here), so an ask arriving before the teardown does not burst it twice. An offline one is
        /// destroyed now.
        /// </summary>
        private static void End(EntityRepository world, Entity charge)
        {
            if (world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>()
                && world.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(charge)
                && world.Bus.IsRegisteredManaged<Fdp.Toolkit.NetworkSpawning.Events.DestroyEntityCommand>())
            {
                world.GetComponentRW<WeaponSystemParams>(charge).Charge = 0;
                world.Bus.PublishManaged(new Fdp.Toolkit.NetworkSpawning.Events.DestroyEntityCommand
                {
                    NetworkId = world.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(charge).Value,
                    Reason    = "Charge detonated",
                });
                return;
            }
            world.DestroyEntity(charge);
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
    /// ⭐ <c>CE-4621</c> M-5 (MV-9, IM-2; docs/designs/vehicle-tactics/DESIGN_Motorcycle.md §2 / §8) — a charge's TIMER: it asks the charge to go off
    /// (<see cref="Charges.Ask"/> with no one — a timer needs no phone) once sim time reaches <see cref="DetonateAt"/>. ⭐ Unmanaged and recorded,
    /// so a replay, a seek or a restore keeps the countdown. Ticked by <see cref="ChargeSystem"/> wherever the component is (the node that armed
    /// it); the ask reaches the position owner through the interaction transport, as every ask does.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.ChargeTimer)]
    [DataPolicy(DataPolicy.Default)]   // world state: recorded, saved, snapshotted
    public struct ChargeTimer
    {
        /// <summary>The sim time it goes off.</summary>
        public SimInstant DetonateAt;
    }

    /// <summary>⭐ <c>CE-4621</c> M-5 — arm / read a charge's timer.</summary>
    public static class ChargeTimers
    {
        /// <summary>Arm <paramref name="charge"/> to go off <paramref name="delay"/> from now (re-arming replaces the time). False when it is no charge.</summary>
        /// <remarks>⭐ Parallel-safe by construction: inside a per-entity phase (a brain arming the charge it carries — ANOTHER entity) the write is
        /// a follow-up, played after the phase in batch order (R-4212, CLAUDE.md "behaviour code runs in parallel"); the instant is fixed now.</remarks>
        public static bool Arm(EntityRepository world, Entity charge, SimDuration delay)
        {
            if (!Charges.IsCharge(world, charge) || !world.IsComponentTypeRegistered<ChargeTimer>()) return false;
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;
            double at = now + Math.Max(0d, (double)delay);
            if (ParallelReadCheck.Defers(world)) ParallelReadCheck.CurrentBatch!.Later(ArmLater.Instance, charge, 0, BitConverter.DoubleToInt64Bits(at));
            else Set(world, charge, at);
            return true;
        }

        private static void Set(EntityRepository world, Entity charge, double at)
        {
            if (!world.IsAlive(charge)) return;
            var timer = new ChargeTimer { DetonateAt = (SimInstant)at };
            if (world.HasComponent<ChargeTimer>(charge)) world.SetComponent(charge, timer);
            else world.AddComponent(charge, timer);
        }

        private sealed class ArmLater : Fdp.Core.Jobs.IPhaseFollowUp
        {
            public static readonly ArmLater Instance = new();
            public void Apply(EntityRepository repo, Entity charge, int code, long at) => Set(repo, charge, BitConverter.Int64BitsToDouble(at));
        }

        /// <summary>Is <paramref name="charge"/> armed?</summary>
        public static bool IsArmed(EntityRepository world, Entity charge)
            => world.IsComponentTypeRegistered<ChargeTimer>() && world.IsAlive(charge) && world.HasComponent<ChargeTimer>(charge);
    }

    /// <summary>
    /// ⭐ <c>CE-3233</c> (B-11b, §7.13 ST-8) — applies <see cref="DetonateChargeEvent"/> where the charge's position is owned. In the
    /// <c>Interactions</c> capability on every node.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class ChargeSystem : IEcsModuleSystem
    {
        /// <summary>Charges set off on this node (rails read it).</summary>
        [field: Fdp.Core.WorldDerived("diagnostic counter")]
        public long Detonated { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository world || !world.Bus.IsRegistered<DetonateChargeEvent>()) return;
            var asks = world.ReadEvents<DetonateChargeEvent>();
            for (int i = 0; i < asks.Length; i++)
                if (Charges.Detonate(world, asks[i].Charge, asks[i].By)) Detonated++;
            TickTimers(world);
        }

        // ⭐ CE-4621 M-5 — the timers this node armed: a due one asks its charge (no one — a timer needs no phone) and is removed
        private EntityQuery? _timers;
        private EntityRepository? _for;
        private int _epoch;
        [Fdp.Core.FrameScratch("list of due charges: cleared at 203 and filled from this frame query")]
        private readonly System.Collections.Generic.List<Entity> _due = new();

        /// <summary>Timers that came due on this node (rails read it).</summary>
        [field: Fdp.Core.WorldDerived("diagnostic counter of fired charge timers")]
        public long TimersFired { get; private set; }

        private void TickTimers(EntityRepository world)
        {
            if (!world.IsComponentTypeRegistered<ChargeTimer>()) return;
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(world)) return;   // a replay plays the recorded detonation, it never re-asks
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(world, ref _epoch) | !ReferenceEquals(world, _for) || _timers == null)
            {
                _for = world;
                _timers = world.Query().With<ChargeTimer>().Build();
            }
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;
            _due.Clear();
            foreach (var e in _timers)
                if ((double)world.GetComponentRO<ChargeTimer>(e).DetonateAt <= now) _due.Add(e);
            for (int i = 0; i < _due.Count; i++)
            {
                var charge = _due[i];
                world.RemoveComponent<ChargeTimer>(charge);
                if (Charges.Ask(world, charge, Entity.Null)) TimersFired++;
            }
        }

    }
}
