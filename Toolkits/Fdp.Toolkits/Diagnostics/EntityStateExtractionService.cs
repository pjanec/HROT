using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;

namespace Fdp.Toolkit.Diagnostics
{
    /// <summary>
    /// Default implementation of <see cref="IEntityStateExtractionService"/>.
    /// Walks the <see cref="EntityRepository"/> directly without touching any Presentation code.
    /// </summary>
    public sealed class EntityStateExtractionService : IEntityStateExtractionService
    {
        private readonly EntityRepository  _repo;
        private readonly NetworkEntityMap? _entityMap;

        /// <param name="repo">The world repository to query.</param>
        /// <param name="entityMap">
        /// Optional map for NetworkId lookups.  When null the service still works but
        /// <see cref="EntityStateDumpDto.NetworkId"/> will be 0 for entities where the
        /// NetworkIdentity component is absent.
        /// </param>
        public EntityStateExtractionService(EntityRepository repo, NetworkEntityMap? entityMap = null)
        {
            _repo      = repo      ?? throw new ArgumentNullException(nameof(repo));
            _entityMap = entityMap;
        }

        /// <inheritdoc/>
        public IReadOnlyList<EntityStateDumpDto> ExtractEntities(IReadOnlyList<long>? networkIds = null)
        {
            // Build a fast lookup set when filtering is requested.
            HashSet<long>? filterSet = null;
            if (networkIds != null && networkIds.Count > 0)
            {
                filterSet = new HashSet<long>(networkIds);
            }

            var registeredTypes = _repo.GetRegisteredComponentTypes();
            var result = new List<EntityStateDumpDto>();

            for (int i = 0; i <= _repo.MaxEntityIndex; i++)
            {
                var entity = _repo.GetEntityByIndex(i);
                if (entity == Entity.Null || !_repo.IsAlive(entity)) continue;

                // Resolve network ID from NetworkIdentity component if available.
                long networkId = 0;
                if (_repo.HasComponent<NetworkIdentity>(entity))
                    networkId = _repo.GetComponent<NetworkIdentity>(entity).Value;

                // Apply filter.
                if (filterSet != null && !filterSet.Contains(networkId)) continue;

                // Extract component data.
                var components = new Dictionary<string, object>();
                foreach (var kvp in registeredTypes)
                {
                    var componentType  = kvp.Key;
                    var componentTable = kvp.Value;

                    int typeId = componentTable.ComponentTypeId;
                    if (!_repo.HasComponentByTypeId(entity, typeId)) continue;

                    object? rawObj = null;
                    try { rawObj = componentTable.GetRawObject(entity.Index); }
                    catch { /* unmanaged component not present for this slot — skip */ }

                    if (rawObj != null)
                        components[componentType.Name] = rawObj;
                }

                result.Add(new EntityStateDumpDto
                {
                    NetworkId       = networkId,
                    LocalIndex      = entity.Index,
                    LocalGeneration = entity.Generation,
                    Components      = components,
                });
            }

            return result;
        }
    }
}
