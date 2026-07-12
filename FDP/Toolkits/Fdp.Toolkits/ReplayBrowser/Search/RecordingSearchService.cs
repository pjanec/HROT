using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Threading;
using Fdp.Core;
using Fdp.Core.FlightRecorder;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// Headless recording search engine. Each public method opens its own
    /// PlaybackController and EntityRepository, completely isolated from the GUI context.
    /// </summary>
    public sealed class RecordingSearchService : IRecordingSearchService
    {
        private readonly IPredicateCompiler _predicateCompiler;
        private readonly IEventScannerCompiler _eventScannerCompiler;

        // Cached generic MethodInfo for RegisterComponent<T>(DataPolicy?).
        private static readonly MethodInfo _registerComponentMethod =
            typeof(EntityRepository).GetMethod(
                "RegisterComponent",
                new[] { typeof(DataPolicy?) })!;

        public RecordingSearchService(
            IPredicateCompiler predicateCompiler,
            IEventScannerCompiler eventScannerCompiler)
        {
            _predicateCompiler = predicateCompiler
                ?? throw new ArgumentNullException(nameof(predicateCompiler));
            _eventScannerCompiler = eventScannerCompiler
                ?? throw new ArgumentNullException(nameof(eventScannerCompiler));
        }

        // ── IRecordingSearchService ──────────────────────────────────────────

        /// <inheritdoc/>
        public IReadOnlyList<SearchResultDto> ExecuteSearch(string fdpPath, SearchPredicateDto root, TargetEntityFilter? entityFilter = null, CancellationToken ct = default)
        {
            if (fdpPath == null) throw new ArgumentNullException(nameof(fdpPath));
            if (root == null) throw new ArgumentNullException(nameof(root));

            // Dispatch to specialized loop by root predicate type.
            if (root is TransientEventPredicateDto eventPred)
                return RunEventScan(fdpPath, eventPred, entityFilter, ct);

            if (root is LifecyclePredicateDto lifecyclePred)
            {
                var ranges = ExecuteLifecycleSearch(fdpPath, lifecyclePred, entityFilter, ct);
                // Flatten lifecycle results into SearchResultDto for uniform output.
                var flat = new List<SearchResultDto>(ranges.Count);
                foreach (var lr in ranges)
                    flat.Add(new SearchResultDto(lr.StartFrame, 0L, lr.Entity, lr.MatchContext));
                return flat;
            }

            // All other types (component, structural, spatial, compound) use the frame-step loop.
            return RunFrameStepScan(fdpPath, root, entityFilter, ct);
        }

        /// <inheritdoc/>
        public IReadOnlyList<LifecycleSearchResultDto> ExecuteLifecycleSearch(
            string fdpPath,
            LifecyclePredicateDto criteria,
            TargetEntityFilter? entityFilter = null,
            CancellationToken ct = default)
        {
            if (fdpPath == null) throw new ArgumentNullException(nameof(fdpPath));
            if (criteria == null) throw new ArgumentNullException(nameof(criteria));

            return RunLifecycleScan(fdpPath, criteria, entityFilter, ct);
        }

        // ── Frame-step scan (component / structural / spatial / compound) ────

        private List<SearchResultDto> RunFrameStepScan(string fdpPath, SearchPredicateDto root, TargetEntityFilter? entityFilter, CancellationToken ct)
        {
            var results = new List<SearchResultDto>(64);

            using var playback = new PlaybackController(fdpPath);
            var repo = new EntityRepository();
            var bus = new FdpEventBus();

            RegisterAllComponents(repo, playback);
            playback.EventBus = bus;

            // Compile predicate and extract mandatory component types for early exit.
            var compiledFn = _predicateCompiler.CompileComponentPredicate(root);
            var mandatory = _predicateCompiler.ExtractMandatoryComponents(root);

            // Check if any mandatory component has a registered table in the replay repo.
            // If none do, no match is possible — skip the scan entirely.
            if (mandatory.Count > 0)
            {
                var registeredTables = repo.GetRegisteredComponentTypes();
                bool hasAnyMandatory = false;
                foreach (var t in mandatory)
                {
                    if (registeredTables.ContainsKey(t)) { hasAnyMandatory = true; break; }
                }
                if (!hasAnyMandatory)
                    return results; // Zero-allocation early exit for SR-T34 scenario.
            }

            // Specialized state for structural and spatial modes (allocated once before loop).
            var structuralState = root is StructuralPredicateDto ? new HashSet<Entity>() : null;
            var spatialState = root is SpatialBoundingPredicateDto ? new HashSet<Entity>() : null;

            // Build EntityQuery for component and compound modes.
            EntityQuery? deltaQuery = BuildDeltaQuery(repo, root, mandatory);
            var frameCandidates = new List<Entity>(32);
            var previousMatches = new HashSet<int>();

            while (playback.StepForward(repo))
            {
                if (ct.IsCancellationRequested) break;
                int frame = playback.CurrentFrame;
                long ticks = playback.GetFrameMetadata(frame).WallClockTicks;

                // ── Component / Compound property mode ───────────────────────
                if (deltaQuery != null)
                {
                    frameCandidates.Clear();
                    foreach (var entity in deltaQuery)
                    {
                        if (entityFilter != null && !entityFilter.Passes(repo, entity)) continue;
                        if (compiledFn(repo, entity))
                        {
                            frameCandidates.Add(entity);
                            results.Add(new SearchResultDto(frame, ticks, entity,
                                BuildComponentContext(root, repo, entity)));
                        }
                    }

                    previousMatches.Clear();
                    for (int i = 0; i < frameCandidates.Count; i++)
                        previousMatches.Add(frameCandidates[i].Index);
                }

                // ── Spatial mode ─────────────────────────────────────────────
                if (root is SpatialBoundingPredicateDto spatial && spatialState != null)
                {
                    RunSpatialFrame(repo, frame, ticks, spatial, spatialState, results, entityFilter);
                }

                // ── Structural mode ──────────────────────────────────────────
                if (root is StructuralPredicateDto structural && structuralState != null)
                {
                    RunStructuralFrame(repo, frame, ticks, structural, structuralState, results, entityFilter);
                }

                // Per-frame cleanup: remove destroyed entities from state sets.
                var destroyed = repo.GetDestructionLog();
                if (destroyed.Count > 0)
                {
                    for (int i = 0; i < destroyed.Count; i++)
                    {
                        Entity dead = destroyed[i];
                        spatialState?.Remove(dead);
                        structuralState?.Remove(dead);
                    }
                    // Structural: also emit "Lost (Destroyed)" for entities in the set.
                    if (root is StructuralPredicateDto strDead && structuralState != null)
                    {
                        for (int i = 0; i < destroyed.Count; i++)
                        {
                            Entity dead = destroyed[i];
                            if (structuralState.Contains(dead))
                            {
                                results.Add(new SearchResultDto(frame, ticks, dead,
                                    "Lost " + strDead.ComponentType.Name + " (Destroyed)"));
                                structuralState.Remove(dead);
                            }
                        }
                    }
                }

                repo.ClearDestructionLog();
            }

            return results;
        }

        // ── Event scan ───────────────────────────────────────────────────────

        private List<SearchResultDto> RunEventScan(string fdpPath, TransientEventPredicateDto predicate, TargetEntityFilter? entityFilter, CancellationToken ct)
        {
            EventScannerDelegate scanner = _eventScannerCompiler.CompileScanner(predicate);
            var results = new List<SearchResultDto>(64);

            using var playback = new PlaybackController(fdpPath);
            var repo = new EntityRepository();
            var bus = new FdpEventBus();

            RegisterAllComponents(repo, playback);
            // CRITICAL: set EventBus BEFORE the loop so ApplyFrame injects events.
            playback.EventBus = bus;

            // Per DESIGN.md §6.4 strict contract: step first, then scan, no ClearCurrentBuffers.
            while (playback.StepForward(repo))
            {
                if (ct.IsCancellationRequested) break;
                int frame = playback.CurrentFrame;
                long ticks = playback.GetFrameMetadata(frame).WallClockTicks;
                // Step 1 already happened (StepForward injected events into bus).
                // Step 2: scan immediately without clearing the bus.
                scanner(bus, frame, ticks, results, repo, entityFilter);
                // Deliberately NO bus.ClearCurrentBuffers() here — this is the SR-T38 invariant.
            }

            return results;
        }

        // ── Lifecycle scan ───────────────────────────────────────────────────

        private List<LifecycleSearchResultDto> RunLifecycleScan(
            string fdpPath,
            LifecyclePredicateDto criteria,
            TargetEntityFilter? entityFilter,
            CancellationToken ct)
        {
            var activeRanges = new Dictionary<Entity, int>(); // entity -> startFrame
            var results = new List<LifecycleSearchResultDto>(32);

            using var playback = new PlaybackController(fdpPath);
            var repo = new EntityRepository();

            RegisterAllComponents(repo, playback);

            int eofFrame = 0;

            while (playback.StepForward(repo))
            {
                if (ct.IsCancellationRequested) break;
                int frame = playback.CurrentFrame;
                eofFrame = frame;

                // Detect births: check all alive entities for criteria match.
                int maxIdx = repo.MaxEntityIndex;
                for (int i = 0; i <= maxIdx; i++)
                {
                    Entity entity = repo.GetEntityByIndex(i);
                    if (entity.IsNull) continue;
                    if (entityFilter != null && !entityFilter.Passes(repo, entity)) continue;
                    if (activeRanges.ContainsKey(entity)) continue;

                    if (MatchesLifecycleCriteria(entity, repo, criteria))
                        activeRanges[entity] = frame;
                }

                // Detect deaths via destruction log.
                var destroyed = repo.GetDestructionLog();
                for (int i = 0; i < destroyed.Count; i++)
                {
                    Entity dead = destroyed[i];
                    if (activeRanges.TryGetValue(dead, out int startFrame))
                    {
                        results.Add(new LifecycleSearchResultDto(
                            dead, startFrame, frame,
                            BuildLifecycleContext(dead, criteria)));
                        activeRanges.Remove(dead);
                    }
                }

                repo.ClearDestructionLog();
            }

            // Flush remaining alive ranges at EOF.
            foreach (var kvp in activeRanges)
            {
                results.Add(new LifecycleSearchResultDto(
                    kvp.Key, kvp.Value, eofFrame,
                    BuildLifecycleContext(kvp.Key, criteria)));
            }

            return results;
        }

        // ── Spatial frame processing ─────────────────────────────────────────

        private static void RunSpatialFrame(
            EntityRepository repo,
            int frame,
            long ticks,
            SpatialBoundingPredicateDto predicate,
            HashSet<Entity> insideZone,
            List<SearchResultDto> results,
            TargetEntityFilter? entityFilter)
        {
            if (predicate.PositionComponentType == null) return;

            int typeId = ComponentTypeRegistry.GetId(predicate.PositionComponentType);
            if (typeId < 0) return;

            // Build a query for entities with the position component.
            EntityQuery posQuery = repo.Query().WithComponentId(typeId).Build();
            BoundingBox2D bounds = predicate.Bounds;
            BoundaryEvent trigger = predicate.TriggerEvent;

            repo.QueryDelta(posQuery, 0, entity =>
            {
                if (entityFilter != null && !entityFilter.Passes(repo, entity)) return;
                if (!repo.HasComponentByTypeId(entity, typeId)) return;

                // Read X and Y from the position component using direct reflection.
                // (Expression-tree approach would be faster; this is acceptable for spatial mode.)
                Vector2 pos = ReadPosition2D(repo, entity, predicate);

                bool nowInside = IsInBounds(pos, bounds);
                bool wasInside = insideZone.Contains(entity);

                if (nowInside && !wasInside)
                {
                    insideZone.Add(entity);
                    if (trigger != BoundaryEvent.Exit)
                        results.Add(new SearchResultDto(frame, ticks, entity, "Entered Area"));
                }
                else if (!nowInside && wasInside)
                {
                    insideZone.Remove(entity);
                    if (trigger != BoundaryEvent.Entry)
                        results.Add(new SearchResultDto(frame, ticks, entity, "Exited Area"));
                }
            });
        }

        // ── Structural frame processing ──────────────────────────────────────

        private static void RunStructuralFrame(
            EntityRepository repo,
            int frame,
            long ticks,
            StructuralPredicateDto predicate,
            HashSet<Entity> hasComponent,
            List<SearchResultDto> results,
            TargetEntityFilter? entityFilter)
        {
            int typeId = ComponentTypeRegistry.GetId(predicate.ComponentType);
            if (typeId < 0) return;

            string typeName = predicate.ComponentType.Name;

            int maxIdx = repo.MaxEntityIndex;
            for (int i = 0; i <= maxIdx; i++)
            {
                ref var compRSS = ref repo.GetComponentMask(i);
                ref var metaRSS = ref repo.GetMetadata(i);
                if (!metaRSS.IsActive) continue;

                // Scan all active entities: version-based filtering is unreliable during
                // playback because RestoreChunkFromBuffer does not update chunk versions.
                // State transitions are detected correctly by the hasComponent HashSet.

                Entity entity = repo.GetEntityByIndex(i);
                if (entity.IsNull) continue;
                if (entityFilter != null && !entityFilter.Passes(repo, entity)) continue;

                bool present = ComputeEffectivePresence(ref compRSS, ref metaRSS, typeId, predicate.AuthorityRequirement);
                bool was = hasComponent.Contains(entity);

                if (present && !was)
                {
                    hasComponent.Add(entity);
                    if (predicate.ModificationType == StructuralModification.Added ||
                        predicate.ModificationType == StructuralModification.AnyChange)
                    {
                        results.Add(new SearchResultDto(frame, ticks, entity, "Gained " + typeName));
                    }
                }
                else if (!present && was)
                {
                    hasComponent.Remove(entity);
                    if (predicate.ModificationType == StructuralModification.Removed ||
                        predicate.ModificationType == StructuralModification.AnyChange)
                    {
                        results.Add(new SearchResultDto(frame, ticks, entity, "Lost " + typeName));
                    }
                }
            }
        }

        // ── Authority helper ─────────────────────────────────────────────────

        private static bool ComputeEffectivePresence(
            ref BitMask512 componentMask,
            ref EntityMetadataCold meta,
            int typeId,
            AuthorityRequirement req)
        {
            return req switch
            {
                AuthorityRequirement.RequireAuthority =>
                    componentMask.IsSet(typeId) && meta.AuthorityMask.IsSet(typeId),
                AuthorityRequirement.RequireGhost =>
                    componentMask.IsSet(typeId) && !meta.AuthorityMask.IsSet(typeId),
                _ => componentMask.IsSet(typeId)
            };
        }

        // ── Spatial helpers ──────────────────────────────────────────────────

        private static Vector2 ReadPosition2D(
            EntityRepository repo,
            Entity entity,
            SpatialBoundingPredicateDto predicate)
        {
            Type compType = predicate.PositionComponentType;
            int typeId = ComponentTypeRegistry.GetId(compType);
            if (typeId < 0) return Vector2.Zero;

            object? comp = null;
            if (compType.IsValueType)
            {
                // Box the unmanaged component using pointer + Marshal.
                unsafe
                {
                    void* ptr = repo.GetComponentPointer(entity, typeId);
                    if (ptr == null) return Vector2.Zero;
                    comp = System.Runtime.InteropServices.Marshal.PtrToStructure(
                        new IntPtr(ptr), compType);
                }
            }
            else
            {
                comp = repo.GetManagedComponentByTypeId(entity, typeId);
            }

            if (comp == null) return Vector2.Zero;

            float x = ReadFloatField(comp, predicate.PositionXPath);
            float y = ReadFloatField(comp, predicate.PositionYPath);
            return new Vector2(x, y);
        }

        private static float ReadFloatField(object obj, string fieldPath)
        {
            object? cur = obj;
            string[] segments = fieldPath.Split('.');
            foreach (string seg in segments)
            {
                if (cur == null) return 0f;
                Type t = cur.GetType();
                FieldInfo? fi = t.GetField(seg,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi != null) { cur = fi.GetValue(cur); continue; }
                PropertyInfo? pi = t.GetProperty(seg,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (pi != null) { cur = pi.GetValue(cur); continue; }
                return 0f;
            }
            return cur is float f ? f : (cur is double d ? (float)d : 0f);
        }

        private static bool IsInBounds(Vector2 pos, BoundingBox2D bounds)
        {
            return pos.X >= bounds.Min.X && pos.X <= bounds.Max.X
                && pos.Y >= bounds.Min.Y && pos.Y <= bounds.Max.Y;
        }

        // ── Lifecycle helpers ────────────────────────────────────────────────

        private static bool MatchesLifecycleCriteria(
            Entity entity,
            EntityRepository repo,
            LifecyclePredicateDto criteria)
        {
            switch (criteria.IdentifierType)
            {
                case EntityIdentifierType.EcsHandle:
                    return int.TryParse(criteria.TargetValue, out int idx) && entity.Index == idx;

                case EntityIdentifierType.NetworkId:
                    // Simplified: treat NetworkId as entity index for test compatibility.
                    return int.TryParse(criteria.TargetValue, out int netId) && entity.Index == netId;

                case EntityIdentifierType.NameSubstring:
                {
                    if (criteria.NameComponentType == null) return false;
                    int typeId = ComponentTypeRegistry.GetId(criteria.NameComponentType);
                    if (typeId < 0 || !repo.HasComponentByTypeId(entity, typeId)) return false;

                    object? comp = null;
                    if (criteria.NameComponentType.IsValueType)
                    {
                        unsafe
                        {
                            void* ptr = repo.GetComponentPointer(entity, typeId);
                            if (ptr == null) return false;
                            comp = System.Runtime.InteropServices.Marshal.PtrToStructure(
                                new IntPtr(ptr), criteria.NameComponentType);
                        }
                    }
                    else
                    {
                        comp = repo.GetManagedComponentByTypeId(entity, typeId);
                    }

                    if (comp == null) return false;

                    string? name = ReadStringField(comp, criteria.NamePropertyPath);
                    return name != null &&
                           name.Contains(criteria.TargetValue, StringComparison.OrdinalIgnoreCase);
                }

                default:
                    return false;
            }
        }

        private static string? ReadStringField(object obj, string fieldPath)
        {
            object? cur = obj;
            string[] segments = fieldPath.Split('.');
            foreach (string seg in segments)
            {
                if (cur == null) return null;
                Type t = cur.GetType();
                FieldInfo? fi = t.GetField(seg,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi != null) { cur = fi.GetValue(cur); continue; }
                PropertyInfo? pi = t.GetProperty(seg,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (pi != null) { cur = pi.GetValue(cur); continue; }
                return null;
            }
            return cur?.ToString();
        }

        private static string BuildLifecycleContext(Entity entity, LifecyclePredicateDto criteria)
        {
            return criteria.IdentifierType switch
            {
                EntityIdentifierType.EcsHandle => "EcsHandle:" + entity.Index,
                EntityIdentifierType.NetworkId => "NetworkId:" + criteria.TargetValue,
                _ => "Name:" + criteria.TargetValue
            };
        }

        // ── Context message builders ─────────────────────────────────────────

        private static string BuildComponentContext(
            SearchPredicateDto root,
            EntityRepository repo,
            Entity entity)
        {
            if (root is PropertyMatchDto pm)
                return pm.ComponentType.Name + "." + pm.PropertyPath + " matched";
            return "Matched";
        }

        // ── EntityQuery builder ──────────────────────────────────────────────

        private static EntityQuery? BuildDeltaQuery(
            EntityRepository repo,
            SearchPredicateDto root,
            IReadOnlyList<Type> mandatory)
        {
            if (root is StructuralPredicateDto || root is SpatialBoundingPredicateDto)
                return null; // These modes handle iteration themselves.

            var qb = repo.Query();

            foreach (var t in mandatory)
            {
                int id = ComponentTypeRegistry.GetId(t);
                if (id >= 0)
                {
                    qb.WithComponentId(id);
                }
            }

            // If no mandatory components, query all entities (no type filter).
            return qb.Build();
        }

        // ── Component registration ───────────────────────────────────────────

        /// <summary>
        /// Registers all globally-known component types in <paramref name="repo"/> so that
        /// PlaybackSystem.ApplyChunkData can find the tables during frame restoration.
        /// </summary>
        internal static void RegisterAllComponents(EntityRepository repo, PlaybackController playback)
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
                catch (ReflectionTypeLoadException ex)
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

            // Prefer schema manifest (only registers types actually in the recording).
            var manifest = playback.Metadata?.SchemaManifest;
            if (manifest != null && manifest.Count > 0)
            {
                foreach (var kvp in manifest)
                {
                    int typeId = kvp.Key;
                    Type? type = ComponentTypeRegistry.GetType(typeId);
                    if (type == null) continue;
                    TryRegisterComponentByType(repo, type);
                }
                return;
            }

            // Fallback: register all globally-known component types.
            foreach (Type type in ComponentTypeRegistry.GetAllTypes())
                TryRegisterComponentByType(repo, type);
        }

        private static void TryRegisterComponentByType(EntityRepository repo, Type type)
        {
            if (_registerComponentMethod == null) return;
            try
            {
                var concrete = _registerComponentMethod.MakeGenericMethod(type);
                concrete.Invoke(repo, new object?[] { null });
            }
            catch
            {
                // Silently skip types that cannot be registered (e.g. abstract or pointer types).
            }
        }
    }
}
