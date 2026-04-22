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

namespace Hrot.CGF.Orchestration
{
    /// <summary>
    /// Two-pass entity extraction engine that converts scenario JSON into
    /// <see cref="EntityCreationRequest"/> objects ready for the genesis pipeline.
    ///
    /// <para>
    /// <b>Pass 1 – ID allocation:</b> pre-allocate new network IDs for every entity
    /// that carries a <c>NetworkIdentity</c> component.  Records the old-to-new mapping
    /// so that network IDs embedded in doctrine <c>BehaviorParams</c> JSON strings can be
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
    public sealed class StagingEntityExtractor
    {
        // ── Static exclusion mask ──────────────────────────────────────────────────
        // Built once at class init using GlobalComponentIds named constants.
        // At extraction time the instance mask is built by copying this mask and
        // OR-ing in any translator-consumed component masks from the provided
        // ScenarioSerializer (Decision 10).

        private static readonly BitMask256 s_staticExclusionMask = BuildStaticMask();

        private static BitMask256 BuildStaticMask()
        {
            var mask = new BitMask256();
            mask.SetBit(GlobalComponentIds.LifecycleDescriptor);  // 5
            mask.SetBit(GlobalComponentIds.NetworkIdentity);       // 50
            mask.SetBit(GlobalComponentIds.NetworkAuthority);      // 51
            mask.SetBit(GlobalComponentIds.DescriptorOwnership);   // 59
            mask.SetBit(GlobalComponentIds.TkbIdentity);           // 65
            mask.SetBit(GlobalComponentIds.GhostStateTracker);     // 66
            mask.SetBit(GlobalComponentIds.NetworkOwnership);      // 140
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
            ScenarioBehaviorRemapper? behaviorRemapper = null)
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
                exclusionMask.BitwiseOr(translator.GetConsumedComponentsMask());

            // Additional child-extraction mask: also excludes PartMetadata itself
            // (its ParentEntity field is a volatile ECS handle valid only within
            // the staging repo — Decision 5).
            var childExclusionMask = exclusionMask;
            childExclusionMask.SetBit(GlobalComponentIds.PartMetadata);

            var stagingRepo = new EntityRepository();
            try
            {
                // Pre-register all globally known component types so that
                // FdpAutoSerializer.TryInject can call SetComponent<T> without
                // encountering "component not registered" exceptions.
                RegisterAllGlobalTypesInRepo(stagingRepo);

                // Hydrate the staging repository from the scenario JSON.
                serializer.Deserialize(stagingRepo, json);

                var registeredTables = stagingRepo.GetRegisteredComponentTypes();
                int maxIdx = stagingRepo.MaxEntityIndex;

                // ── Pass 1: ID allocation ──────────────────────────────────────────
                // Pre-allocate new network IDs for every entity (root and child) that
                // carries a NetworkIdentity in the staging DOM.  Build the translation
                // map so doctrine BehaviorParams can be patched in Pass 2.
                var oldToNewMap = new Dictionary<long, long>();
                for (int i = 0; i <= maxIdx; i++)
                {
                    ref var hdr = ref stagingRepo.GetHeader(i);
                    if (!hdr.IsActive) continue;
                    if (!hdr.ComponentMask.IsSet(GlobalComponentIds.NetworkIdentity)) continue;

                    var e = stagingRepo.GetEntityByIndex(i);
                    if (e == Entity.Null) continue;

                    long oldId = stagingRepo.GetComponent<NetworkIdentity>(e).Value;
                    long newId = idAllocator.AllocateId();
                    oldToNewMap[oldId] = newId;
                }

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
                    long   preAllocId)>();

                var entityToRootIdx = new Dictionary<Entity, int>();

                // Child buffer keyed by parentEntity (staging handle) then by InstanceId.
                var childBuffer = new Dictionary<Entity,
                    Dictionary<int, (long preAllocId, List<object> components)>>();

                for (int i = 0; i <= maxIdx; i++)
                {
                    ref var hdr = ref stagingRepo.GetHeader(i);
                    if (!hdr.IsActive) continue;

                    var e = stagingRepo.GetEntityByIndex(i);
                    if (e == Entity.Null) continue;

                    if (hdr.ComponentMask.IsSet(GlobalComponentIds.PartMetadata))
                    {
                        // ── Child entity ───────────────────────────────────────────
                        var partMeta = stagingRepo.GetComponent<PartMetadata>(e);

                        long childPreAllocId = 0;
                        if (hdr.ComponentMask.IsSet(GlobalComponentIds.NetworkIdentity))
                        {
                            long childOldId = stagingRepo.GetComponent<NetworkIdentity>(e).Value;
                            oldToNewMap.TryGetValue(childOldId, out childPreAllocId);
                        }

                        var childComps = ExtractEntityComponents(
                            registeredTables, i, in hdr.ComponentMask, in childExclusionMask);

                        if (!childBuffer.TryGetValue(partMeta.ParentEntity, out var childMap))
                            childBuffer[partMeta.ParentEntity] =
                                childMap = new Dictionary<int, (long, List<object>)>();

                        childMap[partMeta.InstanceId] = (childPreAllocId, childComps);
                    }
                    else
                    {
                        // ── Root entity ────────────────────────────────────────────
                        long tkbType = 0;
                        if (hdr.ComponentMask.IsSet(GlobalComponentIds.TkbIdentity))
                            tkbType = stagingRepo.GetComponent<TkbIdentity>(e).TkbType;

                        ulong disType = hdr.DisType.Value;

                        long preAllocId = 0;
                        if (hdr.ComponentMask.IsSet(GlobalComponentIds.NetworkIdentity))
                        {
                            long oldId = stagingRepo.GetComponent<NetworkIdentity>(e).Value;
                            oldToNewMap.TryGetValue(oldId, out preAllocId);
                        }

                        var comps = ExtractEntityComponents(
                            registeredTables, i, in hdr.ComponentMask, in exclusionMask);

                        entityToRootIdx[e] = rootDataList.Count;
                        rootDataList.Add((e, tkbType, disType, comps, preAllocId));
                    }
                }

                // ── Build requests ─────────────────────────────────────────────────
                var results = new List<EntityCreationRequest>(rootDataList.Count);
                foreach (var rd in rootDataList)
                {
                    var comps = rd.components;

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
                                    task.BehaviorParams =
                                        behaviorRemapper.RemapJson(
                                            task.BehaviorId,
                                            task.BehaviorParams,
                                            oldToNewMap)
                                        ?? task.BehaviorParams;
                                }
                            }
                        }
                    }

                    // Remap cross-entity Network IDs embedded in Intent DTO managed components.
                    // Intent DTOs are written by scenario translators during Inject; the network IDs
                    // they contain refer to the old staging allocations and must be patched to the
                    // new IDs allocated in Pass 1 before the requests are dispatched.
                    for (int ci = 0; ci < comps.Count; ci++)
                    {
                        if (comps[ci] is InitialPassengersIntent pIntent)
                        {
                            var remapped = new InitialPassengersIntent();
                            foreach (var id in pIntent.PassengerNetworkIds)
                                remapped.PassengerNetworkIds.Add(
                                    oldToNewMap.TryGetValue(id, out long newPsId) ? newPsId : id);
                            comps[ci] = remapped;
                        }
                        else if (comps[ci] is InitialVehicleIntent vIntent)
                        {
                            comps[ci] = new InitialVehicleIntent
                            {
                                VehicleNetworkId = oldToNewMap.TryGetValue(
                                    vIntent.VehicleNetworkId, out long newVId) ? newVId : vIntent.VehicleNetworkId,
                            };
                        }
                        else if (comps[ci] is InitialHierarchyIntent hIntent)
                        {
                            comps[ci] = new InitialHierarchyIntent
                            {
                                ParentNetworkId      = oldToNewMap.TryGetValue(
                                    hIntent.ParentNetworkId,      out long newParId) ? newParId : hIntent.ParentNetworkId,
                                FirstChildNetworkId  = oldToNewMap.TryGetValue(
                                    hIntent.FirstChildNetworkId,  out long newFcId)  ? newFcId  : hIntent.FirstChildNetworkId,
                                NextSiblingNetworkId = oldToNewMap.TryGetValue(
                                    hIntent.NextSiblingNetworkId, out long newNsId)  ? newNsId  : hIntent.NextSiblingNetworkId,
                            };
                        }
                        else if (comps[ci] is InitialRouteIntent rIntent)
                        {
                            comps[ci] = new InitialRouteIntent
                            {
                                RouteNetworkId = oldToNewMap.TryGetValue(
                                    rIntent.RouteNetworkId, out long newRId) ? newRId : rIntent.RouteNetworkId,
                            };
                        }
                        else if (comps[ci] is InitialTargetsIntent tIntent)
                        {
                            var remappedIntent = new InitialTargetsIntent();
                            foreach (var entry in tIntent.Entries)
                            {
                                remappedIntent.Entries.Add(new TargetEntry
                                {
                                    NetworkId    = oldToNewMap.TryGetValue(entry.NetworkId, out long newTId) ? newTId : entry.NetworkId,
                                    PosX         = entry.PosX,
                                    PosY         = entry.PosY,
                                    Score        = entry.Score,
                                    LastSeenTick = entry.LastSeenTick,
                                    Modality     = entry.Modality,
                                });
                            }
                            comps[ci] = remappedIntent;
                        }
                    }

                    // Append EpisodeTag last when an episode ID is provided.
                    if (episodeId.HasValue)
                        comps.Add(new EpisodeTag { EpisodeId = episodeId.Value });

                    // Attach harvested child component overrides (null when none).
                    IReadOnlyDictionary<int, (long PreAllocatedId, IReadOnlyList<object> Components)>?
                        childOverrides = null;

                    if (childBuffer.TryGetValue(rd.stagingEntity, out var harvested))
                    {
                        var overrideDict = new Dictionary<int, (long, IReadOnlyList<object>)>(harvested.Count);
                        foreach (var kvp in harvested)
                            overrideDict[kvp.Key] = (kvp.Value.preAllocId, kvp.Value.components);
                        childOverrides = overrideDict;
                    }

                    results.Add(new EntityCreationRequest
                    {
                        RequestId              = Guid.NewGuid(),
                        OwnerAppInstanceId     = 0,
                        TkbType                = rd.tkbType,
                        DisType                = rd.disType,
                        InitialComponents      = comps,
                        PreAllocatedNetworkId  = rd.preAllocId,
                        ChildComponentOverrides = childOverrides,
                    });
                }

                return results;
            }
            finally
            {
                stagingRepo.Dispose();
                StagingRepositoryDisposedCallback?.Invoke();
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Extracts all non-excluded components for the entity at <paramref name="entityIndex"/>
        /// from the registered component tables.
        /// </summary>
        private static List<object> ExtractEntityComponents(
            IReadOnlyDictionary<Type, IComponentTable> tables,
            int entityIndex,
            in BitMask256 componentMask,
            in BitMask256 exclusionMask)
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
