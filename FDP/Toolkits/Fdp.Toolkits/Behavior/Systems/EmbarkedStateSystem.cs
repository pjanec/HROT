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
    /// ⭐ Runs in Input, before <see cref="CognitiveInterruptSystem"/>, so a passenger's stripped CanMove is in place before that system
    /// compares capabilities (it skips a tagged entity). 📄 docs/DESIGN_Entity_Interactions.md §7.1c.
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    [UpdateBefore(typeof(CognitiveInterruptSystem))]
    public sealed class EmbarkedStateSystem : IEcsModuleSystem
    {
        private readonly List<(Entity passenger, Entity vehicle)> _boarders = new();
        private readonly List<(Entity passenger, Entity vehicle)> _leavers = new();
        private readonly Dictionary<Entity, int> _leaving = new();
        private readonly Dictionary<Entity, int> _order = new();

        /// <summary>Passengers this node boarded from a list (a rail reads it).</summary>
        public long Boarded { get; private set; }
        /// <summary>Passengers this node took off because their vehicle no longer lists them.</summary>
        public long Left { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.IsComponentTypeRegistered<PassengerBuffer>() || !repo.IsComponentTypeRegistered<IsEmbarkedTag>()) return;

            foreach (var vehicle in repo.Query().With<PassengerBuffer>().Build())
            {
                ref readonly var buffer = ref repo.GetComponentRO<PassengerBuffer>(vehicle);
                for (int i = 0; i < buffer.Count && i < PassengerBuffer.Capacity; i++)
                {
                    var p = buffer.Passengers[i];
                    if (!repo.IsAlive(p)) continue;
                    if (repo.HasComponent<IsEmbarkedTag>(p) && repo.GetComponentRO<IsEmbarkedTag>(p).VehicleEntity == vehicle) continue;
                    _boarders.Add((p, vehicle));
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

            foreach (var (p, vehicle) in _boarders) { Embarkation.Board(repo, p, vehicle); Boarded++; }

            foreach (var (p, vehicle) in _leavers)
            {
                _order.TryGetValue(vehicle, out var k);
                _order[vehicle] = k + 1;
                Embarkation.Leave(repo, p, StandAt(repo, p, vehicle, k, _leaving[vehicle]));
                Left++;
            }

            _boarders.Clear();
            _leavers.Clear();
            _leaving.Clear();
            _order.Clear();
        }

        private bool Listed(Entity p)
        {
            foreach (var (b, _) in _boarders) if (b == p) return true;
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
