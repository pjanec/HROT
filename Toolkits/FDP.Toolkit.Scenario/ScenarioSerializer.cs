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
    /// components.  Optionally stamps <see cref="StoryTag"/> when <c>asStory == true</c>.
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
                            _          => JsonValue.Create(rawValue?.ToString())
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
        /// <param name="asStory">
        /// When <see langword="true"/>, stamps <see cref="StoryTag"/> on every created
        /// entity.
        /// </param>
        /// <param name="storyId">Story identifier written to <see cref="StoryTag.StoryId"/>.</param>
        /// <remarks>
        /// Returns immediately (without creating any entities) if
        /// <c>Header.SubsystemType</c> does not match the type this serializer was
        /// configured with.
        /// </remarks>
        public void Deserialize(
            EntityRepository repo,
            JsonObject dom,
            bool asStory      = false,
            string? storyId   = null)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            if (dom  == null) throw new ArgumentNullException(nameof(dom));

            // Peek header for subsystem-type filter.
            var headerNode = dom["Header"] as JsonObject;
            var savedType  = headerNode?["SubsystemType"]?.GetValue<string>();
            if (!string.Equals(savedType, _subsystemType, StringComparison.Ordinal))
                return; // Graceful subsystem mismatch — no entities created.

            var entitiesNode = dom["Entities"] as JsonObject;
            if (entitiesNode == null) return;

            // ── Pass 1: create entities, build load-side IGuidResolver ───────────
            var guidToEntity = new Dictionary<Guid, Entity>(entitiesNode.Count);

            foreach (var kvp in entitiesNode)
            {
                if (!Guid.TryParse(kvp.Key, out var guid)) continue;
                var entity = repo.CreateEntity();
                guidToEntity[guid] = entity;
            }

            IGuidResolver loadResolver = new LoadResolver(guidToEntity);

            // ── Pass 2: inject components ────────────────────────────────────────
            foreach (var kvp in entitiesNode)
            {
                if (!Guid.TryParse(kvp.Key, out var guid)) continue;
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
                }

                // Auto-serializer handles the rest.
                foreach (var compKvp in entityNode)
                {
                    if (translatorHandled.Contains(compKvp.Key)) continue;

                    // Find type ID by component name.
                    int typeId = FindTypeIdByName(compKvp.Key);
                    if (typeId < 0) continue;

                    AutoSerializer.TryInject(repo, entity, typeId, compKvp.Value, loadResolver);
                }

                // Stamp StoryTag if requested.
                if (asStory)
                {
                    repo.SetComponent(entity, new StoryTag { StoryId = storyId });
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
                // Entity not in the save set (may be destroyed or ignored) — use a zero GUID.
                return Guid.Empty.ToString();
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
                if (Guid.TryParse(guidStr, out var guid) &&
                    _guidToEntity.TryGetValue(guid, out var entity))
                    return entity;
                return default; // Entity.Null
            }
        }
    }
}
