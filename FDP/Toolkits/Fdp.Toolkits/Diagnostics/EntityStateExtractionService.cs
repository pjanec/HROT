using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
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
                    //
                    // Guard: components that carry non-finite float values (NaN / ±Infinity)
                    // cause JsonSerializer.SerializeToNode to throw a JsonReaderException
                    // when it parses the internal write buffer back into a JsonNode (the
                    // .NET JSON DOM does not support named float literals).
                    //
                    // FALLBACK on JsonException: instead of returning an empty component dict
                    // (which made whole entity classes permanently un-inspectable), fall back to
                    // the reflection-based per-component extraction (the else-branch below).
                    // That populates the dict with raw component objects, which are then
                    // serialized by DebugApiService.DumpToJsonNode via DebugApiDumpOptions —
                    // the NaN-safe converters (NonFiniteFloatSentinelConverter etc.) in those
                    // options render non-finite fields as string sentinels ("NaN"/"Infinity"/
                    // "-Infinity") so the output is valid standard JSON and the entity remains
                    // fully inspectable.  NOTE: fallback output is less readable than the
                    // translator/DTO path (raw struct fields vs. translator-shaped DTOs) but
                    // all finite components are preserved and non-finite fields are visible.
                    JsonObject componentsJson;
                    bool serializerSucceeded = true;
                    try
                    {
                        componentsJson = _serializer.SerializeEntity(_repo, entity, resolver!, snapshotableMask);
                    }
                    catch (System.Text.Json.JsonException)
                    {
                        // Serializer failed due to non-finite float — flag for fallback.
                        serializerSucceeded = false;
                        componentsJson = new JsonObject(); // unused placeholder
                    }

                    if (serializerSucceeded)
                    {
                        // Walk the JsonNode tree and replace any remaining non-finite numeric
                        // JsonValues (e.g., translator-produced float.NaN via JsonValue.Create(f))
                        // with string sentinels before calling ToJsonString(), which would otherwise
                        // emit bare NaN/Infinity literals that DefaultRelaxed Deserialize rejects.
                        SanitizeNonFinite(componentsJson);
                        components = JsonSerializer.Deserialize<Dictionary<string, object>>(
                            componentsJson.ToJsonString(), FdpJsonOptionsRegistry.DefaultRelaxed)
                            ?? new Dictionary<string, object>();
                    }
                    else
                    {
                        // Reflection-based fallback: enumerate registered component types and
                        // collect raw component objects.  DebugApiService.DumpToJsonNode will
                        // serialize this dict through DebugApiDumpOptions whose NaN-safe
                        // converters emit string sentinels for any non-finite float/double.
                        var fallbackTypes = _repo.GetRegisteredComponentTypes();
                        components = new Dictionary<string, object>();
                        foreach (var kvp in fallbackTypes)
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

        /// <summary>
        /// Recursively walks a <see cref="JsonNode"/> tree and replaces any non-finite
        /// numeric values (NaN, +Infinity, -Infinity) with their string sentinels so that
        /// the resulting JSON is spec-compliant and can be deserialised without error.
        /// </summary>
        private static void SanitizeNonFinite(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                // Collect keys first to avoid modifying the collection while iterating.
                var keys = new List<string>(obj.Count);
                foreach (var kvp in obj)
                    keys.Add(kvp.Key);

                foreach (var key in keys)
                {
                    var child = obj[key];
                    if (child is JsonValue val && TryGetNonFiniteSentinel(val, out var sentinel))
                    {
                        obj.Remove(key);
                        obj[key] = JsonValue.Create(sentinel);
                    }
                    else
                    {
                        SanitizeNonFinite(child);
                    }
                }
            }
            else if (node is JsonArray arr)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    var child = arr[i];
                    if (child is JsonValue val && TryGetNonFiniteSentinel(val, out var sentinel))
                    {
                        arr[i] = JsonValue.Create(sentinel);
                    }
                    else
                    {
                        SanitizeNonFinite(child);
                    }
                }
            }
            // JsonValue leaf with a finite value — nothing to do.
        }

        /// <summary>
        /// If <paramref name="value"/> holds a non-finite <c>double</c> or <c>float</c>,
        /// sets <paramref name="sentinel"/> to the appropriate string ("NaN", "Infinity",
        /// or "-Infinity") and returns <c>true</c>.  Returns <c>false</c> otherwise.
        /// </summary>
        private static bool TryGetNonFiniteSentinel(JsonValue value, out string sentinel)
        {
            if (value.TryGetValue(out double d) && !double.IsFinite(d))
            {
                sentinel = double.IsNaN(d) ? "NaN"
                         : double.IsPositiveInfinity(d) ? "Infinity"
                         : "-Infinity";
                return true;
            }
            if (value.TryGetValue(out float f) && !float.IsFinite(f))
            {
                sentinel = float.IsNaN(f) ? "NaN"
                         : float.IsPositiveInfinity(f) ? "Infinity"
                         : "-Infinity";
                return true;
            }
            sentinel = string.Empty;
            return false;
        }
    }
}
