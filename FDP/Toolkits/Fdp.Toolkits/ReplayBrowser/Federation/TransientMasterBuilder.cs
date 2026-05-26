using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;

namespace Fdp.Toolkit.ReplayBrowser.Federation
{
    /// <summary>
    /// Builds a transient merged <see cref="EntityRepository"/> from all nodes loaded in a
    /// <see cref="FederatedReplayManager"/>.  Each call to <see cref="Build"/> produces an
    /// independent repository reflecting the current playback state of every node.
    /// </summary>
    /// <remarks>
    /// The builder is stateless beyond the injected serializer; it can be called multiple
    /// times on the same manager to produce refreshed snapshots.
    /// </remarks>
    public sealed class TransientMasterBuilder
    {
        private readonly ScenarioSerializer _serializer;

        /// <summary>
        /// Creates a <see cref="TransientMasterBuilder"/> that uses <paramref name="serializer"/>
        /// for component serialization and deserialization.
        /// </summary>
        public TransientMasterBuilder(ScenarioSerializer serializer)
        {
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        /// <summary>
        /// Merges the current playback state of all nodes in <paramref name="manager"/> into a
        /// single transient <see cref="EntityRepository"/> and returns it.
        /// </summary>
        /// <remarks>
        /// Each call allocates a fresh repository; the caller is responsible for disposing it.
        /// The algorithm follows DESIGN §7.3 (consensus extraction) and §7.8 (local-entities
        /// provider injection).
        /// </remarks>
        public EntityRepository Build(FederatedReplayManager manager)
        {
            if (manager == null) throw new ArgumentNullException(nameof(manager));

            // ── Step 1: Allocate and prime transient repo ────────────────────────────
            var transientRepo = new EntityRepository();
            // Populate the transient repo's component tables for every discovered type.
            // The AutoSerializer delegates were compiled at ScenarioSerializerBuilder.Build()
            // time; we do NOT rebuild them here to avoid touching types that are valid at
            // the call site but may be absent from the serializer's registry.
            RepositoryPriming.RegisterDiscoveredComponents(transientRepo);

            // ── Step 2: Correlate entities by NetworkIdentity.Value ─────────────────
            int netIdTypeId = ComponentTypeRegistry.GetId(typeof(NetworkIdentity));
            var correlation = new Dictionary<long, List<(int nodeId, Entity entity)>>();

            if (netIdTypeId >= 0)
            {
                foreach (var kvp in manager.Contexts)
                {
                    int nodeId = kvp.Key;
                    var repo = kvp.Value.SandboxRepo;
                    for (int i = 0; i <= repo.MaxEntityIndex; i++)
                    {
                        var e = new Entity(i, repo.GetMetadata(i).Generation);
                        if (!repo.IsAlive(e)) continue;
                        if (!repo.GetComponentMask(i).IsSet(netIdTypeId)) continue;
                        long netVal = repo.GetComponent<NetworkIdentity>(e).Value;
                        if (!correlation.TryGetValue(netVal, out var list))
                            correlation[netVal] = list = new List<(int, Entity)>();
                        list.Add((nodeId, e));
                    }
                }
            }

            // ── Step 3: Pre-allocate global entities in transient repo + load map ───
            var resolver = new FederatedGuidResolver();
            var preAllocated = new Dictionary<string, Entity>(StringComparer.Ordinal);
            var loadMap = new Dictionary<string, Entity>(StringComparer.Ordinal);

            foreach (var kvp in correlation)
            {
                var key = NetworkIdGuid.From(kvp.Key).ToString("N");
                var transientEntity = transientRepo.CreateEntity();
                preAllocated[key] = transientEntity;
                loadMap[key] = transientEntity;
            }

            // ── Step 3b (P3T7): Pre-allocate local entities from provider ───────────
            int providerNodeId = manager.LocalEntitiesProviderNodeId;
            // Maps each provider-repo Entity to its synthetic Guid key.
            var localEntityKeys = new Dictionary<Entity, string>();

            if (manager.Contexts.TryGetValue(providerNodeId, out var providerCtxPreAlloc))
            {
                var providerRepo = providerCtxPreAlloc.SandboxRepo;
                for (int i = 0; i <= providerRepo.MaxEntityIndex; i++)
                {
                    var e = new Entity(i, providerRepo.GetMetadata(i).Generation);
                    if (!providerRepo.IsAlive(e)) continue;
                    // Skip global entities (have NetworkIdentity)
                    if (netIdTypeId >= 0 && providerRepo.GetComponentMask(i).IsSet(netIdTypeId)) continue;

                    ushort gen = providerRepo.GetMetadata(i).Generation;
                    var syntheticKey = MakeSyntheticKey(providerNodeId, i, gen);
                    var transientEntity = transientRepo.CreateEntity();
                    preAllocated[syntheticKey] = transientEntity;
                    loadMap[syntheticKey] = transientEntity;
                    localEntityKeys[e] = syntheticKey;
                }
            }

            resolver.SetLoadMap(loadMap);

            // ── Step 4: Build master DOM envelope ───────────────────────────────────
            var entitiesNode = new JsonObject();
            var masterDom = new JsonObject
            {
                ["Header"] = new JsonObject
                {
                    ["SubsystemType"] = JsonValue.Create(_serializer.SubsystemType),
                    ["SchemaVersion"]  = JsonValue.Create(1)
                },
                ["Entities"] = entitiesNode
            };

            // ── Step 5: §7.3 consensus extraction per global entity ──────────────────
            int netAuthTypeId = ComponentTypeRegistry.GetId(typeof(NetworkAuthority));

            foreach (var corrKvp in correlation)
            {
                long netVal = corrKvp.Key;
                var nodeEntities = corrKvp.Value;

                var entityKey = NetworkIdGuid.From(netVal).ToString("N");
                var mergedEntityNode = new JsonObject();
                var alreadyClaimed = new BitMask512();

                // Determine primary owner from NetworkAuthority
                int primaryOwner = -1;
                if (netAuthTypeId >= 0)
                {
                    foreach (var (nid, ent) in nodeEntities)
                    {
                        var repo = manager.Contexts[nid].SandboxRepo;
                        if (!repo.GetComponentMask(ent.Index).IsSet(netAuthTypeId)) continue;
                        primaryOwner = repo.GetComponent<NetworkAuthority>(ent).PrimaryOwnerId;
                        break;
                    }
                }

                // Order: primary-owner node first, then ascending NodeId
                var ordered = new List<(int nodeId, Entity entity)>(nodeEntities);
                ordered.Sort((a, b) =>
                {
                    bool aIsPrimary = (a.nodeId == primaryOwner);
                    bool bIsPrimary = (b.nodeId == primaryOwner);
                    if (aIsPrimary && !bIsPrimary) return -1;
                    if (bIsPrimary && !aIsPrimary) return 1;
                    return a.nodeId.CompareTo(b.nodeId);
                });

                foreach (var (nid, localEntity) in ordered)
                {
                    var localRepo = manager.Contexts[nid].SandboxRepo;

                    // Build save-map for this node (global entities only)
                    var saveMap = BuildSaveMapForNode(nid, manager, correlation);
                    // If this is the provider node, extend save-map with local entities (P3T7)
                    if (nid == providerNodeId)
                    {
                        foreach (var localKvp in localEntityKeys)
                            saveMap[localKvp.Key] = localKvp.Value;
                    }
                    resolver.SetSaveMap(saveMap);

                    // §7.3 consensus: presence & authority & ~alreadyClaimed
                    var presenceMask  = localRepo.GetComponentMask(localEntity.Index);   // struct copy
                    var authorityMask = localRepo.GetMetadata(localEntity.Index).AuthorityMask; // struct copy
                    var candidate = presenceMask;
                    candidate.BitwiseAnd(authorityMask);
                    var extract = candidate;
                    extract.BitwiseAndNot(alreadyClaimed);
                    alreadyClaimed.BitwiseOr(extract);

                    if (extract.IsEmpty()) continue;

                    var fragment = _serializer.SerializeEntity(localRepo, localEntity, resolver, extract);

                    // Deep-clone each value to avoid re-parenting violations in JsonNode.
                    foreach (var fragKvp in fragment)
                        mergedEntityNode[fragKvp.Key] = fragKvp.Value?.DeepClone();
                }

                entitiesNode[entityKey] = mergedEntityNode;
            }

            // ── Step 5b (P3T7): Extract local entities from provider ─────────────────
            if (localEntityKeys.Count > 0 &&
                manager.Contexts.TryGetValue(providerNodeId, out var provCtxExtract))
            {
                var providerRepo = provCtxExtract.SandboxRepo;

                // Build provider save-map (global + local entities)
                var providerSaveMap = BuildSaveMapForNode(providerNodeId, manager, correlation);
                foreach (var localKvp in localEntityKeys)
                    providerSaveMap[localKvp.Key] = localKvp.Value;
                resolver.SetSaveMap(providerSaveMap);

                // Extract each local entity using the full presence mask (NOT AuthorityMask).
                foreach (var localKvp in localEntityKeys)
                {
                    var localEntity  = localKvp.Key;
                    var syntheticKey = localKvp.Value;
                    var fullMask = providerRepo.GetComponentMask(localEntity.Index); // struct copy
                    var fragment = _serializer.SerializeEntity(providerRepo, localEntity, resolver, fullMask);
                    entitiesNode[syntheticKey] = fragment;
                }
            }

            // ── Step 6: Deserialize master DOM into transient repo ───────────────────
            _serializer.DeserializeWith(transientRepo, masterDom, resolver, preAllocated);

            return transientRepo;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a save-map for the given node containing only the global (NetworkIdentity)
        /// entities in that node.  Local-entity entries are added by the caller if needed.
        /// </summary>
        private static Dictionary<Entity, string> BuildSaveMapForNode(
            int nodeId,
            FederatedReplayManager manager,
            Dictionary<long, List<(int nodeId, Entity entity)>> correlation)
        {
            var saveMap = new Dictionary<Entity, string>();
            foreach (var kvp in correlation)
            {
                long netVal = kvp.Key;
                foreach (var (nid, ent) in kvp.Value)
                {
                    if (nid != nodeId) continue;
                    saveMap[ent] = NetworkIdGuid.From(netVal).ToString("N");
                }
            }
            return saveMap;
        }

        /// <summary>
        /// Generates a deterministic, Guid-shaped key for a local (non-networked) entity.
        /// The key is stable across multiple <see cref="Build"/> calls as long as the entity
        /// index and generation remain the same.
        /// </summary>
        internal static string MakeSyntheticKey(int providerNodeId, int entityIndex, ushort generation)
        {
            // Stable human-readable prefix for debug dumps; hash into a Guid.
            var src = $"LOCAL_NODE_{providerNodeId}_ENT_{entityIndex}_G_{generation}";
            byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(src));
            return new Guid(hash).ToString("N");
        }
    }
}
