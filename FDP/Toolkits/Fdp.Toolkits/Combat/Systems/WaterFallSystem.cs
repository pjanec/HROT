using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Combat.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-4692</c> BL-9 (📄 docs/designs/engineering/DESIGN_Bridge_Laying.md §3, §9 Q-3) — whoever stood on a laid bridge's span as it FELL goes into the river: there is no water
    /// model (nothing swims in v1), so a man there is out of the fight and a vehicle is destroyed — through the ordinary <see cref="DamageAssessedEvent"/> to each one's Health owner
    /// (R-171), lethal damage. A fallen laid bridge leaves NO rubble (its obstacle is simply removed — <see cref="ObstacleRemovalSystem"/>; ⛔ never the authored bridge's mound).
    /// <para>Runs on the DECIDING node — the one that owns the deck's Health (<see cref="HealthOwnership.AppliesHere"/>), the node that removes it — in the frame its Health reaches 0,
    /// before the removal (the deck is still in the world to say who stood on it: <see cref="TerrainWorld.DeckUnder"/>, the SLAB — a ramp runs onto the bank). Once per deck.</para>
    /// <para>⭐ The parallel contract: serial; it changes no other entity — it PUBLISHES events (the Health owner applies them); a cached query and a handled set are its only state;
    /// 0 B while no deck falls. ⛔ Never in a replay (the recording holds the deaths).</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class WaterFallSystem : IEcsModuleSystem
    {
        /// <summary>The damage a fall into the river deals: more than anything's Health (lethal).</summary>
        public const float FallDamage = 1.0e6f;

        private EntityRepository? _for;
        private EntityQuery? _decks, _bodies;
        [Fdp.Core.WorldDerived("decks already handled: a fall is assessed once per deck")]
        private readonly HashSet<Entity> _handled = new();

        /// <summary>Who fell into the river so far (rails / diagnostics).</summary>
        [field: Fdp.Core.WorldDerived("diagnostic counter of falls")]
        public int Fell { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<StaticObstacle>() || !repo.IsComponentTypeRegistered<Health>()
                || !repo.IsComponentTypeRegistered<SimTransform>() || !repo.HasSingletonManaged<TerrainWorld>()) return;
            var world = repo.GetSingletonManaged<TerrainWorld>();
            if (world == null || !world.HasObstacleDecks) { if (_handled.Count > 0) _handled.Clear(); return; }
            if (!ReferenceEquals(repo, _for) || _decks == null || _bodies == null)
            {
                _decks = repo.Query().With<StaticObstacle>().With<Health>().Build();   // Active only, as the removal's
                _bodies = repo.Query().With<Health>().With<SimTransform>().Build();
                _for = repo;
            }
            foreach (var deck in _decks)
            {
                if (repo.GetComponentRO<StaticObstacle>(deck).Kind != ObstacleKind.WalkableDeck) continue;
                if (repo.GetComponentRO<Health>(deck).Current > 0f || !HealthOwnership.AppliesHere(repo, deck) || !_handled.Add(deck)) continue;
                foreach (var e in _bodies)
                {
                    if (e == deck || TerrainObstacles.IsObstacle(repo, e) || repo.GetComponentRO<Health>(e).Current <= 0f) continue;
                    var at = repo.GetComponentRO<SimTransform>(e).Position;
                    if (world.DeckUnder(at.X, at.Y, at.Z, null, slabOnly: true) != deck.Index) continue;
                    repo.Bus.Publish(new DamageAssessedEvent { HitEntity = e, TotalDamage = FallDamage, Source = Entity.Null, OriginalSource = Entity.Null });
                    Fell++;
                }
            }
            if (_handled.Count > 0) _handled.RemoveWhere(h => !repo.IsAlive(h));   // a removed deck's handle is free again
        }
    }
}
