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
    public sealed class EmbarkedStateSystem : IEcsModuleSystem
    {
        private readonly List<(Entity passenger, Entity vehicle, int seat)> _boarders = new();
        private readonly List<(Entity passenger, Entity vehicle)> _leavers = new();
        private readonly Dictionary<Entity, int> _leaving = new();
        private readonly Dictionary<Entity, int> _order = new();
        private readonly HashSet<Entity> _killSent = new();

        /// <summary>Passengers this node boarded from a list (a rail reads it).</summary>
        public long Boarded { get; private set; }
        /// <summary>Passengers this node took off because their vehicle no longer lists them.</summary>
        public long Left { get; private set; }
        /// <summary>Passengers this node killed because their vehicle was destroyed (lethal damage events published).</summary>
        public long Killed { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
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
                        && tag.VehicleEntity == vehicle && tag.Seat == i) continue;
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

            CarryAndKill(repo);

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
                var vehicle = repo.GetComponentRO<IsEmbarkedTag>(p).VehicleEntity;
                if (!repo.IsAlive(vehicle)) continue;

                if (transforms && repo.HasComponent<SimTransform>(vehicle) && repo.HasComponent<SimTransform>(p) && Embarkation.OwnsPosition(repo, p))
                {
                    ref readonly var v = ref repo.GetComponentRO<SimTransform>(vehicle);
                    ref var t = ref repo.GetComponentRW<SimTransform>(p);
                    t.Position = v.Position;
                    t.Rotation = v.Rotation;
                    if (velocities && repo.HasComponent<SimVelocity>(vehicle) && repo.HasComponent<SimVelocity>(p))
                        repo.GetComponentRW<SimVelocity>(p) = repo.GetComponentRO<SimVelocity>(vehicle);   // dead reckoning elsewhere
                }

                if (!Fdp.Toolkit.Combat.CombatLife.IsAlive(repo, p)) { _killSent.Remove(p); continue; }
                if (damage && Fdp.Toolkit.Combat.CombatLife.IsDestroyed(repo, vehicle) && Embarkation.OwnsHealth(repo, p) && _killSent.Add(p))
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
                : Vector3.Zero;
        }
    }
}
