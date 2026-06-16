using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Core.Serialization.Migrations;

namespace Fdp.Toolkit.Scenario
{
    /// <summary>
    /// Serializes and deserializes an <see cref="EntityRepository"/> to/from an
    /// in-memory <see cref="JsonObject"/> DOM using the following schema (Phase 2):
    /// <code>
    /// {
    ///   "$meta": { "docType": "...", "schemaVersion": 1 },
    ///   "Header": { "TkbName": "..." },
    ///   "Entities": {
    ///     "&lt;guid&gt;": { "ComponentName": { "field": "..." } }
    ///   }
    /// }
    /// </code>
    /// Legacy files that carry <c>Header.SubsystemType</c> instead of <c>$meta</c> are
    /// accepted on load for backward compatibility.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Build via <see cref="ScenarioSerializerBuilder"/>.  Do not construct directly.
    /// </para>
    /// <para>
    /// <b>Save pipeline per entity:</b>
    /// <list type="number">
    ///   <item>Obtain <c>remainingMask = globalSaveableMask &amp; entityComponentMask</c>.</item>
    ///   <item>For each registered <see cref="IEntityScenarioTranslator"/>: if
    ///     <c>CanTranslate</c> → <c>Extract</c> → add named entries → clear consumed bits.</item>
    ///   <item><see cref="FdpAutoSerializer"/> processes remaining set bits.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Load pipeline:</b> Peek <c>Header.SubsystemType</c>; on mismatch return
    /// immediately.  Otherwise two-pass: create entities → resolve GUIDs → inject
    /// components.  Optionally stamps <see cref="Fdp.Core.EpisodeTag"/> when <c>asEpisode == true</c>.
    /// </para>
    /// </remarks>
    public sealed class ScenarioSerializer
    {
        /// <summary>
        /// Current scenario schema version written into the document <c>$meta</c> envelope.
        /// v2: entity DIS type is now persisted via the <c>DisEntityType</c> translator
        /// (added on top of the v1 component set). Load remains backward-compatible — v1
        /// files simply lack the <c>DisEntityType</c> entry.
        /// </summary>
        public const int CurrentSchemaVersion = 2;

        private readonly string _subsystemType;
        private readonly IEntityScenarioTranslator[] _translators;

        /// <summary>Compiled 1:1 fallback serializer.</summary>
        public FdpAutoSerializer AutoSerializer { get; }

        internal ScenarioSerializer(
            string subsystemType,
            IEntityScenarioTranslator[] translators,
            FdpAutoSerializer autoSerializer)
        {
            _subsystemType = subsystemType;
            _translators   = translators;
            AutoSerializer = autoSerializer;
        }

        /// <summary>
        /// The subsystem type string this serializer was built for.
        /// Used by application-layer handlers to determine if a scenario file belongs
        /// to this subsystem via <c>Hrot.Common.Scenario.HrotScenarioEnvelope.IsMatchingSubsystem</c>.
        /// </summary>
        public string SubsystemType => _subsystemType;

        /// <summary>
        /// The translators registered with this serializer.
        /// Consumed by <c>StagingEntityExtractor</c> to build the translator-component
        /// exclusion mask at extraction time.
        /// </summary>
        public IReadOnlyList<IEntityScenarioTranslator> Translators => _translators;

        /// <summary>
        /// Parses <paramref name="jsonText"/> and deserializes entities into <paramref name="repo"/>.
        /// Convenience overload that avoids exposing <see cref="JsonObject"/> to callers that
        /// do not otherwise depend on <c>System.Text.Json</c>.
        /// </summary>
        public void Deserialize(
            EntityRepository repo,
            string jsonText,
            bool asEpisode  = false,
            Guid? episodeId = null)
        {
            if (repo     == null) throw new ArgumentNullException(nameof(repo));
            if (jsonText == null) throw new ArgumentNullException(nameof(jsonText));

            var dom = JsonNode.Parse(jsonText)?.AsObject();
            if (dom == null)
                throw new InvalidOperationException(
                    "[ScenarioSerializer] Deserialize(string): failed to parse JSON text.");
            Deserialize(repo, dom, asEpisode, episodeId);
        }

        // ── Serialize ────────────────────────────────────────────────────────────

        /// <summary>
        /// Serializes all entities in <paramref name="repo"/> (excluding those bearing
        /// <see cref="ScenarioIgnoreTag"/>) into an in-memory <see cref="JsonObject"/>.
        /// </summary>
        /// <param name="repo">Repository to serialize.</param>
        /// <param name="header">Header metadata written to <c>dom["Header"]</c>.</param>
        /// <returns>The assembled scenario DOM.</returns>
        public JsonObject Serialize(EntityRepository repo, ScenarioHeader header)
        {
            if (repo == null)    throw new ArgumentNullException(nameof(repo));
            if (header == null)  throw new ArgumentNullException(nameof(header));

            // ── Pass 1: enumerate entities, build save-side IGuidResolver ────────
            var guidToEntity = new Dictionary<Guid, Entity>();
            var entityToGuid = new Dictionary<Entity, Guid>();
            var liveEntities = CollectSaveableEntities(repo);

            foreach (var entity in liveEntities)
            {
                var guid = Guid.NewGuid();
                entityToGuid[entity] = guid;
                guidToEntity[guid]   = entity;
            }

            IGuidResolver saveResolver = new SaveResolver(entityToGuid);

            // ── Pass 2: serialize each entity ────────────────────────────────────
            var globalSaveable = repo.GetSaveableMask();
            var entitiesNode   = new JsonObject();

            foreach (var entity in liveEntities)
            {
                var guidStr     = entityToGuid[entity].ToString();
                var entityNode  = new JsonObject();

                // Per-entity saveable mask = global saveable AND entity's own components.
                var entityComponents512 = repo.GetComponentMask(entity.Index);
                entityComponents512.BitwiseAnd(globalSaveable);
                var remainingMask = entityComponents512; // mutable copy

                // Run custom translators first.
                foreach (var translator in _translators)
                {
                    if (!translator.CanTranslate(repo, entity)) continue;

                    var entries = translator.Extract(repo, entity, saveResolver);
                    foreach (var kv in entries)
                    {
                        var rawValue = kv.Value;
                        JsonNode? node = rawValue switch
                        {
                            JsonNode jn => jn,
                            string  s  => JsonValue.Create(s),
                            int     i  => JsonValue.Create(i),
                            float   f  => JsonValue.Create(f),
                            double  d  => JsonValue.Create(d),
                            bool    b  => JsonValue.Create(b),
                            null       => throw new InvalidOperationException(
                                $"[ScenarioSerializer] Translator '{translator.GetType().Name}' returned null for key '{kv.Key}'. " +
                                "Translator.Extract must return non-null values."),
                            _          => throw new InvalidOperationException(
                                $"[ScenarioSerializer] Translator '{translator.GetType().Name}' returned unsupported payload type " +
                                $"'{rawValue.GetType().Name}' for key '{kv.Key}'. " +
                                "Supported types: JsonNode, string, int, float, double, bool.")
                        };
                        entityNode.Add(kv.Key, node);
                    }

                    // Clear consumed bits from remaining mask.
                    ClearConsumed(ref remainingMask, translator.GetConsumedComponentsMask());
                }

                // Auto-serializer handles all remaining bits.
                for (int bit = 0; bit < FdpConfig.MAX_COMPONENT_TYPES; bit++)
                {
                    if (!remainingMask.IsSet(bit)) continue;

                    var fieldObj = AutoSerializer.TryExtract(repo, entity, bit, saveResolver);
                    if (fieldObj == null) continue;

                    var compName = AutoSerializer.GetComponentName(bit);
                    if (compName == null) continue;

                    entityNode.Add(compName, fieldObj);
                }

                entitiesNode.Add(guidStr, entityNode);
            }

            // ── Assemble root DOM ────────────────────────────────────────────────
            var root = new JsonObject { ["Entities"] = entitiesNode };
            if (header.TkbName != null)
                root["Header"] = new JsonObject { ["TkbName"] = JsonValue.Create(header.TkbName) };
            JsonEnvelope.Write(root, new DocumentMeta(header.SubsystemType, CurrentSchemaVersion));
            return root;
        }

        // ── SerializeEntity (single entity, caller-supplied mask) ─────────────────

        /// <summary>
        /// Serializes a single entity's components into a <see cref="JsonObject"/> using
        /// the provided <paramref name="componentMask"/> to select which components to
        /// include.  Custom translators run first; <see cref="FdpAutoSerializer"/> handles
        /// the remaining bits.
        /// </summary>
        /// <remarks>
        /// Use this for clipboard / diagnostic dumps.  Pass
        /// <c>repo.GetSnapshotableMask()</c> to include <c>NoSave</c> execution-state
        /// components (e.g. <see cref="Fdp.Toolkit.Behavior.Components.BrainBlackboard"/>),
        /// or <c>repo.GetSaveableMask()</c> to limit output to persistable components.
        /// </remarks>
        public JsonObject SerializeEntity(
            EntityRepository repo,
            Entity entity,
            IGuidResolver resolver,
            BitMask512 componentMask)
        {
            var entityNode    = new JsonObject();
            var remainingMask = componentMask; // mutable copy

            // Run custom translators first.
            foreach (var translator in _translators)
            {
                if (!translator.CanTranslate(repo, entity)) continue;

                // STRICT ARCHITECTURE BOUNDARY: Enforce the requested component mask.
                // If this translator consumes none of the requested components, skip it.
                // Exception: translators with an empty consumed mask are "header-native" —
                // they read from entity-header data (not ECS components) and must always run
                // (they self-gate via CanTranslate above).  Only apply the strict mask gate
                // to translators that actually declare component consumption.
                var consumed = translator.GetConsumedComponentsMask();
                if (!consumed.IsEmpty())
                {
                    var intersection = consumed;
                    intersection.BitwiseAnd(componentMask);
                    if (intersection.IsEmpty()) continue;
                }

                var entries = translator.Extract(repo, entity, resolver);
                foreach (var kv in entries)
                {
                    var rawValue = kv.Value;
                    JsonNode? node = rawValue switch
                    {
                        JsonNode jn => jn,
                        string  s  => JsonValue.Create(s),
                        int     i  => JsonValue.Create(i),
                        float   f  => JsonValue.Create(f),
                        double  d  => JsonValue.Create(d),
                        bool    b  => JsonValue.Create(b),
                        null       => throw new InvalidOperationException(
                            $"[ScenarioSerializer] Translator '{translator.GetType().Name}' returned null for key '{kv.Key}'."),
                        _          => throw new InvalidOperationException(
                            $"[ScenarioSerializer] Translator '{translator.GetType().Name}' returned unsupported payload type " +
                            $"'{rawValue.GetType().Name}' for key '{kv.Key}'.")
                    };
                    entityNode.Add(kv.Key, node);
                }

                ClearConsumed(ref remainingMask, translator.GetConsumedComponentsMask());
            }

            // Auto-serializer handles all remaining bits.
            for (int bit = 0; bit < FdpConfig.MAX_COMPONENT_TYPES; bit++)
            {
                if (!remainingMask.IsSet(bit)) continue;

                var fieldObj = AutoSerializer.TryExtract(repo, entity, bit, resolver);
                if (fieldObj == null) continue;

                var compName = AutoSerializer.GetComponentName(bit);
                if (compName == null) continue;

                entityNode.Add(compName, fieldObj);
            }

            return entityNode;
        }

        // ── Deserialize ──────────────────────────────────────────────────────────

        /// <summary>
        /// Deserializes entities from <paramref name="dom"/> into <paramref name="repo"/>.
        /// </summary>
        /// <param name="repo">Target repository.</param>
        /// <param name="dom">Scenario DOM previously produced by <see cref="Serialize"/>.</param>
        /// <param name="asEpisode">
        /// When <see langword="true"/>, stamps <see cref="Fdp.Core.EpisodeTag"/> on every created
        /// entity.  <paramref name="episodeId"/> must be a non-null, non-empty <see cref="Guid"/>;
        /// passing <see langword="null"/> or <see cref="Guid.Empty"/> throws
        /// <see cref="InvalidOperationException"/>.
        /// </param>
        /// <param name="episodeId">Episode identifier written to <see cref="Fdp.Core.EpisodeTag.EpisodeId"/>.</param>
        /// <remarks>
        /// Returns immediately (without creating any entities) if
        /// <c>Header.SubsystemType</c> does not match the type this serializer was
        /// configured with.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the DOM is structurally invalid (missing or wrong-typed <c>Entities</c>
        /// node, invalid GUID keys, or unrecognised component type names).  Also thrown when
        /// <paramref name="asEpisode"/> is <see langword="true"/> but <paramref name="episodeId"/> is
        /// <see langword="null"/> or <see cref="Guid.Empty"/>.
        /// </exception>
        public void Deserialize(
            EntityRepository repo,
            JsonObject dom,
            bool asEpisode     = false,
            Guid? episodeId    = null)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            if (dom  == null) throw new ArgumentNullException(nameof(dom));

            // Validate episode arguments before touching the DOM.
            if (asEpisode && (episodeId == null || episodeId == Guid.Empty))
                throw new InvalidOperationException(
                    "[ScenarioSerializer] Deserialize called with asEpisode=true but episodeId is null or Guid.Empty. " +
                    "A non-empty Guid is required to stamp Fdp.Core.EpisodeTag on loaded entities.");

            // Peek header for subsystem-type filter.
            // Phase 2 format: $meta.docType carries the subsystem type.
            // Legacy format:  Header.SubsystemType (Pascal or camelCase).
            if (JsonEnvelope.HasEnvelope(dom))
            {
                var meta = JsonEnvelope.Read(dom);
                if (!string.Equals(meta.DocType, _subsystemType, StringComparison.Ordinal))
                    return; // Graceful subsystem mismatch — no entities created.
            }
            else
            {
                var headerNode = (dom["Header"] ?? dom["header"]) as JsonObject;
                var savedType  = headerNode?["SubsystemType"]?.GetValue<string>()
                              ?? headerNode?["subsystemType"]?.GetValue<string>();
                if (!string.Equals(savedType, _subsystemType, StringComparison.Ordinal))
                    return; // Graceful subsystem mismatch — no entities created.
            }

            var entitiesNode = (dom["Entities"] ?? dom["entities"]) as JsonObject;
            if (entitiesNode == null)
                throw new InvalidOperationException(
                    "[ScenarioSerializer] Deserialize: scenario DOM is missing or has a non-object 'Entities' node. " +
                    "The file may be corrupt or written by an incompatible version.");
            if (entitiesNode.GetType() != typeof(JsonObject))
                throw new InvalidOperationException(
                    "[ScenarioSerializer] Deserialize: 'Entities' node is not a JsonObject. " +
                    "The file may be corrupt or written by an incompatible version.");

            // ── Pass 1: create entities, build load-side IGuidResolver ───────────
            var guidToEntity = new Dictionary<Guid, Entity>(entitiesNode.Count);

            foreach (var kvp in entitiesNode)
            {
                if (!Guid.TryParse(kvp.Key, out var guid))
                    throw new InvalidOperationException(
                        $"[ScenarioSerializer] Deserialize: entity key '{kvp.Key}' is not a valid Guid. " +
                        "The scenario file is invalid or corrupt.");
                var entity = repo.CreateEntity();
                guidToEntity[guid] = entity;
            }

            IGuidResolver loadResolver = new LoadResolver(guidToEntity);

            // ── Pass 2: inject components ────────────────────────────────────────
            foreach (var kvp in entitiesNode)
            {
                if (!Guid.TryParse(kvp.Key, out var guid))
                    throw new InvalidOperationException(
                        $"[ScenarioSerializer] Deserialize (pass 2): entity key '{kvp.Key}' is not a valid Guid.");
                if (!guidToEntity.TryGetValue(guid, out var entity)) continue;

                var entityNode = kvp.Value as JsonObject;
                if (entityNode == null) continue;

                // Build a set of component names handled by custom translators.
                var translatorHandled = new HashSet<string>(StringComparer.Ordinal);

                // Build a full data map from all entity node entries so N:M translators
                // can use their own custom DOM keys (not just consumed-component-type names).
                var scenarioData = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var kv in entityNode)
                {
                    if (kv.Value != null) scenarioData[kv.Key] = kv.Value;
                }

                foreach (var translator in _translators)
                {
                    // Each translator self-filters: its Inject method checks for its own keys.
                    translator.Inject(repo, entity, scenarioData, loadResolver);

                    // Mark the consumed *component type* names so the auto-serializer
                    // does not try to re-process them as plain auto-serialize entries.
                    foreach (var name in BuildConsumedNames(translator.GetConsumedComponentsMask()))
                        translatorHandled.Add(name);

                    // Also mark the custom DOM keys the translator produces in Extract
                    // (e.g. "OrdnanceDef") so the fail-fast unknown-key check skips them.
                    foreach (var key in translator.GetOutputDomKeys())
                        translatorHandled.Add(key);
                }

                // Auto-serializer handles the rest.
                foreach (var compKvp in entityNode)
                {
                    if (translatorHandled.Contains(compKvp.Key)) continue;

                    // Find type ID by component name.
                    int typeId = FindTypeIdByName(compKvp.Key);
                    if (typeId < 0)
                        throw new InvalidOperationException(
                            $"[ScenarioSerializer] Deserialize: unknown component type name '{compKvp.Key}'. " +
                            "The scenario file references a component that is not registered in the current " +
                            "ComponentTypeRegistry. This may indicate a file version skew or a typo.");

                    AutoSerializer.TryInject(repo, entity, typeId, compKvp.Value, loadResolver);
                }

                // Stamp Fdp.Core.EpisodeTag if requested.
                if (asEpisode)
                {
                    repo.SetComponent(entity, new Fdp.Core.EpisodeTag { EpisodeId = episodeId!.Value });
                }
            }
        }

        // ── DeserializeWith ──────────────────────────────────────────────────────

        /// <summary>
        /// Deserializes components from <paramref name="dom"/> into <paramref name="repo"/>
        /// using caller-supplied entity mappings and a caller-supplied GUID resolver.
        /// Unlike <see cref="Deserialize(EntityRepository,JsonObject,bool,Guid?)"/>, this overload:
        /// <list type="bullet">
        ///   <item>Skips the subsystem-type header check (the DOM may carry mixed-subsystem data).</item>
        ///   <item>Skips entity creation (pass 1). All entity keys in the DOM must be present in
        ///     <paramref name="preAllocated"/>; a missing key throws.</item>
        ///   <item>Passes <paramref name="loadResolver"/> into every translator and auto-serializer
        ///     call so callers control cross-entity reference resolution.</item>
        /// </list>
        /// </summary>
        /// <param name="repo">Target repository.</param>
        /// <param name="dom">Scenario DOM containing an <c>Entities</c> node.</param>
        /// <param name="loadResolver">Resolver forwarded to translators and auto-serializer for
        ///   Entity-typed fields during injection.</param>
        /// <param name="preAllocated">Maps DOM entity-key strings to pre-created Entity handles.</param>
        /// <exception cref="ArgumentNullException">When any argument is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the DOM is missing the <c>Entities</c> node, an entity key is absent from
        /// <paramref name="preAllocated"/>, or an unknown component type name is encountered.
        /// </exception>
        public void DeserializeWith(
            EntityRepository repo,
            JsonObject dom,
            IGuidResolver loadResolver,
            Dictionary<string, Entity> preAllocated)
        {
            if (repo         == null) throw new ArgumentNullException(nameof(repo));
            if (dom          == null) throw new ArgumentNullException(nameof(dom));
            if (loadResolver == null) throw new ArgumentNullException(nameof(loadResolver));
            if (preAllocated == null) throw new ArgumentNullException(nameof(preAllocated));

            var entitiesNode = (dom["Entities"] ?? dom["entities"]) as JsonObject;
            if (entitiesNode == null)
                throw new InvalidOperationException(
                    "[ScenarioSerializer] DeserializeWith: scenario DOM is missing or has a non-object 'Entities' node.");

            // No pass-1: entities must already exist in preAllocated.
            // ── Pass 2: inject components using the caller-supplied resolver ────
            foreach (var kvp in entitiesNode)
            {
                if (!preAllocated.TryGetValue(kvp.Key, out var entity))
                    continue; // Entity not in preAllocated: caller did not request it, skip.

                var entityNode = kvp.Value as JsonObject;
                if (entityNode == null) continue;

                // Build a set of component names handled by custom translators.
                var translatorHandled = new HashSet<string>(StringComparer.Ordinal);

                // Build a full data map so N:M translators can use their own custom DOM keys.
                var scenarioData = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var kv in entityNode)
                {
                    if (kv.Value != null) scenarioData[kv.Key] = kv.Value;
                }

                foreach (var translator in _translators)
                {
                    translator.Inject(repo, entity, scenarioData, loadResolver);

                    foreach (var name in BuildConsumedNames(translator.GetConsumedComponentsMask()))
                        translatorHandled.Add(name);

                    foreach (var key in translator.GetOutputDomKeys())
                        translatorHandled.Add(key);
                }

                // Auto-serializer handles the rest, forwarding the caller-supplied resolver.
                foreach (var compKvp in entityNode)
                {
                    if (translatorHandled.Contains(compKvp.Key)) continue;

                    int typeId = FindTypeIdByName(compKvp.Key);
                    if (typeId < 0)
                        throw new InvalidOperationException(
                            $"[ScenarioSerializer] DeserializeWith: unknown component type name '{compKvp.Key}'. " +
                            "The scenario file references a component that is not registered in the current " +
                            "ComponentTypeRegistry. This may indicate a file version skew or a typo.");

                    AutoSerializer.TryInject(repo, entity, typeId, compKvp.Value, loadResolver);
                }
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>Collects all active entities that do NOT carry <see cref="ScenarioIgnoreTag"/>.</summary>
        private static List<Entity> CollectSaveableEntities(EntityRepository repo)
        {
            int ignoreTagId = ComponentTypeRegistry.GetId(typeof(ScenarioIgnoreTag));
            var result = new List<Entity>(repo.EntityCount);

            for (int i = 0; i <= repo.MaxEntityIndex; i++)
            {
                var entity = new Entity(i, repo.GetMetadata(i).Generation);
                if (!repo.IsAlive(entity)) continue;

                // Skip entities tagged ScenarioIgnoreTag.
                if (ignoreTagId >= 0 && repo.GetComponentMask(i).IsSet(ignoreTagId))
                    continue;

                result.Add(entity);
            }

            return result;
        }

        private static void ClearConsumed(ref BitMask512 remaining, BitMask512 consumed)
        {
            for (int bit = 0; bit < FdpConfig.MAX_COMPONENT_TYPES; bit++)
            {
                if (consumed.IsSet(bit))
                    remaining.ClearBit(bit);
            }
        }

        private static int FindTypeIdByName(string componentName)
        {
            foreach (int id in ComponentTypeRegistry.GetAllTypeIds())
            {
                var t = ComponentTypeRegistry.GetType(id);
                if (t?.Name == componentName) return id;
            }
            return -1;
        }

        private static List<string> BuildConsumedNames(BitMask512 consumedMask)
        {
            var names = new List<string>();
            for (int bit = 0; bit < FdpConfig.MAX_COMPONENT_TYPES; bit++)
            {
                if (!consumedMask.IsSet(bit)) continue;
                var t = ComponentTypeRegistry.GetType(bit);
                if (t != null) names.Add(t.Name);
            }
            return names;
        }

        // ── GuidResolver implementations ─────────────────────────────────────────

        private sealed class SaveResolver : IGuidResolver
        {
            private readonly Dictionary<Entity, Guid> _entityToGuid;

            public SaveResolver(Dictionary<Entity, Guid> entityToGuid)
                => _entityToGuid = entityToGuid;

            public string Resolve(Entity entity)
            {
                if (_entityToGuid.TryGetValue(entity, out var guid))
                    return guid.ToString();
                throw new InvalidOperationException(
                    $"[ScenarioSerializer] SaveResolver: entity {entity} is not in the save map. " +
                    "This is a programmer error — ensure all cross-referenced entities are included " +
                    "in the saveable entity set (not tagged with ScenarioIgnoreTag or DataPolicy.NoSave).");
            }

            public Entity Resolve(string guidStr)
                => throw new InvalidOperationException(
                    "SaveResolver.Resolve(string) must not be called during save.");
        }

        private sealed class LoadResolver : IGuidResolver
        {
            private readonly Dictionary<Guid, Entity> _guidToEntity;

            public LoadResolver(Dictionary<Guid, Entity> guidToEntity)
                => _guidToEntity = guidToEntity;

            public string Resolve(Entity entity)
                => throw new InvalidOperationException(
                    "LoadResolver.Resolve(Entity) must not be called during load.");

            public Entity Resolve(string guidStr)
            {
                if (!Guid.TryParse(guidStr, out var guid))
                    throw new InvalidOperationException(
                        $"[ScenarioSerializer] LoadResolver: reference GUID string '{guidStr}' is not a valid Guid. " +
                        "The scenario file is corrupt.");
                if (!_guidToEntity.TryGetValue(guid, out var entity))
                    throw new InvalidOperationException(
                        $"[ScenarioSerializer] LoadResolver: GUID {guid} does not resolve to any loaded entity. " +
                        "This may indicate a forward reference or a corrupt scenario file.");
                return entity;
            }
        }
    }
}
