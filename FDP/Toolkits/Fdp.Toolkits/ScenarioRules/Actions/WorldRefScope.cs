using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;

namespace Fdp.Toolkit.ScenarioRules.Actions
{
    /// <summary>
    /// ⭐ rules S2-d (CE-4162) — the <see cref="IRefScope"/> of a LIVE world: every entity with a network id, by its <see cref="RefKey"/> and
    /// <see cref="EntityGuid"/>. What a step's authored <c>"@Key"</c> / GUID link resolves in when the step starts (the scenario-load scope is
    /// <see cref="ScenarioRefScope"/>; the walker — <see cref="RefResolve"/> — is the same). Built on the FIRST lookup only, so a step with
    /// nothing to resolve pays nothing. ⭐ SR-188: a key two entities carry resolves to the FIRST, with a warning (<see cref="Ambiguous"/>).
    /// ⭐ Macros debt A1 — the ONE live-world scope: the macro expander resolves an instance's <c>@Key</c> / GUID args here too (it had its own copy).
    /// </summary>
    public sealed class WorldRefScope : IRefScope
    {
        private readonly EntityRepository _world;
        private ScenarioRefScope? _scope;

        public WorldRefScope(EntityRepository world) { _world = world ?? throw new ArgumentNullException(nameof(world)); }

        public string Describe => "this world";

        private ScenarioRefScope Scope => _scope ??= Build();

        private ScenarioRefScope Build()
        {
            var scope = new ScenarioRefScope(Describe);
            bool keys = _world.IsComponentTypeRegistered<RefKey>(), guids = _world.IsComponentTypeRegistered<EntityGuid>(),
                 pos = _world.IsComponentTypeRegistered<SimTransform>();
            foreach (var e in _world.Query().With<NetworkIdentity>().Build())
            {
                string? key = keys && _world.HasComponent<RefKey>(e) ? _world.GetComponentRO<RefKey>(e).Value.ToString() : null;
                Guid guid = guids && _world.HasComponent<EntityGuid>(e) ? _world.GetComponentRO<EntityGuid>(e).Value : Guid.Empty;
                WorldPoint? at = pos && _world.HasComponent<SimTransform>(e) ? _world.GetComponentRO<SimTransform>(e).Position : null;
                scope.Add(new EntityRef(_world.GetComponentRO<NetworkIdentity>(e).Value), key, guid, at);
            }
            return scope;
        }

        /// <summary>⭐ SR-188 — keys two or more entities carry, with how many (the first match is used).</summary>
        public IReadOnlyDictionary<string, int> Ambiguous => Scope.Ambiguous;

        public bool TryResolve(in RefSpec spec, out EntityRef entity) => Scope.TryResolve(spec, out entity);
        public bool TryPositionOf(EntityRef entity, out WorldPoint position) => Scope.TryPositionOf(entity, out position);
    }
}
