using System;
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
    /// nothing to resolve pays nothing. ⛔ A key two entities carry is a loud error (a key is unique per scope).
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

        public bool TryResolve(in RefSpec spec, out EntityRef entity) => Scope.TryResolve(spec, out entity);
        public bool TryPositionOf(EntityRef entity, out WorldPoint position) => Scope.TryPositionOf(entity, out position);
    }
}
