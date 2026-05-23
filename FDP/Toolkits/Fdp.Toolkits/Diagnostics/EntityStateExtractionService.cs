using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Fdp.Core;
using Fdp.Core.Serialization;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Scenario;

namespace Fdp.Toolkit.Diagnostics
{
    /// <summary>
    /// Default implementation of <see cref="IEntityStateExtractionService"/>.
    /// Walks the <see cref="EntityRepository"/> directly without touching any Presentation code.
    /// When a <see cref="ScenarioSerializer"/> is supplied, component data is produced via the
    /// unified serialization pipeline (translator chain + FdpAutoSerializer) so that custom
    /// translators such as <c>BrainBlackboardTranslator</c> and <c>Blackboard1024Translator</c>
    /// emit readable DTO output instead of raw fixed-buffer bytes.
    /// </summary>
    public sealed class EntityStateExtractionService : IEntityStateExtractionService
    {
        private readonly EntityRepository  _repo;
        private readonly NetworkEntityMap? _entityMap;
        private readonly ScenarioSerializer? _serializer;

        /// <param name="repo">The world repository to query.</param>
        /// <param name="entityMap">
        /// Optional map for NetworkId lookups.  When null the service still works but
        /// <see cref="EntityStateDumpDto.NetworkId"/> will be 0 for entities where the
        /// NetworkIdentity component is absent.
        /// </param>
        /// <param name="serializer">
        /// Optional scenario serializer.  When supplied, <see cref="ExtractEntities"/> routes
        /// component extraction through the translator pipeline so custom translators and
        /// FdpAutoSerializer produce the same output as the entity inspector clipboard path.
        /// When null, falls back to the direct reflection-based extraction.
        /// </param>
        public EntityStateExtractionService(EntityRepository repo, NetworkEntityMap? entityMap = null, ScenarioSerializer? serializer = null)
        {
            _repo       = repo ?? throw new ArgumentNullException(nameof(repo));
            _entityMap  = entityMap;
            _serializer = serializer;
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

            var result = new List<EntityStateDumpDto>();

            // Pre-compute the snapshotable mask once when using the serializer path.
            var snapshotableMask = _serializer != null ? _repo.GetSnapshotableMask() : default;
            var resolver         = _serializer != null ? new DiagnosticGuidResolver()  : null;

            var registeredTypes = _serializer == null ? _repo.GetRegisteredComponentTypes() : null;

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

                Dictionary<string, object> components;

                if (_serializer != null)
                {
                    // Unified path: route through the translator pipeline so custom
                    // translators (BrainBlackboardTranslator, Blackboard1024Translator)
                    // and FdpAutoSerializer emit readable DTO output.
                    // TODO(ecs-512): remove projection when SerializeEntity upgraded to BitMask512
                    BitMask256 snapshotable256 = Unsafe.As<BitMask512, BitMask256>(ref snapshotableMask);
                    var componentsJson = _serializer.SerializeEntity(_repo, entity, resolver!, snapshotable256);
                    components = JsonSerializer.Deserialize<Dictionary<string, object>>(
                        componentsJson.ToJsonString(), FdpJsonOptionsRegistry.DefaultRelaxed)
                        ?? new Dictionary<string, object>();
                }
                else
                {
                    // Fallback: direct reflection-based extraction.
                    components = new Dictionary<string, object>();
                    foreach (var kvp in registeredTypes!)
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
                }

                result.Add(new EntityStateDumpDto
                {
                    EntityId   = new[] { entity.Index, entity.Generation },
                    NetworkId  = networkId,
                    Components = components,
                });
            }

            return result;
        }
    }
}
