using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.ReplayBrowser.Federation
{
    /// <summary>A reference the merge could not resolve (design §7.6, the paradox rule): <c>Source</c> in node <c>NodeId</c>'s sandbox is held by <c>Component</c> of the entity with network id <c>NetId</c> (-1: a local entity) and names no entity of the merged world — it became <see cref="Entity.Null"/>.</summary>
    public readonly record struct MergeParadox(long NetId, int NodeId, Type Component, Entity Source);

    /// <summary>
    /// ⭐ G-7 / CE-4354 (docs/UX/DESIGN_Review_Perspective.md §6, §6.12 S1) — the ONE merge of a federated group, BINARY: for every networked entity, each node's
    /// component values are COPIED (unmanaged by value, raw <see cref="Entity"/> fields remapped through <see cref="EntityRemapPlan"/>; managed by reference —
    /// M5 — or as a NEW remapped instance when the type holds a raw <see cref="Entity"/>, <see cref="ManagedEntityRemap"/>) into the target's OWN entities, which a
    /// <see cref="MergeTarget"/> keeps across calls. It replaces the JSON round trip of <see cref="TransientMasterBuilder"/> (kept as the test ORACLE) and ends the
    /// per-step new repository: both hosts (Replay Browser, the editor's Review source) call <see cref="Merge"/> where they used to call the builder.
    /// <list type="bullet">
    /// <item>who supplies a component: <see cref="OwnershipSource"/> (S1: today's rule — owned AND not already taken, primary owner first);</item>
    /// <item>skipped: types with <see cref="DataPolicy.NodeLocal"/> (I9) and types that are not recorded (<see cref="DataPolicy.NoReplay"/> / <c>Transient</c>);</item>
    /// <item>local (non-networked) entities: from the provider node (<c>LocalEntitiesProviderNodeId</c>), every component it holds, keyed (node, index, generation) — S5 widens this to every node;</item>
    /// <item>a component no source supplies any more is REMOVED from the target entity; an entity gone from every node is destroyed.</item>
    /// </list>
    /// <para>⛔ §6.10 holds: never a raw chunk or <c>EntityIndex</c> copy — every value goes entity by entity into the target's own entities. S1 is a FULL pass per call (it
    /// may allocate); S4 makes it incremental and 0 B. Singletons (world-level components) are NOT merged: they are per-node services and indices (the old
    /// builder never touched them either).</para>
    /// </summary>
    public sealed unsafe class BinaryMerger
    {
        private sealed class TypePlan
        {
            public Type Type = null!;
            public bool Skip, Managed;
            public int Size;
            public EntityRemapPlan? Remap;                                     // unmanaged with raw Entity fields
            public Func<object, Func<Entity, Entity>, object>? ManagedRemap;   // managed with raw Entity fields
        }

        private static readonly MethodInfo RegisterComponent =
            typeof(EntityRepository).GetMethod(nameof(EntityRepository.RegisterComponent))!;

        private readonly MergeTarget _target = new();
        private readonly OwnershipSource _ownership;
        private readonly Dictionary<long, List<NodeEntity>> _corr = new();
        private readonly Stack<List<NodeEntity>> _listPool = new();
        private readonly Dictionary<int, Dictionary<Entity, Entity>> _nodeMaps = new();
        private readonly Dictionary<int, Func<Entity, Entity>> _resolvers = new();
        private readonly List<(Entity source, Entity target)> _locals = new();
        private readonly List<BitMask512> _extracts = new();
        private readonly List<MergeParadox> _paradoxes = new();
        private readonly HashSet<int> _registered = new();
        private TypePlan?[] _plans = new TypePlan?[512];
        private int _planGeneration = -1;
        private byte[] _scratch = GC.AllocateArray<byte>(256, pinned: true);

        private long _curNet;
        private Type? _curType;

        /// <param name="ownership">Who supplies each component; default <see cref="OwnershipSource"/> (today's rule).</param>
        public BinaryMerger(OwnershipSource? ownership = null) => _ownership = ownership ?? new OwnershipSource();

        /// <summary>The map this merger keeps (network id → target entity).</summary>
        public MergeTarget Target => _target;

        /// <summary>References of the LAST <see cref="Merge"/> that named no entity of the merged world (each became <see cref="Entity.Null"/>).</summary>
        public IReadOnlyList<MergeParadox> Paradoxes => _paradoxes;

        /// <summary>Components written by the last <see cref="Merge"/>.</summary>
        public int LastComponentsCopied { get; private set; }

        /// <summary>The target entity of network id <paramref name="netId"/> after the last merge.</summary>
        public bool TryGetEntity(long netId, out Entity entity) => _target.TryGet(netId, out entity);

        /// <summary>
        /// A world boundary: the target was cleared (or a new one is about to be bound) — forget every mapping so the next <see cref="Merge"/> creates afresh.
        /// ⚠ Destroys nothing: the host owns the world and clears it.
        /// </summary>
        public void Reset()
        {
            _target.Reset();
            _registered.Clear();
            _paradoxes.Clear();
        }

        /// <summary>Merges the current playback state of every node of <paramref name="manager"/> into <paramref name="target"/> (a full pass).</summary>
        public void Merge(FederatedReplayManager manager, EntityRepository target)
        {
            if (manager == null) throw new ArgumentNullException(nameof(manager));
            if (target == null) throw new ArgumentNullException(nameof(target));

            if (_planGeneration != ComponentTypeRegistry.Generation)
            {
                Array.Clear(_plans);
                _planGeneration = ComponentTypeRegistry.Generation;
            }
            if (!ReferenceEquals(_target.Repo, target)) _registered.Clear();
            _target.Begin(target);
            _paradoxes.Clear();
            LastComponentsCopied = 0;

            // ① correlate by NetworkIdentity (TransientMasterBuilder step 2)
            int netIdTypeId = ComponentTypeRegistry.GetId(typeof(NetworkIdentity));
            foreach (var l in _corr.Values) { l.Clear(); _listPool.Push(l); }
            _corr.Clear();
            foreach (var m in _nodeMaps.Values) m.Clear();

            foreach (var kvp in manager.Contexts)
            {
                int nodeId = kvp.Key;
                var repo = kvp.Value.SandboxRepo;
                if (!_nodeMaps.ContainsKey(nodeId)) _nodeMaps[nodeId] = new Dictionary<Entity, Entity>();
                if (netIdTypeId < 0) continue;
                for (int i = 0; i <= repo.MaxEntityIndex; i++)
                {
                    var e = new Entity(i, repo.GetMetadata(i).Generation);
                    if (!repo.IsAlive(e)) continue;
                    if (!repo.GetComponentMask(i).IsSet(netIdTypeId)) continue;
                    long netVal = repo.GetComponent<NetworkIdentity>(e).Value;
                    if (!_corr.TryGetValue(netVal, out var list))
                        _corr[netVal] = list = _listPool.Count > 0 ? _listPool.Pop() : new List<NodeEntity>();
                    list.Add(new NodeEntity(nodeId, e));
                }
            }

            // ② one target entity per network id (created on first sight) …
            foreach (var kv in _corr)
            {
                var te = _target.EnsureNetworked(kv.Key);
                foreach (var ne in kv.Value) _nodeMaps[ne.NodeId][ne.Entity] = te;
            }

            // … and per local entity of the provider node (TransientMasterBuilder step 3b)
            int providerNodeId = manager.LocalEntitiesProviderNodeId;
            _locals.Clear();
            EntityRepository? providerRepo = null;
            if (manager.Contexts.TryGetValue(providerNodeId, out var providerCtx))
            {
                providerRepo = providerCtx.SandboxRepo;
                for (int i = 0; i <= providerRepo.MaxEntityIndex; i++)
                {
                    var e = new Entity(i, providerRepo.GetMetadata(i).Generation);
                    if (!providerRepo.IsAlive(e)) continue;
                    if (netIdTypeId >= 0 && providerRepo.GetComponentMask(i).IsSet(netIdTypeId)) continue;   // a networked entity
                    var te = _target.EnsureLocal(providerNodeId, e);
                    _nodeMaps[providerNodeId][e] = te;
                    _locals.Add((e, te));
                }
            }

            // ③ gone from every node ⇒ destroyed
            _target.Sweep();

            // ④ the components
            foreach (var kv in _corr)
            {
                _target.TryGet(kv.Key, out var te);
                _curNet = kv.Key;
                _ownership.Claim(manager, kv.Value, _extracts);
                var supplied = new BitMask512();
                for (int n = 0; n < kv.Value.Count; n++)
                    CopyComponents(manager, kv.Value[n], te, _extracts[n], target, ref supplied);
                RemoveUnsupplied(target, te, in supplied);
            }

            if (providerRepo != null)
            {
                _curNet = -1;
                foreach (var (source, te) in _locals)
                {
                    var all = providerRepo.GetComponentMask(source.Index);   // every component it holds (not the authority mask)
                    var supplied = new BitMask512();
                    CopyComponents(manager, new NodeEntity(providerNodeId, source), te, all, target, ref supplied);
                    RemoveUnsupplied(target, te, in supplied);
                }
            }
        }

        // ── one source entity's share ───────────────────────────────────────────────────────

        private void CopyComponents(FederatedReplayManager manager, NodeEntity src, Entity te, BitMask512 take, EntityRepository target, ref BitMask512 supplied)
        {
            var srcRepo = manager.Contexts[src.NodeId].SandboxRepo;
            var resolve = ResolverOf(src.NodeId);
            for (int w = 0; w < 8; w++)
            {
                ulong bits = take.Word(w);
                while (bits != 0)
                {
                    int typeId = w * 64 + BitOperations.TrailingZeroCount(bits);
                    bits &= bits - 1;
                    var plan = PlanOf(typeId);
                    if (plan.Skip) continue;
                    EnsureTable(target, typeId, plan);
                    _curType = plan.Type;

                    if (plan.Managed)
                    {
                        object? obj = srcRepo.GetManagedComponentByTypeId(src.Entity, typeId);
                        if (obj == null) continue;
                        if (plan.ManagedRemap != null) obj = plan.ManagedRemap(obj, resolve);
                        else if (target.GetComponentMask(te.Index).IsSet(typeId) && ReferenceEquals(target.GetManagedComponentByTypeId(te, typeId), obj))
                        {
                            supplied.SetBit(typeId); LastComponentsCopied++; continue;   // M5: the same object, already there
                        }
                        target.SetManagedComponentRaw(te, typeId, obj);
                    }
                    else
                    {
                        void* p = srcRepo.GetComponentPointer(src.Entity, typeId);
                        if (plan.Remap == null)
                            target.SetComponentRaw(te, typeId, (IntPtr)p, plan.Size);
                        else
                        {
                            if (_scratch.Length < plan.Size) _scratch = GC.AllocateArray<byte>(plan.Size * 2, pinned: true);
                            fixed (byte* s = _scratch)
                            {
                                Buffer.MemoryCopy(p, s, plan.Size, plan.Size);
                                plan.Remap.RemapAt(s, resolve);
                                target.SetComponentRaw(te, typeId, (IntPtr)s, plan.Size);
                            }
                        }
                    }
                    supplied.SetBit(typeId);
                    LastComponentsCopied++;
                }
            }
        }

        private void RemoveUnsupplied(EntityRepository target, Entity te, in BitMask512 supplied)
        {
            var held = target.GetComponentMask(te.Index);   // struct copy
            held.BitwiseAndNot(supplied);
            for (int w = 0; w < 8; w++)
            {
                ulong bits = held.Word(w);
                while (bits != 0)
                {
                    int typeId = w * 64 + BitOperations.TrailingZeroCount(bits);
                    bits &= bits - 1;
                    var plan = PlanOf(typeId);
                    if (plan.Managed) target.RemoveManagedComponentRaw(te, typeId);
                    else target.RemoveComponentRaw(te, typeId);
                }
            }
        }

        // ── the remap: source entity of node N → target entity ──────────────────────────────

        private Func<Entity, Entity> ResolverOf(int nodeId)
        {
            if (_resolvers.TryGetValue(nodeId, out var f)) return f;
            f = e => Resolve(nodeId, e);
            _resolvers[nodeId] = f;
            return f;
        }

        private Entity Resolve(int nodeId, Entity e)
        {
            if (e.IsNull) return Entity.Null;
            if (_nodeMaps.TryGetValue(nodeId, out var map) && map.TryGetValue(e, out var t)) return t;
            // the paradox rule (design §7.6): a reference whose target is not in the merged world becomes Entity.Null AND is flagged — never thrown, never hidden
            _paradoxes.Add(new MergeParadox(_curNet, nodeId, _curType!, e));
            return Entity.Null;
        }

        // ── per-type plans, built once ──────────────────────────────────────────────────────

        private TypePlan PlanOf(int typeId)
        {
            var plan = _plans[typeId];
            if (plan != null) return plan;
            var type = ComponentTypeRegistry.GetType(typeId);
            plan = new TypePlan { Type = type! };
            if (type == null || DataPolicyAttribute.IsNodeLocal(type) || !ComponentTypeRegistry.IsRecordable(typeId))
                plan.Skip = true;
            else if (!type.IsValueType)
            {
                plan.Managed = true;
                if (ManagedEntityRemap.TryGet(type, out var mr)) plan.ManagedRemap = mr;
            }
            else
            {
                var r = EntityRemapPlan.For(type);
                plan.Size = r.Size;
                if (r.HasEntities) plan.Remap = r;
            }
            _plans[typeId] = plan;
            return plan;
        }

        private void EnsureTable(EntityRepository target, int typeId, TypePlan plan)
        {
            if (_registered.Contains(typeId)) return;
            if (!target.TryGetTable(plan.Type, out _))
                RegisterComponent.MakeGenericMethod(plan.Type).Invoke(target, new object?[] { null });
            _registered.Add(typeId);
        }
    }
}
