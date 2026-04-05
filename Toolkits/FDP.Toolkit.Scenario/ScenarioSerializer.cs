using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Kernel;

namespace FDP.Toolkit.Scenario
{
    /// <summary>
    /// Serializes and deserializes an <see cref="EntityRepository"/> to/from an
    /// in-memory <see cref="JsonObject"/> DOM using the following schema:
    /// <code>
    /// {
    ///   "Header": { "SubsystemType": "...", "SchemaVersion": 1 },
    ///   "Entities": {
    ///     "&lt;guid&gt;": { "ComponentName": { "field": "..." } }
    ///   }
    /// }
    /// </code>
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
    /// components.  Optionally stamps <see cref="Fdp.Kernel.EpisodeTag"/> when <c>asEpisode == true</c>.
    /// </para>
    /// </remarks>
    public sealed class ScenarioSerializer
    {
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

                // Per-entity saveable mask = global saveable ∩ entity's own components.
                var entityComponents = repo.GetHeader(entity.Index).ComponentMask;
                entityComponents.BitwiseAnd(globalSaveable);
                var remainingMask = entityComponents; // mutable copy

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
                for (int bit = 0; bit < 256; bit++)
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
            var headerNode = new JsonObject
            {
                ["SubsystemType"]  = JsonValue.Create(header.SubsystemType),
                ["SchemaVersion"]  = JsonValue.Create(header.SchemaVersion),
            };

            return new JsonObject
            {
                ["Header"]   = headerNode,
                ["Entities"] = entitiesNode,
            };
        }

        // ── Deserialize ──────────────────────────────────────────────────────────

        /// <summary>
        /// Deserializes entities from <paramref name="dom"/> into <paramref name="repo"/>.
        /// </summary>
        /// <param name="repo">Target repository.</param>
        /// <param name="dom">Scenario DOM previously produced by <see cref="Serialize"/>.</param>
        /// <param name="asEpisode">
        /// When <see langword="true"/>, stamps <see cref="Fdp.Kernel.EpisodeTag"/> on every created
        /// entity.  <paramref name="episodeId"/> must be a non-null, non-empty <see cref="Guid"/>;
        /// passing <see langword="null"/> or <see cref="Guid.Empty"/> throws
        /// <see cref="InvalidOperationException"/>.
        /// </param>
        /// <param name="episodeId">Episode identifier written to <see cref="Fdp.Kernel.EpisodeTag.EpisodeId"/>.</param>
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
                    "A non-empty Guid is required to stamp Fdp.Kernel.EpisodeTag on loaded entities.");

            // Peek header for subsystem-type filter.
            // Support both Pascal case ("Header"/"SubsystemType" from ScenarioSerializer.Serialize)
            // and camelCase ("header"/"subsystemType" from HrotSerializerOptions.HrotJsonOptions).
            var headerNode = (dom["Header"] ?? dom["header"]) as JsonObject;
            var savedType  = headerNode?["SubsystemType"]?.GetValue<string>()
                          ?? headerNode?["subsystemType"]?.GetValue<string>();
            if (!string.Equals(savedType, _subsystemType, StringComparison.Ordinal))
                return; // Graceful subsystem mismatch — no entities created.

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

                // Stamp Fdp.Kernel.EpisodeTag if requested.
                if (asEpisode)
                {
                    repo.SetComponent(entity, new Fdp.Kernel.EpisodeTag { EpisodeId = episodeId!.Value });
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
                var entity = new Entity(i, repo.GetHeader(i).Generation);
                if (!repo.IsAlive(entity)) continue;

                // Skip entities tagged ScenarioIgnoreTag.
                if (ignoreTagId >= 0 && repo.GetHeader(i).ComponentMask.IsSet(ignoreTagId))
                    continue;

                result.Add(entity);
            }

            return result;
        }

        private static void ClearConsumed(ref BitMask256 remaining, BitMask256 consumed)
        {
            for (int bit = 0; bit < 256; bit++)
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

        private static List<string> BuildConsumedNames(BitMask256 consumedMask)
        {
            var names = new List<string>();
            for (int bit = 0; bit < 256; bit++)
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
