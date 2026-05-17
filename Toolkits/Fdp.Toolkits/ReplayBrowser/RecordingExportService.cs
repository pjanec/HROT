using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Core.Serialization;
using Fdp.Toolkit.Diagnostics;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Serialization;
using ToolkitAutoSerializer = Fdp.Toolkit.Scenario.FdpAutoSerializer;

namespace Fdp.Toolkit.ReplayBrowser
{
    /// <summary>
    /// Streams an .fdp recording to a JSON file without buffering all frames in memory.
    /// Uses Utf8JsonWriter directly to a FileStream to keep heap allocations bounded.
    /// </summary>
    public sealed class RecordingExportService : IRecordingExportService
    {
        private readonly ScenarioSerializer? _serializer;
        private readonly Diff.IComponentDiffService _diffService;

        public RecordingExportService(
            ScenarioSerializer? serializer = null,
            Diff.IComponentDiffService? diffService = null)
        {
            _serializer = serializer;
            _diffService = diffService ?? new Diff.ComponentDiffService();
        }

        /// <inheritdoc/>
        public void ExportToJson(string inputFdpPath, string outputJsonPath, JsonExportOptions options)
        {
            // Dispatch to mutation exporters when requested
            if (options.FormatMode == ExportFormatMode.Changelog
                || options.FormatMode == ExportFormatMode.Incremental)
            {
                ExportChangelogToJson(inputFdpPath, outputJsonPath, options);
                return;
            }

            using var sandboxRepo = new EntityRepository();
            var sandboxBus = new FdpEventBus();

            // Register all known component types into the sandbox repo so PlaybackSystem
            // can apply frames without throwing on unknown type IDs.
            AutoRegisterAllComponentTypes(sandboxRepo);

            // Pre-register all known native event types in the sandbox bus so that events
            // injected by PlaybackController land in typed streams (IEventStreamInspector)
            // rather than UntypedNativeEventStreams which are invisible to GetDebugInspectors.
            AutoRegisterAllEventTypes(sandboxBus);

            // Build the auto-serializer after types are registered into the registry.
            ToolkitAutoSerializer autoSerializer;
            if (_serializer != null)
            {
                autoSerializer = _serializer.AutoSerializer;
            }
            else
            {
                autoSerializer = new ToolkitAutoSerializer();
                autoSerializer.Build();
            }

            var guidResolver = new DiagnosticGuidResolver();

            // Build serialization options for event payloads: copy converters from the
            // registry singleton and prepend the EntityJsonConverter so that Entity struct
            // fields inside events are formatted as "[Index, vGeneration]" strings.
            var eventSerializerOpts = BuildEventSerializerOptions();

            using var playback = new PlaybackController(inputFdpPath);
            playback.EventBus = sandboxBus;

            // --- Windowing: pre-seek if needed ---
            long firstFrameWallTicks = 0L;
            bool firstFrameWallTicksKnown = false;
            long targetEndTicks = long.MaxValue;

            if (options.WindowMode == ExportWindowMode.ByFrame && options.StartFrame > 0)
            {
                sandboxBus.ClearCurrentBuffers();
                // Seek to StartFrame-1 so the first StepForward() yields StartFrame.
                playback.SeekToFrame(sandboxRepo, options.StartFrame - 1);
            }
            else if (options.WindowMode == ExportWindowMode.ByTime)
            {
                firstFrameWallTicks = playback.GetFrameMetadata(0).WallClockTicks;
                firstFrameWallTicksKnown = true;
                if (options.StartTimeSec > 0f)
                {
                    long targetStart = firstFrameWallTicks + (long)(options.StartTimeSec * TimeSpan.TicksPerSecond);
                    sandboxBus.ClearCurrentBuffers();
                    playback.SeekToWallClockTicks(sandboxRepo, targetStart);
                }
                if (!float.IsPositiveInfinity(options.EndTimeSec))
                    targetEndTicks = firstFrameWallTicks + (long)(options.EndTimeSec * TimeSpan.TicksPerSecond);
            }

            // --- Open output file and stream JSON ---
            using var fileStream = new FileStream(outputJsonPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536);
            var writerOptions = new JsonWriterOptions { Indented = !options.Minified };
            using var writer = new Utf8JsonWriter(fileStream, writerOptions);

            writer.WriteStartObject();

            // Header block
            writer.WriteStartObject("Header");
            writer.WriteString("Magic", "FDPREC");
            writer.WriteNumber("FormatVersion", playback.FormatVersion);
            writer.WriteNumber("Timestamp", playback.RecordingTimestamp);
            writer.WriteEndObject();

            writer.WriteStartArray("Frames");

            int fileFrameOrdinal = 0;

            while (playback.StepForward(sandboxRepo))
            {
                int currentFrame = playback.CurrentFrame;
                var meta = playback.GetFrameMetadata(currentFrame);

                // End-window check
                if (options.WindowMode == ExportWindowMode.ByFrame && currentFrame > options.EndFrame)
                    break;
                if (options.WindowMode == ExportWindowMode.ByTime && meta.WallClockTicks > targetEndTicks)
                    break;

                // Capture first-frame wall ticks for FullFile and ByFrame modes
                if (!firstFrameWallTicksKnown)
                {
                    firstFrameWallTicks = meta.WallClockTicks;
                    firstFrameWallTicksKnown = true;
                }

                double relativeWallTimeSec = (meta.WallClockTicks - firstFrameWallTicks) / (double)TimeSpan.TicksPerSecond;

                double simTimeSec = 0.0;
                long simFrameNumber = 0;
                if (sandboxRepo.HasSingletonUnmanaged<GlobalTime>())
                {
                    var gt = sandboxRepo.GetSingletonUnmanaged<GlobalTime>();
                    simTimeSec = gt.TotalTime;
                    simFrameNumber = gt.FrameNumber;
                }

                writer.WriteStartObject();

                // FrameHeader
                writer.WriteStartObject("FrameHeader");
                writer.WriteNumber("FileFrameOrdinal", fileFrameOrdinal++);
                writer.WriteNumber("SimFrameNumber", simFrameNumber);
                writer.WriteNumber("Tick", meta.Tick);
                writer.WriteString("FrameType", meta.FrameType == FrameType.Keyframe ? "Keyframe" : "Delta");
                writer.WriteNumber("WallClockTicks", meta.WallClockTicks);
                writer.WriteNumber("RelativeWallTimeSec", relativeWallTimeSec);
                writer.WriteNumber("SimTimeSec", simTimeSec);
                writer.WriteNumber("CompressedSize", meta.CompressedSize);
                writer.WriteNumber("UncompressedSize", meta.UncompressedSize);
                writer.WriteEndObject();

                // DestroyedEntities (entities destroyed this delta tick)
                if (options.IncludeEntities)
                {
                    var destroyed = sandboxRepo.GetDestructionLog();
                    writer.WriteStartArray("DestroyedEntities");
                    if (meta.FrameType == FrameType.Delta)
                    {
                        foreach (var e in destroyed)
                        {
                            if (!EntityPassesFilter(e.Index, e, options)) continue;
                            writer.WriteStringValue(guidResolver.Resolve(e));
                        }
                    }
                    writer.WriteEndArray();
                    sandboxRepo.ClearDestructionLog();
                }

                // Entities block
                if (options.IncludeEntities)
                {
                    writer.WriteStartArray("Entities");
                    var query = sandboxRepo.Query().Build();
                    foreach (Entity entity in query)
                    {
                        if (!EntityPassesFilter(entity.Index, entity, options)) continue;

                        ref EntityHeader header = ref sandboxRepo.GetHeader(entity.Index);
                        writer.WriteStartObject();

                        // EntityId is an integer array [Index, Generation] (not a string)
                        writer.WriteStartArray("EntityId");
                        writer.WriteNumberValue(entity.Index);
                        writer.WriteNumberValue(entity.Generation);
                        writer.WriteEndArray();

                        // Components list
                        writer.WriteStartArray("Components");

                        // Build a translator payload map for this entity.
                        // Translators claim component bits and return named payloads (keyed by component name).
                        var translatorPayloads = new System.Collections.Generic.Dictionary<string, JsonNode?>();
                        if (_serializer != null)
                        {
                            foreach (var translator in _serializer.Translators)
                            {
                                if (!translator.CanTranslate(sandboxRepo, entity)) continue;
                                var extracted = translator.Extract(sandboxRepo, entity, guidResolver);
                                foreach (var kvp in extracted)
                                    translatorPayloads[kvp.Key] = kvp.Value as JsonNode;
                            }
                        }

                        for (int bit = 0; bit < 256; bit++)
                        {
                            if (!header.ComponentMask.IsSet(bit)) continue;

                            string? compName = autoSerializer.GetComponentName(bit);
                            if (compName == null)
                                compName = ComponentTypeRegistry.GetType(bit)?.Name;
                            if (compName == null) continue;

                            bool hasAuth = sandboxRepo.HasAuthority(entity, bit);
                            JsonNode? payload;
                            if (translatorPayloads.TryGetValue(compName, out var translatorPayload))
                                payload = translatorPayload;
                            else
                                payload = autoSerializer.TryExtract(sandboxRepo, entity, bit, guidResolver);

                            Type? compType = ComponentTypeRegistry.GetType(bit);
                            if (payload == null && compType != null && !compType.IsValueType)
                            {
                                payload = TrySerializeManagedComponentByReflection(sandboxRepo, entity, compType);
                            }

                            writer.WriteStartObject();
                            writer.WriteString("ComponentType", compName);
                            writer.WriteBoolean("HasAuthority", hasAuth);
                            writer.WritePropertyName("Payload");
                            WritePayloadNode(writer, payload, options.Minified);
                            writer.WriteEndObject();
                        }
                        writer.WriteEndArray(); // Components

                        writer.WriteEndObject(); // entity
                    }
                    writer.WriteEndArray(); // Entities
                }

                // Events block
                if (options.IncludeEvents)
                {
                    writer.WriteStartArray("Events");
                    foreach (var inspector in sandboxBus.GetDebugInspectors())
                    {
                        if (inspector.Count == 0) continue;
                        Type eventType = inspector.EventType;
                        bool isManaged = !eventType.IsValueType;
                        string eventTypeName = eventType.Name;

                        foreach (object evt in inspector.InspectReadBuffer())
                        {
                            JsonNode? payloadNode = JsonSerializer.SerializeToNode(evt, eventType, eventSerializerOpts);

                            writer.WriteStartObject();
                            writer.WriteString("EventType", eventTypeName);
                            writer.WriteBoolean("IsManaged", isManaged);
                            writer.WritePropertyName("Payload");
                            WritePayloadNode(writer, payloadNode, options.Minified);
                            writer.WriteEndObject();
                        }
                    }
                    writer.WriteEndArray(); // Events
                }

                writer.WriteEndObject(); // frame object

                // Flush periodically to keep memory usage bounded on large recordings.
                writer.Flush();
            }

            writer.WriteEndArray(); // Frames
            writer.WriteEndObject(); // root
            writer.Flush();
        }

        // ── Changelog export ─────────────────────────────────────────────────────────

        /// <summary>
        /// Exports a changelog-mode JSON (root array of ChangelogEntryDto) for the target entities.
        /// </summary>
        private void ExportChangelogToJson(string inputFdpPath, string outputJsonPath, JsonExportOptions options)
        {
            using var sandboxRepo = new EntityRepository();
            var sandboxBus = new FdpEventBus();

            AutoRegisterAllComponentTypes(sandboxRepo);
            AutoRegisterAllEventTypes(sandboxBus);

            ToolkitAutoSerializer autoSerializer;
            if (_serializer != null)
            {
                autoSerializer = _serializer.AutoSerializer;
            }
            else
            {
                autoSerializer = new ToolkitAutoSerializer();
                autoSerializer.Build();
            }

            var guidResolver = new DiagnosticGuidResolver();

            using var playback = new PlaybackController(inputFdpPath);
            playback.EventBus = sandboxBus;

            // Windowing pre-seek
            long firstFrameWallTicks = 0L;
            bool firstFrameWallTicksKnown = false;
            long targetEndTicks = long.MaxValue;

            if (options.WindowMode == ExportWindowMode.ByFrame && options.StartFrame > 0)
            {
                sandboxBus.ClearCurrentBuffers();
                playback.SeekToFrame(sandboxRepo, options.StartFrame - 1);
            }
            else if (options.WindowMode == ExportWindowMode.ByTime)
            {
                firstFrameWallTicks = playback.GetFrameMetadata(0).WallClockTicks;
                firstFrameWallTicksKnown = true;
                if (options.StartTimeSec > 0f)
                {
                    long targetStart = firstFrameWallTicks + (long)(options.StartTimeSec * TimeSpan.TicksPerSecond);
                    sandboxBus.ClearCurrentBuffers();
                    playback.SeekToWallClockTicks(sandboxRepo, targetStart);
                }
                if (!float.IsPositiveInfinity(options.EndTimeSec))
                    targetEndTicks = firstFrameWallTicks + (long)(options.EndTimeSec * TimeSpan.TicksPerSecond);
            }

            // Per-entity state baselines: null means "not yet seen / entity destroyed"
            var baselines = new System.Collections.Generic.Dictionary<Entity, System.Text.Json.Nodes.JsonNode?>();
            if (options.FilterBySelection)
            {
                foreach (Entity target in options.TargetEntities)
                    baselines[target] = null;
            }

            var changelogSerializerOpts = options.FormatMode == ExportFormatMode.Incremental
                ? BuildIncrementalSerializerOptions(options.Minified)
                : BuildChangelogSerializerOptions(options.Minified);

            using var fileStream = new FileStream(outputJsonPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536);
            var writerOptions = new JsonWriterOptions { Indented = !options.Minified };
            using var writer = new Utf8JsonWriter(fileStream, writerOptions);

            // Root is an array (not an object with "Frames")
            writer.WriteStartArray();

            // If no target entities in selection mode, just write an empty array
            if (options.FilterBySelection && options.TargetEntities.Count == 0)
            {
                writer.WriteEndArray();
                writer.Flush();
                return;
            }

            while (playback.StepForward(sandboxRepo))
            {
                int currentFrame = playback.CurrentFrame;
                var meta = playback.GetFrameMetadata(currentFrame);

                // End-window check
                if (options.WindowMode == ExportWindowMode.ByFrame && currentFrame > options.EndFrame)
                    break;
                if (options.WindowMode == ExportWindowMode.ByTime && meta.WallClockTicks > targetEndTicks)
                    break;

                // Capture first-frame wall ticks
                if (!firstFrameWallTicksKnown)
                {
                    firstFrameWallTicks = meta.WallClockTicks;
                    firstFrameWallTicksKnown = true;
                }

                double relativeWallTimeSec = (meta.WallClockTicks - firstFrameWallTicks) / (double)TimeSpan.TicksPerSecond;

                double simTimeSec = 0.0;
                if (sandboxRepo.HasSingletonUnmanaged<GlobalTime>())
                    simTimeSec = sandboxRepo.GetSingletonUnmanaged<GlobalTime>().TotalTime;

                var currentTargets = new System.Collections.Generic.HashSet<Entity>();
                if (options.FilterBySelection)
                {
                    foreach (Entity e in options.TargetEntities)
                        currentTargets.Add(e);
                }
                else
                {
                    var liveQuery = sandboxRepo.Query().Build();
                    foreach (Entity e in liveQuery)
                        currentTargets.Add(e);
                    foreach (Entity e in sandboxRepo.GetDestructionLog())
                        currentTargets.Add(e);
                }

                foreach (Entity target in currentTargets)
                {
                    bool isAlive = sandboxRepo.IsAlive(target);
                    System.Text.Json.Nodes.JsonObject? current = null;
                    if (isAlive)
                    {
                        // Build a flat JsonObject keyed by component name for this entity
                        current = BuildEntityStateNode(
                            sandboxRepo, target, autoSerializer, guidResolver, _serializer?.Translators);
                    }

                    if (!baselines.TryGetValue(target, out var baseline))
                    {
                        baseline = null;
                    }

                    if (baseline == null && current == null)
                        continue;

                    System.Collections.Generic.IReadOnlyList<Diff.DiffNode> diffs =
                        _diffService.ComputeTreeDiff(baseline, current, options.EpsilonTolerance);
                    var prunedDiffs = PruneUnchangedNodes(diffs);
                    if (prunedDiffs.Count > 0)
                    {
                        var entry = new ChangelogEntryDto(
                            FrameIndex: currentFrame,
                            WallClockTicks: meta.WallClockTicks,
                            RelativeWallTimeSec: relativeWallTimeSec,
                            SimTimeSec: simTimeSec,
                            EntityHandle: guidResolver.Resolve(target),
                            Mutations: prunedDiffs);

                        JsonSerializer.Serialize(writer, entry, changelogSerializerOpts);
                    }

                    baselines[target] = current;
                }

                sandboxRepo.ClearDestructionLog();
                writer.Flush();
            }

            writer.WriteEndArray();
            writer.Flush();
        }

        /// <summary>
        /// Builds a flat <see cref="System.Text.Json.Nodes.JsonObject"/> keyed by component name
        /// representing the current state of an entity's components.
        /// </summary>
        // Cached options for the reflection-based component serialization fallback.
        private static readonly JsonSerializerOptions _reflectionFallbackOpts = new JsonSerializerOptions
        {
            IncludeFields = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        // Cached TryGetComponent<T>(Entity, out T) generic method definition.
        private static readonly System.Reflection.MethodInfo? _tryGetComponentGenericDef =
            FindTryGetComponentMethod();
        private static readonly System.Reflection.MethodInfo? _hasManagedComponentGeneric =
            typeof(EntityRepository).GetMethod("HasManagedComponent");
        private static readonly System.Reflection.MethodInfo? _getManagedComponentGeneric =
            typeof(EntityRepository).GetMethod("GetManagedComponentRO")
            ?? typeof(EntityRepository).GetMethod("GetManagedComponent");

        private static System.Reflection.MethodInfo? FindTryGetComponentMethod()
        {
            foreach (var m in typeof(EntityRepository)
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (m.Name != "TryGetComponent") continue;
                if (!m.IsGenericMethodDefinition) continue;
                var parms = m.GetParameters();
                // Signature: bool TryGetComponent<T>(Entity entity, out T component)
                if (parms.Length == 2 &&
                    parms[0].ParameterType == typeof(Entity) &&
                    parms[1].IsOut)
                    return m;
            }
            return null;
        }

        private static System.Text.Json.Nodes.JsonObject BuildEntityStateNode(
            EntityRepository repo,
            Entity entity,
            ToolkitAutoSerializer autoSerializer,
            DiagnosticGuidResolver guidResolver,
            System.Collections.Generic.IReadOnlyList<IEntityScenarioTranslator>? translators = null)
        {
            var obj = new System.Text.Json.Nodes.JsonObject();
            ref EntityHeader header = ref repo.GetHeader(entity.Index);

            // Build a translator payload map for this entity.
            var translatorPayloads = new System.Collections.Generic.Dictionary<string, System.Text.Json.Nodes.JsonNode?>();
            if (translators != null)
            {
                foreach (var translator in translators)
                {
                    if (!translator.CanTranslate(repo, entity)) continue;
                    var extracted = translator.Extract(repo, entity, guidResolver);
                    foreach (var kvp in extracted)
                        translatorPayloads[kvp.Key] = kvp.Value as System.Text.Json.Nodes.JsonNode;
                }
            }

            for (int bit = 0; bit < 256; bit++)
            {
                if (!header.ComponentMask.IsSet(bit)) continue;

                Type? compType = ComponentTypeRegistry.GetType(bit);
                string? compName = autoSerializer.GetComponentName(bit);
                if (compName == null)
                    compName = compType?.Name;
                if (compName == null) continue;

                if (translatorPayloads.TryGetValue(compName, out var translatorPayload))
                {
                    obj[compName] = translatorPayload;
                    continue;
                }

                System.Text.Json.Nodes.JsonObject? payload =
                    autoSerializer.TryExtract(repo, entity, bit, guidResolver);
                if (payload != null)
                {
                    obj[compName] = System.Text.Json.Nodes.JsonNode.Parse(payload.ToJsonString());
                    continue;
                }

                // Fallback: serialize via reflection for types the auto-serializer cannot handle
                // (e.g. internal component types registered from a different assembly).
                if (compType == null) continue;
                System.Text.Json.Nodes.JsonNode? fallback = compType.IsValueType
                    ? TrySerializeComponentByReflection(repo, entity, compType)
                    : TrySerializeManagedComponentByReflection(repo, entity, compType);
                if (fallback != null)
                    obj[compName] = fallback;
            }

            return obj;
        }

        private static System.Text.Json.Nodes.JsonNode? TrySerializeComponentByReflection(
            EntityRepository repo, Entity entity, Type compType)
        {
            if (_tryGetComponentGenericDef == null) return null;
            try
            {
                var method = _tryGetComponentGenericDef.MakeGenericMethod(compType);
                var args = new object?[] { entity, null };
                bool found = (bool)method.Invoke(repo, args)!;
                if (!found) return null;
                return JsonSerializer.SerializeToNode(args[1], compType, _reflectionFallbackOpts);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Builds <see cref="JsonSerializerOptions"/> for serializing <see cref="ChangelogEntryDto"/>
        /// objects in changelog mode.
        /// </summary>
        private static JsonSerializerOptions BuildChangelogSerializerOptions(bool minified)
        {
            var opts = new JsonSerializerOptions
            {
                IncludeFields = true,
                WriteIndented = !minified,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            };
            opts.Converters.Add(new DiffNodeConverter());
            opts.TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver();
            opts.MakeReadOnly();
            return opts;
        }

        private static JsonSerializerOptions BuildIncrementalSerializerOptions(bool minified)
        {
            var opts = new JsonSerializerOptions
            {
                IncludeFields = true,
                WriteIndented = !minified,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            };
            opts.Converters.Add(new CompactDiffListConverter());
            opts.TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver();
            opts.MakeReadOnly();
            return opts;
        }

        private static System.Collections.Generic.List<Diff.DiffNode> PruneUnchangedNodes(System.Collections.Generic.IReadOnlyList<Diff.DiffNode> nodes)
        {
            var result = new System.Collections.Generic.List<Diff.DiffNode>();
            foreach (var node in nodes)
            {
                if (!node.IsModified) continue;

                if (node is Diff.DiffObject obj)
                {
                    var prunedChildren = PruneUnchangedNodes(obj.Children);
                    obj.Children.Clear();
                    obj.Children.AddRange(prunedChildren);
                }

                result.Add(node);
            }
            return result;
        }

        private sealed class CompactDiffListConverter : JsonConverter<System.Collections.Generic.IReadOnlyList<Diff.DiffNode>>
        {
            public override System.Collections.Generic.IReadOnlyList<Diff.DiffNode> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
                => throw new NotSupportedException("Incremental deserialization is not supported.");

            public override void Write(Utf8JsonWriter writer, System.Collections.Generic.IReadOnlyList<Diff.DiffNode> value, JsonSerializerOptions options)
            {
                WriteCompactTree(writer, value);
            }

            private static void WriteCompactTree(Utf8JsonWriter writer, System.Collections.Generic.IReadOnlyList<Diff.DiffNode> nodes)
            {
                writer.WriteStartObject();
                foreach (var node in nodes)
                {
                    if (!node.IsModified) continue;

                    if (node is Diff.DiffObject obj)
                    {
                        writer.WritePropertyName(obj.Name);
                        WriteCompactTree(writer, obj.Children);
                    }
                    else if (node is Diff.DiffValue val)
                    {
                        writer.WritePropertyName(val.Name);
                        if (val.NewValue == "null")
                            writer.WriteNullValue();
                        else
                        {
                            if (writer.Options.Indented)
                            {
                                string prettyJson = Fdp.Toolkit.Serialization.JsonAestheticFormatter.FlattenNumericArrays(val.NewValue);
                                string indent = new string(' ', writer.CurrentDepth * 2);
                                string indentedJson = prettyJson
                                    .Replace("\r\n", "\n")
                                    .Replace("\r", "\n")
                                    .Replace("\n", Environment.NewLine + indent);
                                writer.WriteRawValue(indentedJson, skipInputValidation: true);
                            }
                            else
                            {
                                writer.WriteRawValue(val.NewValue, skipInputValidation: true);
                            }
                        }
                    }
                }
                writer.WriteEndObject();
            }
        }

        private static System.Text.Json.Nodes.JsonNode? TrySerializeManagedComponentByReflection(
            EntityRepository repo, Entity entity, Type compType)
        {
            if (_hasManagedComponentGeneric == null || _getManagedComponentGeneric == null) return null;
            try
            {
                var hasMethod = _hasManagedComponentGeneric.MakeGenericMethod(compType);
                bool found = (bool)hasMethod.Invoke(repo, new object[] { entity })!;
                if (!found) return null;

                var getMethod = _getManagedComponentGeneric.MakeGenericMethod(compType);
                object comp = getMethod.Invoke(repo, new object[] { entity })!;
                return JsonSerializer.SerializeToNode(comp, compType, _reflectionFallbackOpts);
            }
            catch
            {
                return null;
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────

        private static bool EntityPassesFilter(int entityIndex, Entity entity, JsonExportOptions options)
        {
            if (options.FilterByEntityIndex)
                return entityIndex == options.TargetEntityIndex;
            if (options.FilterBySelection && options.TargetEntities.Count > 0)
                return options.TargetEntities.Contains(entity);
            return true;
        }

        /// <summary>
        /// Writes a <see cref="JsonNode"/> (or null) as the current JSON value.
        /// When not minified, numeric arrays are flattened to a single line.
        /// </summary>
        private static void WritePayloadNode(Utf8JsonWriter writer, JsonNode? node, bool minified)
        {
            if (node == null)
            {
                writer.WriteNullValue();
                return;
            }

            if (minified)
            {
                node.WriteTo(writer);
                return;
            }

            string rawJson = node.ToJsonString();
            string prettyJson = JsonAestheticFormatter.FlattenNumericArrays(rawJson);
            string indent = new string(' ', writer.CurrentDepth * 2);

            string indentedJson = prettyJson
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace("\n", Environment.NewLine + indent);

            writer.WriteRawValue(indentedJson, skipInputValidation: true);
        }

        /// <summary>
        /// Iterates all registered component type IDs and calls
        /// <c>RegisterComponent&lt;T&gt;()</c> via reflection so the sandbox
        /// <see cref="EntityRepository"/> can apply playback frames without
        /// throwing on unregistered type IDs.
        /// </summary>
        private static void AutoRegisterAllComponentTypes(EntityRepository repo)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                string? fullName = assembly.FullName;
                if (!string.IsNullOrEmpty(fullName) &&
                    (fullName.StartsWith("System", StringComparison.Ordinal) ||
                     fullName.StartsWith("Microsoft", StringComparison.Ordinal)))
                {
                    continue;
                }

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (System.Reflection.ReflectionTypeLoadException ex)
                {
                    types = System.Array.FindAll(ex.Types, t => t != null)!;
                }
                catch
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    if (type.GetCustomAttributes(typeof(ComponentIdAttribute), false).Length == 0) continue;
                    try
                    {
                        ComponentTypeRegistry.GetOrRegisterManaged(type);
                    }
                    catch
                    {
                        // Skip types that cannot be registered.
                    }
                }
            }
            // Find RegisterComponent<T>(DataPolicy? policyOverride = null) — the single
            // public generic instance method of that name with exactly one parameter.
            System.Reflection.MethodInfo? registerMethod = null;
            foreach (var m in typeof(EntityRepository)
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (m.Name != "RegisterComponent") continue;
                if (!m.IsGenericMethodDefinition) continue;
                if (m.GetParameters().Length == 1) { registerMethod = m; break; }
            }
            if (registerMethod == null) return;

            foreach (int typeId in ComponentTypeRegistry.GetAllTypeIds())
            {
                Type? type = ComponentTypeRegistry.GetType(typeId);
                if (type == null) continue;
                try
                {
                    registerMethod.MakeGenericMethod(type).Invoke(repo, new object?[] { null });
                }
                catch
                {
                    // Skip types that cannot be registered (e.g. abstract types,
                    // types with unsupported layouts, etc.).
                }
            }
        }

        /// <summary>
        /// Iterates AppDomain assemblies for value types with EventIdAttribute and calls
        /// <see cref="FdpEventBus.PrepareForNativeEventReplay{T}()"/> so events injected
        /// during playback land in typed, inspectable streams.
        /// </summary>
        private static void AutoRegisterAllEventTypes(FdpEventBus bus)
        {
            var method = typeof(FdpEventBus).GetMethod(
                nameof(FdpEventBus.PrepareForNativeEventReplay),
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                string? fullName = assembly.FullName;
                if (!string.IsNullOrEmpty(fullName) &&
                    (fullName.StartsWith("System", StringComparison.Ordinal) ||
                     fullName.StartsWith("Microsoft", StringComparison.Ordinal)))
                {
                    continue;
                }

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (System.Reflection.ReflectionTypeLoadException ex)
                {
                    types = System.Array.FindAll(ex.Types, t => t != null)!;
                }
                catch
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    if (!type.IsValueType) continue;
                    bool hasEventId = type.GetCustomAttributes(typeof(EventIdAttribute), false).Length > 0;
                    if (!hasEventId) continue;
                    try
                    {
                        method.MakeGenericMethod(type).Invoke(bus, null);
                    }
                    catch
                    {
                        // Skip types that cannot be registered.
                    }
                }
            }
        }

        /// <summary>
        /// Builds a <see cref="JsonSerializerOptions"/> instance suitable for serializing
        /// event payload objects.  Adds an <see cref="EntityJsonConverter"/> so that
        /// <see cref="Entity"/> struct fields inside events are rendered as
        /// <c>"[Index, vGeneration]"</c> strings rather than object literals.
        /// </summary>
        private static JsonSerializerOptions BuildEventSerializerOptions()
        {
            var opts = new JsonSerializerOptions
            {
                IncludeFields = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            };

            // Add our custom Entity converter first so it takes priority.
            opts.Converters.Add(new EntityJsonConverter());

            // Copy converters from the platform singleton for Vector3/Quaternion etc.
            foreach (var c in FdpJsonOptionsRegistry.DefaultRelaxed.Converters)
                opts.Converters.Add(c);

            opts.TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver();
            opts.MakeReadOnly();
            return opts;
        }

        // ── Nested converters ────────────────────────────────────────────────────

        /// <summary>
        /// Renders an <see cref="Entity"/> as the human-readable string
        /// <c>"[Index, vGeneration]"</c> inside event payload JSON.
        /// </summary>
        private sealed class EntityJsonConverter : JsonConverter<Entity>
        {
            public override Entity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
                => Entity.Null;

            public override void Write(Utf8JsonWriter writer, Entity value, JsonSerializerOptions options)
                => writer.WriteStringValue(value.IsNull ? "null" : $"[{value.Index}, v{value.Generation}]");
        }

        /// <summary>
        /// Serializes <see cref="Diff.DiffNode"/> (and its subclasses) to JSON for changelog output.
        /// Writes a compact object with Name, IsModified, and type-specific fields.
        /// </summary>
        private sealed class DiffNodeConverter : JsonConverter<Diff.DiffNode>
        {
            public override bool CanConvert(Type typeToConvert)
                => typeof(Diff.DiffNode).IsAssignableFrom(typeToConvert);

            public override Diff.DiffNode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
                => throw new NotSupportedException("DiffNode deserialization is not supported.");

            public override void Write(Utf8JsonWriter writer, Diff.DiffNode value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                writer.WriteString("Name", value.Name);
                writer.WriteBoolean("IsModified", value.IsModified);

                if (value is Diff.DiffObject obj)
                {
                    writer.WriteString("Kind", "Object");
                    writer.WriteStartArray("Children");
                    foreach (Diff.DiffNode child in obj.Children)
                        Write(writer, child, options);
                    writer.WriteEndArray();
                }
                else if (value is Diff.DiffValue leaf)
                {
                    writer.WriteString("Kind", "Value");
                    writer.WriteString("OldValue", leaf.OldValue);
                    writer.WriteString("NewValue", leaf.NewValue);
                    writer.WriteString("ValueType", leaf.ValueType.ToString());
                }

                writer.WriteEndObject();
            }
        }
    }
}

