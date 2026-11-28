using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-3110</c> I-2b (R-224, Q-1) — derives the PASSENGER side from the vehicle's <see cref="PassengerBuffer"/> on every node:
    /// listed and not tagged ⇒ <see cref="Embarkation.Board"/>; tagged and no longer listed by its vehicle ⇒ <see cref="Embarkation.Leave"/>
    /// at the CE-321 column (in leaving order, per vehicle, this frame). On the deciding node <c>EmbarkationSystem</c> has already done
    /// both, so this is a no-op there; elsewhere the list arrives by replication and the side follows. The body is placed only where its
    /// position is owned (inside <see cref="Embarkation.Leave"/>).
    /// ⭐ Order inside the phase is free: <see cref="Embarkation.Board"/> writes the tag and strips CanMove in one call, so
    /// <see cref="CognitiveInterruptSystem"/> (which skips a tagged entity) sees the tag in whichever frame it notices the change.
    /// ⭐⭐ I-2c (R-271) — for every passenger still aboard: CARRIED (position, rotation and velocity = the vehicle's) where its position is
    /// owned, and KILLED where its Health is owned once the vehicle is destroyed — a lethal <c>DamageAssessedEvent</c> into the one Health
    /// writer, <c>HealthApplicationSystem</c>; the body stays aboard the wreck. ⚠ The carry is ≤ one frame behind the vehicle (this runs in
    /// Input, kinematics in PostSimulation) — the passenger is hidden, or rides an open cabin where that is invisible at frame rate.
    /// ⭐ Composed on every node by the cross-role <c>CoreInfrastructureCapabilities.Interactions</c> — never by a host or role pack.
    /// 📄 docs/DESIGN_Entity_Interactions.md §7.1c, §7.1d, §7.2a.
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    [TickMode(TickPolicy.EveryFrame)]
    public sealed class EmbarkedStateSystem : IEcsModuleSystem
    {
        [Fdp.Core.WorldDerived("per-frame scratch cleared at end of Execute")]
        private readonly List<(Entity passenger, Entity vehicle, int seat)> _boarders = new();
        [Fdp.Core.WorldDerived("per-frame scratch cleared at end of Execute")]
        private readonly List<(Entity passenger, Entity vehicle)> _leavers = new();
        [Fdp.Core.WorldDerived("per-frame scratch cleared at end of Execute")]
        private readonly Dictionary<Entity, int> _leaving = new();
        [Fdp.Core.WorldDerived("per-frame scratch cleared at end of Execute")]
        private readonly Dictionary<Entity, int> _order = new();
        [Fdp.Core.ProtocolCache("kill-damage already-sent latch per passenger; cleared on WorldEpoch")]
        private readonly HashSet<Entity> _killSent = new();
        private int _epoch;   // ⭐ CE-3294 — the world the state below describes (passengers already killed with their vehicle)
        [Fdp.Core.WorldDerived("per-frame scratch cleared after use")]
        private readonly HashSet<Entity> _drops = new();
        [Fdp.Core.WorldDerived("scratch cleared after use")]
        private readonly List<Entity> _released = new();

        /// <summary>Passengers this node boarded from a list (a rail reads it).</summary>
        [field: Fdp.Core.WorldDerived("diagnostic counter")]
        public long Boarded { get; private set; }
        /// <summary>Passengers this node took off because their vehicle no longer lists them.</summary>
        [field: Fdp.Core.WorldDerived("diagnostic counter")]
        public long Left { get; private set; }
        /// <summary>Passengers this node killed because their vehicle was destroyed (lethal damage events published).</summary>
        [field: Fdp.Core.WorldDerived("diagnostic counter")]
        public long Killed { get; private set; }
        /// <summary>⭐ CE-2152 — loads this node dropped because their person carrier went down (⭐ T-a — or his second man did).</summary>
        [field: Fdp.Core.WorldDerived("diagnostic counter — read by tests and diagnostics only")]
        public long Dropped { get; private set; }
        /// <summary>⭐ T-a — second men this node released because their carrier no longer names them.</summary>
        [field: Fdp.Core.WorldDerived("diagnostic counter")]
        public long Released { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _epoch)) { _killSent.Clear(); }   // ⭐ CE-3294 — a reload's units come back under the same handles
            if (view is not EntityRepository repo) return;
            if (!repo.IsComponentTypeRegistered<PassengerBuffer>() || !repo.IsComponentTypeRegistered<IsEmbarkedTag>()) return;

            foreach (var vehicle in repo.Query().With<PassengerBuffer>().Build())
            {
                ref readonly var buffer = ref repo.GetComponentRO<PassengerBuffer>(vehicle);
                for (int i = 0; i < PassengerBuffer.Capacity; i++)   // ⭐ CE-2151 B-b — every seat; a slot IS a seat, so one may be empty mid-list
                {
                    var p = buffer.Passengers[i];
                    if (p.IsNull || !repo.IsAlive(p)) continue;
                    if (repo.HasComponent<IsEmbarkedTag>(p) && repo.GetComponentRO<IsEmbarkedTag>(p) is var tag
                        && tag.VehicleEntity == vehicle && tag.Seat == i && tag.Mode == buffer.ModeOf(i)) continue;   // ⭐ CE-2152 — and its mode
                    _boarders.Add((p, vehicle, i));
                }
            }

            foreach (var p in repo.Query().With<IsEmbarkedTag>().Build())
            {
                var vehicle = repo.GetComponentRO<IsEmbarkedTag>(p).VehicleEntity;
                if (Embarkation.IsAboard(repo, p, vehicle)) continue;
                if (Listed(p)) continue;   // moving to another vehicle: boarding re-tags it
                _leavers.Add((p, vehicle));
                _leaving[vehicle] = _leaving.TryGetValue(vehicle, out var n) ? n + 1 : 1;
            }

            foreach (var (p, vehicle, seat) in _boarders) { Embarkation.Board(repo, p, vehicle, seat); Boarded++; }

            foreach (var (p, vehicle) in _leavers)
            {
                _order.TryGetValue(vehicle, out var k);
                _order[vehicle] = k + 1;
                Embarkation.Leave(repo, p, StandAt(repo, p, vehicle, k, _leaving[vehicle]));
                Left++;
            }
            // ⭐ CE-2152 (R-2011 C7) — a person carrier whose list emptied gets his fire back, on every node deriving from the list.
            foreach (var vehicle in _leaving.Keys) if (repo.IsAlive(vehicle)) Embarkation.ReleaseCarrier(repo, vehicle);

            CarryAndKill(repo);
            HitOccupants(repo);   // ⭐ CE-4622 K-3 (Checkpoint CP-6)
            Assistants(repo);

            // ⭐ CE-2152 (R-2011 C9) — a person carrier who is DOWN (incapacitated or dead) drops his load where he lies, alive: on the node
            //   that owns his list (its change replicates); the casualty stays down (Leave restores only someone in the fight).
            foreach (var carrier in _drops) { Embarkation.EjectAll(repo, carrier); Dropped++; }
            _drops.Clear();

            _boarders.Clear();
            _leavers.Clear();
            _leaving.Clear();
            _order.Clear();
        }

        /// <summary>⭐ I-2c — every passenger still aboard: carried where its position is owned, killed where its Health is owned once the
        /// vehicle is destroyed (CombatLife.IsDestroyed — a wreck, not a deleted entity).</summary>
        private void CarryAndKill(EntityRepository repo)
        {
            bool transforms = repo.IsComponentTypeRegistered<SimTransform>();
            bool velocities = repo.IsComponentTypeRegistered<SimVelocity>();
            bool damage = repo.Bus.IsRegistered<Fdp.Toolkit.Combat.Events.DamageAssessedEvent>();
            foreach (var p in repo.Query().With<IsEmbarkedTag>().Build())
            {
                var tag = repo.GetComponentRO<IsEmbarkedTag>(p);
                var vehicle = tag.VehicleEntity;
                if (!repo.IsAlive(vehicle)) continue;

                bool person = Embarkation.IsPersonCarrier(repo, vehicle);
                // ⭐ T-a (R-2017 T6) — a TEAM load is put down when EITHER man is down
                var second = person ? Embarkation.AssistantOf(repo, vehicle) : Entity.Null;
                if (person && (!Fdp.Toolkit.Combat.Injury.IsInTheFight(repo, vehicle)
                               || (!second.IsNull && (!repo.IsAlive(second) || !Fdp.Toolkit.Combat.Injury.IsInTheFight(repo, second)))))
                {
                    if (Embarkation.OwnsVehicle(repo, vehicle)) _drops.Add(vehicle);   // ⭐ CE-2152 C9 — dropped below, never killed with him
                    continue;
                }

                if (transforms && repo.HasComponent<SimTransform>(vehicle) && repo.HasComponent<SimTransform>(p) && Embarkation.OwnsPosition(repo, p))
                {
                    ref readonly var v = ref repo.GetComponentRO<SimTransform>(vehicle);
                    // ⭐ CE-1480 G4 — the carried transform is computed first and written only when it differs: GetComponentRW stamps the table's
                    //   chunk version, and a paused world re-stamped every passenger's chunk every frame though the vehicle had not moved
                    Vector3 newPos; Quaternion newRot;
                    if (StationOf(repo, vehicle, tag.Seat) is { } station)
                    {
                        // ⭐⭐ Weapon systems WS-2 (R-2016, DESIGN_Weapon_Systems §6) — a weapon in a STATION stands at the seat's place and
                        //   faces its way, in the host's frame (sim, not only drawn): its muzzle and its arc are where the station is.
                        station.Place(v.Position, v.Rotation, out newPos, out newRot);   // ⭐ SD-S5b — the one formula (DevicePoses walks a stack with it)
                    }
                    else
                    {
                        newPos = v.Position - Forward(v.Rotation) * (person ? Behind(repo, vehicle, tag.Mode) : 0f);   // ⭐ CE-2152 C10
                        newRot = v.Rotation;
                    }
                    ref readonly var at = ref repo.GetComponentRO<SimTransform>(p);
                    Quaternion atRot = at.Rotation;
                    if (at.Position != newPos || atRot != newRot)
                    {
                        ref var t = ref repo.GetComponentRW<SimTransform>(p);
                        t.Position = newPos;
                        t.Rotation = newRot;
                    }
                    if (velocities && repo.HasComponent<SimVelocity>(vehicle) && repo.HasComponent<SimVelocity>(p))
                        repo.GetComponentRW<SimVelocity>(p) = repo.GetComponentRO<SimVelocity>(vehicle);   // dead reckoning elsewhere
                }

                // ⭐ CE-4622 K-3 (Checkpoint CP-6, KV-1) — WHO DRIVES IS STATED: a vehicle's seat-0 man is its driver; once he is out of the fight the
                //   vehicle's move ENDS (CanMove cleared where the vehicle's capability is owned — the dispatcher then stops the move, CE-3091)
                if (tag.Seat == 0 && !person && !Fdp.Toolkit.Combat.Injury.IsInTheFight(repo, p)) StopDriverless(repo, vehicle);
                if (!Fdp.Toolkit.Combat.CombatLife.IsAlive(repo, p)) { _killSent.Remove(p); continue; }
                if (damage && Fdp.Toolkit.Combat.CombatLife.IsDestroyed(repo, vehicle) && !Survives(repo, vehicle)
                    && Embarkation.OwnsHealth(repo, p) && _killSent.Add(p))
                {
                    repo.Bus.Publish(new Fdp.Toolkit.Combat.Events.DamageAssessedEvent
                    {
                        HitEntity = p,
                        TotalDamage = repo.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(p).Current,
                    });
                    Killed++;
                }
            }
        }

        /// <summary>⭐ <c>CE-4622</c> K-3 — a soft skin's armour (mm, its side): a car at or under it lets a penetrating round reach who sits inside.</summary>
        public const float SoftSkinMaxArmourMm = 10f;
        /// <summary>⭐ <c>CE-4622</c> K-3 — the chance a penetrating hit on a soft skin also hits ONE occupant (seeded per hit).</summary>
        public const float OccupantHitChance = 0.3f;

        /// <summary>Occupant hits this process sent (rails).</summary>
        [field: Fdp.Core.WorldDerived("diagnostic counter of occupant hits")]
        public int OccupantHits { get; private set; }

        /// <summary>
        /// ⭐⭐ <c>CE-4622</c> K-3 (docs/designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md CP-6, KV-1) — the ONE soft-skin rule: the occupants of an
        /// enclosed car are hidden (never aimed at), but a PENETRATING round (a Kinetic hit that did damage) on a car whose side armour is at most
        /// <see cref="SoftSkinMaxArmourMm"/> may go on into the cabin: with <see cref="OccupantHitChance"/> (seeded by the vehicle, the frame and the hit's
        /// order) ONE occupant — a seat, never a station — takes the same damage, where his Health is owned. A person carrier (a man carrying a casualty)
        /// is not a car. Last frame's hits (this runs in Input, after the swap), so the order is the bus's — the same on every thread count.
        /// </summary>
        private void HitOccupants(EntityRepository repo)
        {
            if (!repo.Bus.IsRegistered<Fdp.Toolkit.Combat.Events.DamageAssessedEvent>()) return;
            uint frame = repo.HasSingleton<GlobalTime>() ? (uint)repo.GetSingleton<GlobalTime>().FrameNumber : 0u;
            uint k = 0;
            foreach (var ev in repo.ReadEvents<Fdp.Toolkit.Combat.Events.DamageAssessedEvent>())
            {
                k++;
                if (ev.TotalDamage <= 0f || ev.Effect != Fdp.Toolkit.Combat.HitEffect.Kinetic || ev.IsRemote) continue;
                var v = ev.HitEntity;
                if (!repo.IsAlive(v) || !repo.HasComponent<PassengerBuffer>(v) || Embarkation.IsPersonCarrier(repo, v)) continue;
                ref readonly var buf = ref repo.GetComponentRO<PassengerBuffer>(v);
                if (buf.Count <= 0) continue;
                if (Fdp.Toolkit.Combat.CombatTkb.PlatformOf(repo, v) is not { } platform || platform.ArmorSide > SoftSkinMaxArmourMm) continue;
                uint h = (uint)v.Index * 0x9E3779B1u ^ frame * 0x85EBCA77u ^ k * 0xC2B2AE3Du;
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
                if ((h & 0xFFFF) / 65536f >= OccupantHitChance) continue;
                int seat = (int)((h >> 16) % PassengerBuffer.Capacity), tries = 0;
                Entity p = Entity.Null;
                for (; tries < PassengerBuffer.Capacity; tries++, seat = (seat + 1) % PassengerBuffer.Capacity)
                {
                    p = buf.Passengers[seat];
                    if (!p.IsNull && repo.IsAlive(p) && StationOf(repo, v, seat) == null) break;
                }
                if (tries == PassengerBuffer.Capacity || !Embarkation.OwnsHealth(repo, p) || !Fdp.Toolkit.Combat.CombatLife.IsAlive(repo, p)) continue;
                repo.Bus.Publish(new Fdp.Toolkit.Combat.Events.DamageAssessedEvent { HitEntity = p, TotalDamage = ev.TotalDamage, Source = ev.Source });
                OccupantHits++;
            }
        }

        /// <summary>⭐ <c>CE-4622</c> K-3 — a vehicle whose driver is out of the fight stops: its <c>CanMove</c> cleared where its capability is owned.</summary>
        private static void StopDriverless(EntityRepository repo, Entity vehicle)
        {
            if (!repo.IsComponentTypeRegistered<ActorCapabilityState>() || !repo.HasComponent<ActorCapabilityState>(vehicle)) return;
            if (repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkAuthority>()
                && repo.HasComponent<Fdp.Toolkit.Replication.Components.NetworkAuthority>(vehicle) && !repo.HasAuthority<ActorCapabilityState>(vehicle)) return;
            if ((repo.GetComponentRO<ActorCapabilityState>(vehicle).Capabilities & ActorCapabilities.CanMove) == 0) return;
            repo.GetComponentRW<ActorCapabilityState>(vehicle).Capabilities &= ~ActorCapabilities.CanMove;
        }

        /// <summary>⭐ WS-2 — seat <paramref name="seat"/> of <paramref name="vehicle"/> when it is a station (a type fact), else null.</summary>
        private static StationSeat? StationOf(EntityRepository repo, Entity vehicle, int seat)
        {
            if (!repo.IsComponentTypeRegistered<CarrierStations>() || !repo.HasComponent<CarrierStations>(vehicle)) return null;
            ref readonly var stations = ref repo.GetComponentRO<CarrierStations>(vehicle);
            // ⭐ CE-4621 PF-1 — a FIRING seat's man sits at its place too (his eye and muzzle are where the seat is, not the hull's ground point)
            return stations.IsStation(seat) || stations.IsFiringSeat(seat) ? stations.Seats[seat] : null;
        }

        /// <summary>⭐ CE-2152 (R-2011 C9) — the type's passengers survive its destruction (<c>Gen.Carrier.DiesWithCarrier = false</c>).</summary>
        private static bool Survives(EntityRepository repo, Entity vehicle)
            => repo.IsComponentTypeRegistered<CarrierSeats>() && repo.HasComponent<CarrierSeats>(vehicle)
               && repo.GetComponentRO<CarrierSeats>(vehicle).PassengersSurvive != 0;

        /// <summary>⭐ CE-2152 (R-2011 C10) — how far behind a person carrier the casualty is held for its mode. ⭐ T-a — a stretcher's casualty
        /// lies half way to the rear handle.</summary>
        private static float Behind(EntityRepository repo, Entity carrier, CarryMode mode)
        {
            ref readonly var pc = ref repo.GetComponentRO<PersonCarrier>(carrier);
            return mode switch
            {
                CarryMode.Dragged   => pc.DragBehindMetres,
                CarryMode.Stretcher => 0.5f * pc.AssistantBehindMetres,
                _                   => pc.CarryBehindMetres,
            };
        }

        /// <summary>
        /// ⭐⭐ Team carry T-a (R-2017 T1, T3) — the SECOND man, derived on every node from his carrier's list as the passenger side is:
        /// named and not tagged ⇒ <see cref="Embarkation.Assist"/>; tagged and no longer named ⇒ <see cref="Embarkation.ReleaseAssistant"/>.
        /// While he assists he is PLACED at the rear handle (<see cref="PersonCarrier.AssistantBehindMetres"/> behind, facing the carrier's
        /// way, at his velocity) where his position is owned — the pair moves as one at the carrier's speed.
        /// </summary>
        private void Assistants(EntityRepository repo)
        {
            if (!repo.IsComponentTypeRegistered<IsAssistingTag>() || !repo.IsComponentTypeRegistered<PersonCarrier>()) return;
            bool transforms = repo.IsComponentTypeRegistered<SimTransform>();
            bool velocities = repo.IsComponentTypeRegistered<SimVelocity>();
            foreach (var carrier in repo.Query().With<PersonCarrier>().With<PassengerBuffer>().Build())
            {
                var second = repo.GetComponentRO<PassengerBuffer>(carrier).Assistant;
                if (second.IsNull || !repo.IsAlive(second)) continue;
                if (!(repo.HasComponent<IsAssistingTag>(second) && repo.GetComponentRO<IsAssistingTag>(second).Carrier == carrier))
                    Embarkation.Assist(repo, second, carrier);
                if (transforms && repo.HasComponent<SimTransform>(carrier) && repo.HasComponent<SimTransform>(second) && Embarkation.OwnsPosition(repo, second))
                {
                    ref readonly var c = ref repo.GetComponentRO<SimTransform>(carrier);
                    ref var t = ref repo.GetComponentRW<SimTransform>(second);
                    t.Position = c.Position - Forward(c.Rotation) * repo.GetComponentRO<PersonCarrier>(carrier).AssistantBehindMetres;
                    t.Rotation = c.Rotation;
                    if (velocities && repo.HasComponent<SimVelocity>(carrier) && repo.HasComponent<SimVelocity>(second))
                        repo.GetComponentRW<SimVelocity>(second) = repo.GetComponentRO<SimVelocity>(carrier);
                }
            }
            foreach (var man in repo.Query().With<IsAssistingTag>().Build())
            {
                var carrier = repo.GetComponentRO<IsAssistingTag>(man).Carrier;
                if (repo.IsAlive(carrier) && Embarkation.AssistantOf(repo, carrier) == man) continue;
                _released.Add(man);
            }
            foreach (var man in _released) { Embarkation.ReleaseAssistant(repo, man); Released++; }
            _released.Clear();
        }

        /// <summary>The heading (X-forward, Z-up), flattened to the ground.</summary>
        private static Vector3 Forward(Quaternion rotation)
        {
            var f = Vector3.Transform(Vector3.UnitX, rotation);
            f.Z = 0f;
            return f.LengthSquared() > 1e-6f ? Vector3.Normalize(f) : Vector3.UnitX;
        }

        private bool Listed(Entity p)
        {
            foreach (var (b, _, _) in _boarders) if (b == p) return true;
            return false;
        }

        /// <summary>Beside the hull at the leaving order; a vehicle already gone leaves the body where it is.</summary>
        private static Vector3 StandAt(EntityRepository repo, Entity passenger, Entity vehicle, int index, int count)
        {
            if (!vehicle.IsNull && repo.IsAlive(vehicle)) return Embarkation.DismountPoint(repo, vehicle, index, count);
            return repo.IsComponentTypeRegistered<SimTransform>() && repo.HasComponent<SimTransform>(passenger)
                ? repo.GetComponentRO<SimTransform>(passenger).Position
                : Fdp.Core.WorldPoint.Zero;
        }
    }
}
