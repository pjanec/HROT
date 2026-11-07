using Fdp.Toolkit.Replication;
using System;
using System.Collections.Generic;
using System.Reflection;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Hrot.CGF.Orchestration;
using Hrot.Common.Serializers;
using Hrot.Core.Network;
using Fdp.Toolkit.NetworkSpawning.Events;

namespace Hrot.CGF.Orchestration
{
    /// <summary>
    /// Two-pass entity extraction engine that converts scenario JSON into
    /// <see cref="EntityCreationRequest"/> objects ready for the genesis pipeline.
    ///
    /// <para>
    /// <b>Pass 1 – ID allocation:</b> pre-allocate new network IDs for every entity
    /// that carries a <c>NetworkIdentity</c> component.  Records the old-to-new mapping
    /// so that network IDs embedded in behavior <c>BehaviorParams</c> JSON strings can be
    /// patched in Pass 2.
    /// </para>
    /// <para>
    /// <b>Pass 2 – extraction:</b> entities are classified as root (no <c>PartMetadata</c>)
    /// or structural child (has <c>PartMetadata</c>).  Root entities are extracted into
    /// <see cref="EntityCreationRequest"/> objects with the exclusion mask applied; child
    /// entity components are harvested into the parent request's
    /// <see cref="EntityCreationRequest.ChildComponentOverrides"/> dictionary.
    /// </para>
    /// <para>
    /// The transient staging <see cref="EntityRepository"/> is always disposed after
    /// extraction, even on exception.
    /// </para>
    /// </summary>
    public sealed class StagingEntityExtractor : IScenarioEntityExtractor
    {
        // ⭐ CE-2396 — a REUSED staging world (the macro expander's instance): a fresh EntityRepository allocates ≈ 93 MB of chunk
        //   bookkeeping (every table sized for 1 000 000 entities, CE-2395 ②) and registers every known component type by reflection —
        //   📐 measured per macro expansion on Crossfire: 20.7 ms median (13 ms of it the registration) and ≈ 93 MB of garbage, ≈ 1 per
        //   3 s (every respawn). Reused, it is EMPTIED after each extraction (Clear: index, tables, bus) and re-registers only when the
        //   global type set grew. A scenario load (rare) keeps a fresh world: holding one between loads would only park the memory.
        private readonly bool _reuseStagingWorld;
        private EntityRepository? _stagingWorld;
        private int _stagingBusy;
        private int _stagingTypesGeneration = -1, _stagingTypesCount = -1;

        /// <param name="reuseStagingWorld">⭐ CE-2396 — keep one staging world and empty it between extractions (the macro expander: one
        /// extraction per expansion); false = a fresh world per extraction, disposed after it (a scenario load).</param>
        public StagingEntityExtractor(bool reuseStagingWorld = false) => _reuseStagingWorld = reuseStagingWorld;

        // ── Static exclusion mask ──────────────────────────────────────────────────
        // Built once at class init using GlobalComponentIds named constants.
        // At extraction time the instance mask is built by copying this mask and
        // OR-ing in any translator-consumed component masks from the provided
        // ScenarioSerializer (Decision 10).

        private static readonly BitMask512 s_staticExclusionMask = BuildStaticMask();

        private static BitMask512 BuildStaticMask()
        {
            var mask = new BitMask512();
            mask.SetBit(GlobalComponentIds.NetworkIdentity);       // 50
            mask.SetBit(GlobalComponentIds.NetworkAuthority);      // 51
            mask.SetBit(GlobalComponentIds.DescriptorOwnership);   // 59
            mask.SetBit(GlobalComponentIds.TkbIdentity);           // 65
            mask.SetBit(GlobalComponentIds.GhostStateTracker);     // 66
            // CE-281: NetworkOwnership (140) retired — merged into NetworkAuthority (51, already masked above).
            mask.SetBit(GlobalComponentIds.PendingNetworkAck);     // 141
            return mask;
        }

        // ── Staging‑repo bootstrap ────────────────────────────────────────────────

        // Cached MethodInfo for the two internal component-registration helpers.
        // Using these lower-level methods avoids the DataPolicy branching in the public
        // RegisterComponent<T> overload, and keeps the registration path minimal.
        private static readonly MethodInfo s_registerUnmanagedMethod =
            typeof(EntityRepository).GetMethod(
                "RegisterUnmanagedComponent",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

        private static readonly MethodInfo s_registerManagedInternalMethod =
            typeof(EntityRepository).GetMethod(
                "RegisterManagedComponentInternal",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

        /// <summary>
        /// Registers every component type currently in the global
        /// <see cref="ComponentTypeRegistry"/> into <paramref name="repo"/>.
        ///
        /// <para>
        /// <see cref="FdpAutoSerializer.TryInject"/> calls
        /// <c>repo.SetComponent&lt;T&gt;</c> for each component it deserialises, but
        /// an empty <see cref="EntityRepository"/> has no component tables yet.
        /// Pre-registering all known types ensures the component tables exist before
        /// any injection attempt.
        /// </para>
        /// <para>
        /// Value types go through <c>RegisterUnmanagedComponent&lt;T&gt;</c>; class
        /// types go through <c>RegisterManagedComponentInternal&lt;T&gt;</c>.
        /// Any type that fails the <c>unmanaged</c> or <c>class</c> constraint check
        /// (e.g. a struct containing reference fields) is silently skipped — such
        /// types cannot appear in a compressed, serialisable scenario file.
        /// </para>
        /// </summary>
        private static void RegisterAllGlobalTypesInRepo(EntityRepository repo)
        {
            foreach (int typeId in ComponentTypeRegistry.GetAllTypeIds())
            {
                var type = ComponentTypeRegistry.GetType(typeId);
                if (type is null || type.IsAbstract || type.IsInterface) continue;
                if (type.ContainsGenericParameters) continue;

                // The 'unmanaged' / 'class' constraints are verified at runtime by
                // MakeGenericMethod.  If the type fails the constraint (e.g. a value
                // type that contains a managed reference), MakeGenericMethod throws
                // ArgumentException — NOT TargetInvocationException.  We therefore
                // catch Exception broadly so the loop proceeds to the next type.
                var method = type.IsValueType ? s_registerUnmanagedMethod : s_registerManagedInternalMethod;
                try
                {
                    method.MakeGenericMethod(type).Invoke(repo, null);
                }
                catch (Exception)
                {
                    // Skip types that do not satisfy their constraint or cannot be
                    // registered for any other reason; they will not appear in
                    // serialised scenario files.
                }
            }
        }


        /// <summary>
        /// Optional callback invoked in the finally block after the staging
        /// <see cref="EntityRepository"/> has been disposed.
        /// Used by unit tests to verify disposal behaviour; not for production use.
        /// </summary>
        internal Action? StagingRepositoryDisposedCallback { get; set; }

        /// <summary>
        /// ⭐⭐⭐ <b><c>BP-509</c> — the staging→runtime id table, handed OUT.</b>
        /// 📄 <c>DESIGN_Variable_Watch_Pinning.md</c> §5 · §8 ① · §8a.
        ///
        /// <para>⛔⛔ <b>Pass 1's <c>guidToNew</c> was a LOCAL that died here</b>, so anything keyed on a
        /// runtime network id — a watch pin, a breakpoint — broke on every scenario reload with no way to
        /// recover the correspondence. ⭐ This publishes the table; ⛔ <b>the remap CODE does not move and
        /// is not copied</b> *(ruling 9 · <c>R-79</c>)</b>.</para>
        ///
        /// <para>⭐⭐ <b>A CALLBACK, not a bus</b> — 🔒 the user's ruling, <c>2026-08-19</c>. ⛔ Taking an
        /// <c>FdpEventBus</c> here would give <c>Hrot.CGF</c> a dependency for one line and stop this
        /// class being a pure transform; ⭐ <b>the subsystem</b> wires the sink to its own bus, which keeps
        /// <c>R-79</c> *(editor and CGF separately deployable)* intact. ⚠ Same shape as
        /// <see cref="StagingRepositoryDisposedCallback"/> one line up — ⛔ not a new idea in this class.</para>
        ///
        /// <para>⚠ <b>Invoked once per successful extraction, AFTER the requests are built</b> — ⛔ not
        /// after Pass 1: a load that throws in Pass 2 produced no entities, and publishing its table
        /// would advertise a correspondence that never came to exist.</para>
        ///
        /// <para>⚠ <b>Public</b>, unlike the disposal callback: that one is a test hook, this one is
        /// production wiring.</para>
        /// </summary>
        public Action<IReadOnlyDictionary<Guid, int>>? OnRemap { get; set; }

        // ── IScenarioEntityExtractor ───────────────────────────────────────────────

        /// <inheritdoc cref="IScenarioEntityExtractor.Extract"/>
        IReadOnlyList<EntityCreationRequest> IScenarioEntityExtractor.Extract(
            ScenarioSerializer serializer,
            string json,
            INetworkIdAllocator idAllocator)
            => Extract(serializer, json, idAllocator);

        /// <summary>
        /// ⭐⭐ The remapping overload — <b>implemented here, not left to the interface's default</b>.
        /// </summary>
        /// <remarks>
        /// 🔴 The interface's default for this member IGNORES the remapper (right for an extractor with no notion of one).
        /// This class — the one that DOES remap — implemented only the three-argument member, so since <c>L4a</c> (the ONE
        /// <c>ScenarioLoadStep</c>) every live load reached the default and CGF's remapper was dropped: entity references
        /// inside mission parameters (an <c>EntityRef</c>, e.g. <c>FireAtTarget.targetNetworkId</c>) kept their
        /// STAGING ids. 📐 Measured by <c>DistributedScenarioLoadTests</c> once its offline ids stopped coinciding with the
        /// live ones (both allocators start at 1000 — which is why nothing noticed). 📄 <c>DESIGN_Cluster_Load_Phase.md</c> §4.1c.
        /// </remarks>
        IReadOnlyList<EntityCreationRequest> IScenarioEntityExtractor.Extract(
            ScenarioSerializer serializer,
            string json,
            INetworkIdAllocator idAllocator,
            ScenarioBehaviorRemapper? behaviorRemapper)
            => Extract(serializer, json, idAllocator, episodeId: null, behaviorRemapper: behaviorRemapper);

        /// <inheritdoc cref="IScenarioEntityExtractor.Extract(ScenarioSerializer, string, INetworkIdAllocator, ScenarioBehaviorRemapper?, ExtractionPasses)"/>
        public IReadOnlyList<EntityCreationRequest> Extract(
            ScenarioSerializer serializer,
            string json,
            INetworkIdAllocator idAllocator,
            ScenarioBehaviorRemapper? behaviorRemapper,
            ExtractionPasses passes)
            => Extract(serializer, json, idAllocator, episodeId: null, behaviorRemapper: behaviorRemapper, passes: passes);

        // ── Extraction ────────────────────────────────────────────────────────────

        /// <summary>
        /// Extracts all root entities from the scenario JSON and returns a list of
        /// <see cref="EntityCreationRequest"/> objects ready for the genesis pipeline.
        /// </summary>
        /// <param name="serializer">Scenario serializer used to hydrate the staging
        ///   repository and to obtain the translator-consumed component masks.</param>
        /// <param name="json">Raw scenario JSON produced by the same serializer.</param>
        /// <param name="idAllocator">Network-ID allocator used to pre-allocate IDs
        ///   for entities that carry <c>NetworkIdentity</c>.</param>
        /// <param name="episodeId">When non-null, appends an <see cref="EpisodeTag"/>
        ///   to every root entity's <c>InitialComponents</c>.</param>
        /// <param name="behaviorRemapper">Optional remapper that patches network IDs
        ///   embedded in <c>ActiveMissionPlan</c> task <c>BehaviorParams</c> JSON.</param>
        public IReadOnlyList<EntityCreationRequest> Extract(
            ScenarioSerializer serializer,
            string json,
            INetworkIdAllocator idAllocator,
            Guid? episodeId = null,
            ScenarioBehaviorRemapper? behaviorRemapper = null,
            ExtractionPasses? passes = null)
        {
            ArgumentNullException.ThrowIfNull(serializer);
            ArgumentNullException.ThrowIfNull(json);
            ArgumentNullException.ThrowIfNull(idAllocator);

            // Build the per-extraction instance exclusion mask:
            // static base (8 known components) + all translator-consumed masks.
            // Translator-consumed components contain volatile Entity handles that
            // become dangling once the staging repo is disposed (Decision 10).
            var exclusionMask = s_staticExclusionMask; // struct copy (32 bytes)
            foreach (var translator in serializer.Translators)
            {
                // some components store safe Network IDs, not volatile ECS Entity handles.
                // They must survive extraction so the genesis pipeline can remap and apply them.
                if (translator.IsExtractionSafe)
                    continue;
        
                exclusionMask.BitwiseOr(translator.GetConsumedComponentsMask());
            }

            // Additional child-extraction mask: also excludes PartMetadata itself
            // (its ParentEntity field is a volatile ECS handle valid only within
            // the staging repo — Decision 5).
            var childExclusionMask = exclusionMask;
            childExclusionMask.SetBit(GlobalComponentIds.PartMetadata);

            // ⭐ CE-2396 — the reused world when this instance keeps one and no other thread holds it; else a fresh one (as before)
            bool reused = _reuseStagingWorld && System.Threading.Interlocked.CompareExchange(ref _stagingBusy, 1, 0) == 0;
            var stagingRepo = reused ? (_stagingWorld ??= new EntityRepository()) : new EntityRepository();
            try
            {
                // Pre-register all globally known component types so that
                // FdpAutoSerializer.TryInject can call SetComponent<T> without
                // encountering "component not registered" exceptions.
                // ⭐ CE-2396 — a reused world registers again only when the global type set grew (or was reset)
                int typesGeneration = ComponentTypeRegistry.Generation, typesCount = ComponentTypeRegistry.RegisteredCount;
                if (!reused || typesGeneration != _stagingTypesGeneration || typesCount != _stagingTypesCount)
                {
                    RegisterAllGlobalTypesInRepo(stagingRepo);
                    if (reused) { _stagingTypesGeneration = typesGeneration; _stagingTypesCount = typesCount; }
                }
                // ⭐ CE-4153 — the staging load stamps every entity's file key as its EntityGuid, which then rides InitialComponents into the
                //   created entity. The staging repo must know the type even when no host has registered it globally yet.
                if (!stagingRepo.IsComponentTypeRegistered<Fdp.Toolkit.Scenario.EntityGuid>())
                    stagingRepo.RegisterComponent<Fdp.Toolkit.Scenario.EntityGuid>();
                if (!stagingRepo.IsComponentTypeRegistered<Fdp.Toolkit.Scenario.RefKey>())   // ⭐ S0 — the keys authored references name
                    stagingRepo.RegisterComponent<Fdp.Toolkit.Scenario.RefKey>();

                // Hydrate the staging repository from the scenario JSON.
                serializer.Deserialize(stagingRepo, json);

                var registeredTables = stagingRepo.GetRegisteredComponentTypes();
                int maxIdx = stagingRepo.MaxEntityIndex;

                // ── Pass 1: ID allocation ──────────────────────────────────────────
                // Pre-allocate new network IDs for every entity (root and child) that
                // carries a NetworkIdentity in the staging DOM.  Build the translation
                // map so behavior BehaviorParams can be patched in Pass 2.
                // S8 (B): a scenario file carries no network id - every staging entity KEYED by an EntityGuid gets its runtime id here, by entity index.
                var newIdByIndex = new Dictionary<int, int>();
                var guidToNew = new Dictionary<Guid, int>();   // what the Watch pins key on
                for (int i = 0; i <= maxIdx; i++)
                {
                    var maskP1 = stagingRepo.GetEntityIndex().GetComponentMask(i);
                    ref readonly var metaP1 = ref stagingRepo.GetEntityIndex().GetMetadata(i);
                    if (!metaP1.IsActive || !maskP1.IsSet(GlobalComponentIds.EntityGuid)) continue;
                    var e = stagingRepo.GetEntityByIndex(i);
                    if (e == Entity.Null) continue;
                    int newId = idAllocator.AllocateId();
                    newIdByIndex[i] = newId;
                    var pg = stagingRepo.GetComponent<Fdp.Toolkit.Scenario.EntityGuid>(e).Value;
                    if (pg != Guid.Empty) guidToNew[pg] = newId;
                }

                // ⭐⭐ S0 item 3 (DESIGN_Scenario_Rules.md SR-126) — the SCENARIO scope authored references resolve in: every entity's
                //   RefKey and EntityGuid → the id it will have, and its position (a point written as a reference takes it). Built
                //   after Pass 1 so the answers are the NEW ids; a duplicate key throws here, before anything is created.
                var refScope = BuildRefScope(stagingRepo, maxIdx, newIdByIndex);

                // ── Pass 2: Classification and extraction ──────────────────────────
                // Root entities (no PartMetadata) are extracted into requests.
                // Child entities (have PartMetadata) have their components harvested
                // into the parent's ChildComponentOverrides dictionary.

                // Root data: (staging entity, tkbType, disType, initialComponents, preAllocId)
                var rootDataList = new List<(
                    Entity stagingEntity,
                    long   tkbType,
                    ulong  disType,
                    List<object> components,
                    int   preAllocId)>();

                var entityToRootIdx = new Dictionary<Entity, int>();

                // Child buffer keyed by parentEntity (staging handle) then by InstanceId.
                var childBuffer = new Dictionary<Entity,
                    Dictionary<int, (int preAllocId, List<object> components)>>();

                for (int i = 0; i <= maxIdx; i++)
                {
                    Fdp.Core.LoadProgress.Report();   // ⭐ CE-2417 — the extraction advances (docs/DESIGN_Cluster_Load_Phase.md §12)
                    var mask = stagingRepo.GetEntityIndex().GetComponentMask(i);
                    ref readonly var meta = ref stagingRepo.GetEntityIndex().GetMetadata(i);
                    if (!meta.IsActive) continue;

                    var e = stagingRepo.GetEntityByIndex(i);
                    if (e == Entity.Null) continue;

                    if (mask.IsSet(GlobalComponentIds.PartMetadata))
                    {
                        // ── Child entity ───────────────────────────────────────────
                        var partMeta = stagingRepo.GetComponent<PartMetadata>(e);

                        newIdByIndex.TryGetValue(i, out int childPreAllocId);

                        var childComps = ExtractEntityComponents(
                            registeredTables, i, in mask, in childExclusionMask);

                        if (!childBuffer.TryGetValue(partMeta.ParentEntity, out var childMap))
                            childBuffer[partMeta.ParentEntity] =
                                childMap = new Dictionary<int, (int, List<object>)>();

                        childMap[partMeta.InstanceId] = (childPreAllocId, childComps);
                    }
                    else
                    {
                        // ── Root entity ────────────────────────────────────────────
                        long tkbType = 0;
                        if (mask.IsSet(GlobalComponentIds.TkbIdentity))
                            tkbType = stagingRepo.GetComponent<TkbIdentity>(e).TkbType;

                        ulong disType = meta.DisType.Value;

                        newIdByIndex.TryGetValue(i, out int preAllocId);

                        var comps = ExtractEntityComponents(
                            registeredTables, i, in mask, in exclusionMask);

                        entityToRootIdx[e] = rootDataList.Count;
                        rootDataList.Add((e, tkbType, disType, comps, preAllocId));
                    }
                }

                // ── Build requests ─────────────────────────────────────────────────
                var results = new List<EntityCreationRequest>(rootDataList.Count);
                foreach (var rd in rootDataList)
                {
                    Fdp.Core.LoadProgress.Report();   // ⭐ CE-2417 — the extraction advances (docs/DESIGN_Cluster_Load_Phase.md §12)
                    var comps = rd.components;

                    // Remap BehaviorParams and cross-entity network IDs in-place.
                    RemapComponentNetworkIds(comps, behaviorRemapper, refScope, passes);

                    // Append EpisodeTag last when an episode ID is provided.
                    if (episodeId.HasValue)
                        comps.Add(new EpisodeTag { EpisodeId = (Fdp.Core.EpisodeId)(episodeId.Value) });

                    // Attach harvested child component overrides (null when none).
                    IReadOnlyDictionary<int, (int PreAllocatedId, IReadOnlyList<object> Components)>?
                        childOverrides = null;

                    if (childBuffer.TryGetValue(rd.stagingEntity, out var harvested))
                    {
                        var overrideDict = new Dictionary<int, (int, IReadOnlyList<object>)>(harvested.Count);
                        foreach (var kvp in harvested)
                        {
                            RemapComponentNetworkIds(kvp.Value.components, behaviorRemapper, refScope, passes);
                            overrideDict[kvp.Key] = (kvp.Value.preAllocId, kvp.Value.components);
                        }
                        childOverrides = overrideDict;
                    }

                    // ⭐⭐⭐ TRANSLATOR — constructing the DTO directly here is CORRECT, not a bypass of
                    //   EntityCreation.RequestEntityCreation. 📄 DESIGN_Entity_Authoring_Surface.md §2:
                    //   an AUTHOR originates a new intent and must use the affordance; a TRANSLATOR maps
                    //   in an EXISTING representation — here a saved scenario file, which already fixed
                    //   the owner, the components and the ids. ⛔ It also sets PreAllocatedNetworkId and
                    //   ChildComponentOverrides, which the affordance deliberately EXCLUDES (§3): they
                    //   have exactly one producer, and it is this one.
                    results.Add(new EntityCreationRequest
                    {
                        RequestId              = Guid.NewGuid(),
                        OwnerAppInstanceId     = 0,
                        TkbType                = (Fdp.Core.TkbTypeId)(rd.tkbType),
                        DisType                = rd.disType,
                        InitialComponents      = comps,
                        PreAllocatedNetworkId  = rd.preAllocId,
                        ChildComponentOverrides = childOverrides,
                        IsScenarioLoad         = true,   // ⭐ CE-3324 — said, not inferred from the id: the subordinates are in the file
                    });
                }

                // ⭐⭐ BP-509 — hand out Pass 1's translation table now that the extraction SUCCEEDED.
                //    ⛔ A copy, not the live dictionary: the subscriber keeps it for the lifetime of the
                //    load and this one is a local that Pass 2 read from — handing out the instance would
                //    make a future edit to Pass 2 visible to a reader that already published it.
                OnRemap?.Invoke(new Dictionary<Guid, int>(guidToNew));

                return results;
            }
            finally
            {
                // ⭐ CE-2396 — reused: EMPTIED (nothing of this extraction reaches the next, also after a throw); fresh: disposed
                if (reused) { stagingRepo.SoftClear(); System.Threading.Volatile.Write(ref _stagingBusy, 0); }
                else stagingRepo.Dispose();
                StagingRepositoryDisposedCallback?.Invoke();
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Remaps network IDs embedded inside <paramref name="comps"/> in-place.
        /// Patches <see cref="ActiveMissionPlan"/> BehaviorParams via
        /// <paramref name="behaviorRemapper"/> (when non-null) and remaps cross-entity
        /// network IDs in Intent DTO managed components.
        /// </summary>
        private static void RemapComponentNetworkIds(
            List<object> comps,
            ScenarioBehaviorRemapper? behaviorRemapper,
            Fdp.Toolkit.Scenario.ScenarioRefScope refScope,
            ExtractionPasses? passes = null)
        {
            // ⭐ Macros S3a ④ (§10.1) — BIND first: a "^param" leaf / a node's Bind takes the macro's value before anything resolves.
            if (passes?.BeforeResolve is { } bind) Hrot.Map.Common.Scenario.ScenarioEntityWalk.Apply(comps, behaviorRemapper, bind);

            // Remap BehaviorParams network IDs in-place on any ActiveMissionPlan
            // component (intentional mutation — staging repo is transient).
            if (behaviorRemapper != null)
            {
                foreach (var comp in comps)
                {
                    if (comp is ActiveMissionPlan plan)
                    {
                        foreach (var task in plan.Plan.Tasks)
                        {
                            task.BehaviorParams = behaviorRemapper.ResolveJson(task.BehaviorName, task.BehaviorParams, refScope) ?? task.BehaviorParams;
                            // ⭐ CE-4301 (G2) — the task's then / give-up conditions name entities too
                            if (task.Triggers != null)
                                foreach (var t in task.Triggers)
                                    if (t.Type is Fdp.Toolkit.Behavior.Systems.MissionConditionTypes.Then or Fdp.Toolkit.Behavior.Systems.MissionConditionTypes.GiveUp)
                                        t.Params = ScenarioBehaviorRemapper.ResolveConditionJson(t.Params, refScope) ?? t.Params;
                        }
                    }
                    // ⭐ CE-1057 — a unit's saved AI (CE-3042) carries each slot's params as a JSON string too, so the type
                    //   plan below cannot see the EntityRefs inside it; the same by-name JSON pass rewrites them.
                    //   🔴 Without it a scenario's `FireAtTarget {targetNetworkId}` kept the saved id and named nothing.
                    else if (comp is InitialBrainIntent brain)
                    {
                        RemapSlot(brain.Behavior);
                        RemapSlot(brain.Sop);
                    }
                }
            }

            // S9' — HoldUnit is an EntityLink: an authored GUID / @Key resolves through the scope into .Ref; a legacy numeric ref is remapped old->new.
            static Fdp.Toolkit.Replication.EntityLink ResolveHold(Fdp.Toolkit.Replication.EntityLink l, Fdp.Toolkit.Scenario.ScenarioRefScope scope)
            {
                if (!l.Spec.IsNone && scope.TryResolve(l.Spec, out var hit)) return l.WithRef(hit);
                if (!l.Ref.IsNone) throw new InvalidOperationException("A scenario file may not name an entity by network id (S8): " + l.Ref);
                return l;
            }

            static Hrot.Map.Common.Components.RoutePlan RemapHoldUnits(Hrot.Map.Common.Components.RoutePlan route, Fdp.Toolkit.Scenario.ScenarioRefScope scope)
            {
                bool any = false;
                foreach (var w in route.Waypoints) any |= !w.HoldUnit.IsNone && ResolveHold(w.HoldUnit, scope) != w.HoldUnit;
                if (!any) return route;
                // CE-4046 — RoutePlan is an immutable record: the remapped plan is a NEW value the caller puts back in the list.
                return route.WithWaypoints(list =>
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var w = list[i];
                        w.HoldUnit = ResolveHold(w.HoldUnit, scope);
                        list[i] = w;
                    }
                });
            }

            void RemapSlot(SavedBrainSlot? slot)
            {
                if (slot == null) return;
                slot.Params = behaviorRemapper!.ResolveJson(slot.Name, slot.Params, refScope) ?? slot.Params;   // ⭐ S0 — "@Key" / GUID → the answer
            }

            // ⭐ CE-2226 — a route's Pickup waypoint names the unit it waits for (RouteWaypoint.HoldUnit, a network id). The type plan
            //   below cannot reach it (RoutePlan.Waypoints is a immutable record), so it is rewritten here, like the
            //   brain params above. 🔴 Measured live on ua-air-rescue: the hold kept the saved id, named nothing, and the UH-60 waited
            //   at the LZ with every man aboard until the casualty it had picked up died.
            for (int ri = 0; ri < comps.Count; ri++)
                if (comps[ri] is Hrot.Map.Common.Components.RoutePlan route) comps[ri] = RemapHoldUnits(route, refScope);

            // ⭐⭐ DESIGN_Entity_Reference.md D4 — every EntityRef inside every component (the Initial*Intent DTOs the
            // scenario translators write, and any other component holding one) is rewritten by the ONE type plan, in place
            // (a boxed struct in its box). The staging repository is transient, so mutating its components is intended.
            // 🔴 This replaced six hand-coded per-intent cases: a new intent was silently left with staging ids, and the
            // TargetEntry copy had dropped PosZ (P3D-206) on every scenario load.


            // ⭐ S0 item 3 — then every AUTHORED reference in a component (an EntityLink / PointLink / AreaLink) is resolved in this
            //   scenario, by the same walker. ⛔ A key that names nothing throws: the load fails loudly, naming the key.
            var resolve = new RefResolve(refScope);
            for (int ci = 0; ci < comps.Count; ci++)
                EntityRefRemap.WalkObject(comps[ci], resolve);

            // ⭐ Macros S3a ④ (§10.1) — RELOCATE last: what came from a reference is already an answer and stays (SR-131).
            if (passes?.AfterResolve is { } relocate) Hrot.Map.Common.Scenario.ScenarioEntityWalk.Apply(comps, behaviorRemapper, relocate);
        }

        /// <summary>⭐ S0 — this load's <see cref="Fdp.Toolkit.Scenario.ScenarioRefScope"/>: every staging entity with a network id, by its
        /// <see cref="Fdp.Toolkit.Scenario.RefKey"/> and <see cref="Fdp.Toolkit.Scenario.EntityGuid"/>, answering with its NEW id and its position.</summary>
        private static Fdp.Toolkit.Scenario.ScenarioRefScope BuildRefScope(EntityRepository stagingRepo, int maxIdx, Dictionary<int, int> newIdByIndex)
        {
            var scope = new Fdp.Toolkit.Scenario.ScenarioRefScope("this scenario");
            for (int i = 0; i <= maxIdx; i++)
            {
                if (!newIdByIndex.TryGetValue(i, out int newId)) continue;
                var mask = stagingRepo.GetEntityIndex().GetComponentMask(i);
                var e = stagingRepo.GetEntityByIndex(i);
                if (e == Entity.Null) continue;
                string? key = mask.IsSet(GlobalComponentIds.RefKey) ? stagingRepo.GetComponent<Fdp.Toolkit.Scenario.RefKey>(e).Value.ToString() : null;
                Guid guid = stagingRepo.GetComponent<Fdp.Toolkit.Scenario.EntityGuid>(e).Value;
                WorldPoint? at = mask.IsSet(GlobalComponentIds.SimTransform) ? stagingRepo.GetComponent<SimTransform>(e).Position : null;
                scope.Add(new EntityRef(newId), key, guid, at);
            }
            return scope;
        }

        /// <summary>
        /// Extracts all non-excluded components for the entity at <paramref name="entityIndex"/>
        /// from the registered component tables.
        /// </summary>
        private static List<object> ExtractEntityComponents(
            IReadOnlyDictionary<Type, IComponentTable> tables,
            int entityIndex,
            in BitMask512 componentMask,
            in BitMask512 exclusionMask)
        {
            var result = new List<object>();
            foreach (var kvp in tables)
            {
                var table  = kvp.Value;
                int typeId = table.ComponentTypeId;

                if (exclusionMask.IsSet(typeId)) continue;
                if (!componentMask.IsSet(typeId)) continue;

                var comp = table.GetRawObject(entityIndex);
                if (comp != null)
                    result.Add(comp);
            }
            return result;
        }
    }
}
