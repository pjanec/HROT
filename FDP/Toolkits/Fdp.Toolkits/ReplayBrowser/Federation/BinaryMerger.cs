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
    /// <para>⛔ §6.10 holds: never a raw chunk or <c>EntityIndex</c> copy — every value goes entity by entity into the target's own entities. Singletons (world-level components)
    /// are NOT merged: they are per-node services and indices (the old builder never touched them either).</para>
    /// <para>⭐ <b>S4 (I3, M4) — INCREMENTAL, 0 B per steady frame.</b> ONE entry point: the FIRST pass (and every pass after a trigger below) is the same pass with every component
    /// treated as moved. A component is copied only when (a) the chunk of its SOURCE table holding the entity carries a version newer than that node's last merge
    /// (<c>IComponentTable.GetVersionForEntity</c> newer than the node's sandbox tick at its last merge (at that tick too while the clock is frozen) — playback stamps every restored chunk with the frame's tick, M4), or (b) the node
    /// is newly the supplier of that component (an OWNER CHANGE, S2's answer differs from last time), or (c) the node's whole pass is full:
    /// a first merge · <see cref="Reset"/> / <see cref="Invalidate"/> (a WORLD BOUNDARY) · a different target repository · the node's sandbox time went BACKWARD (a seek back — versions
    /// then say nothing) · the type registry changed, or (d) the component HOLDS a raw entity handle and the entity set changed (a target entity was born or destroyed, a source →
    /// target mapping moved) or a paradox was listed last pass — every handle holder is then re-remapped, which is what a full pass does in the same call (the <c>Sweep</c> hazard).
    /// Nothing allocates in the steady state: nodes are walked by array, the owner sort is in place, one resolver delegate per node lives for the merger's life, and every buffer is
    /// grown, never shrunk. A managed component allocates only when it is copied AND remapped (<see cref="ManagedEntityRemap"/>) — i.e. when its chunk moved.</para>
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

        /// <summary>One node's merge state: its map (source entity → target entity), its resolver, the sandbox tick at its last merge (M4) and — per TARGET entity — what that node supplied last time.</summary>
        private sealed class NodeState
        {
            public int NodeId;
            public EntityRepository? Repo;
            public readonly Dictionary<Entity, Entity> Map = new();
            public Func<Entity, Entity> Resolver = null!;
            public long LastTick = -1;            // the sandbox's GlobalVersion at its last merge; -1: never merged
            public uint Tick;                     // … at this pass
            public bool Full;                     // every component of this node is treated as moved this pass
            public long MinVersion;               // a chunk stamped at or above this moved since the last merge (see Moved)
            public IComponentTable?[] Tables = new IComponentTable?[512];
            public Row[] Rows = new Row[64];      // by TARGET entity index

            public IComponentTable? TableOf(int typeId, Type type)
            {
                var t = Tables[typeId];
                if (t == null && Repo!.TryGetTable(type, out var found)) Tables[typeId] = t = found;
                return t;
            }

            public ref Row RowOf(int targetIndex)
            {
                if (targetIndex >= Rows.Length) Array.Resize(ref Rows, Math.Max(targetIndex + 1, Rows.Length * 2));
                return ref Rows[targetIndex];
            }
        }

        /// <summary>What a node supplied to one target entity at the last pass: valid only when it is from the PREVIOUS pass and names the same target and source entity.</summary>
        private struct Row { public BitMask512 Take; public Entity Target; public Entity Src; public int Pass; }

        private readonly MergeTarget _target = new();
        private readonly OwnershipSource _ownership;
        private readonly Dictionary<long, List<NodeEntity>> _corr = new();
        private readonly Stack<List<NodeEntity>> _listPool = new();
        private readonly Dictionary<int, NodeState> _nodes = new();
        private readonly List<(Entity source, Entity target)> _locals = new();
        private readonly List<BitMask512> _extracts = new();
        private readonly List<MergeParadox> _paradoxes = new();
        private readonly HashSet<int> _registered = new();
        private TypePlan?[] _plans = new TypePlan?[512];
        private int _planGeneration = -1;
        private byte[] _scratch = GC.AllocateArray<byte>(256, pinned: true);

        private long _curNet;
        private Type? _curType;
        private bool _valid;            // the last pass completed against this target: the next one may be incremental
        private int _pass;
        private int _lastPass = -1;     // the pass number of the last completed pass
        private ulong _lastMapHash;     // the entity set (every node's source → target mapping) of the last pass …
        private int _lastMapCount;
        private bool _structural;       // … and whether it moved (or a paradox stood): handle holders are re-remapped

        /// <param name="ownership">Who supplies each component; default <see cref="OwnershipSource"/> (today's rule).</param>
        public BinaryMerger(OwnershipSource? ownership = null) => _ownership = ownership ?? new OwnershipSource();

        /// <summary>The map this merger keeps (network id → target entity).</summary>
        public MergeTarget Target => _target;

        /// <summary>References of the LAST <see cref="Merge"/> that named no entity of the merged world (each became <see cref="Entity.Null"/>).</summary>
        public IReadOnlyList<MergeParadox> Paradoxes => _paradoxes;

        /// <summary>⭐ S2 — components of the last <see cref="Merge"/> on which the recorded ownership components and the cold authority mask disagreed (the components won).</summary>
        public int OwnershipDisagreements => _ownership.MaskDisagreements;

        /// <summary>Components written by the last <see cref="Merge"/>.</summary>
        public int LastComponentsCopied { get; private set; }

        /// <summary>⭐ S4 — components the last <see cref="Merge"/> left alone because their source chunk had not moved (the incremental saving).</summary>
        public int LastComponentsSkipped { get; private set; }

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
            _valid = false;
        }

        /// <summary>⭐ S4 — the next <see cref="Merge"/> copies everything (a full pass, the same code): call it when something other than the merge wrote the target.</summary>
        public void Invalidate() => _valid = false;

        /// <summary>Merges the current playback state of every node of <paramref name="manager"/> into <paramref name="target"/> — incrementally (see the class remarks; the first pass is a full one).</summary>
        public void Merge(FederatedReplayManager manager, EntityRepository target)
        {
            if (manager == null) throw new ArgumentNullException(nameof(manager));
            if (target == null) throw new ArgumentNullException(nameof(target));

            if (_planGeneration != ComponentTypeRegistry.Generation)
            {
                Array.Clear(_plans);
                _planGeneration = ComponentTypeRegistry.Generation;
                _valid = false;
                foreach (var ns in _nodes.Values) Array.Clear(ns.Tables);
            }
            if (!ReferenceEquals(_target.Repo, target)) { _registered.Clear(); _valid = false; }
            _target.Begin(target);
            bool paradoxStood = _paradoxes.Count > 0;
            _paradoxes.Clear();
            LastComponentsCopied = 0;
            LastComponentsSkipped = 0;
            _ownership.BeginPass();
            bool incremental = _valid;
            _valid = false;                    // set again only when this pass completes
            int pass = ++_pass;

            // ① correlate by NetworkIdentity (TransientMasterBuilder step 2) — the nodes are walked by array (no enumerator)
            int netIdTypeId = ComponentTypeRegistry.GetId(typeof(NetworkIdentity));
            foreach (var l in _corr.Values) { l.Clear(); _listPool.Push(l); }
            _corr.Clear();

            for (int ci = 0; ci < manager.NodeCount; ci++)
            {
                int nodeId = manager.NodeIdAt(ci);
                var repo = manager.ContextAt(ci).SandboxRepo;
                var ns = StateOf(nodeId, repo, ref incremental);
                ns.Map.Clear();
                ns.Tick = repo.GlobalVersion;
                ns.Full = !incremental || ns.LastTick < 0 || ns.Tick < ns.LastTick;   // a seek BACK: versions say nothing about what changed
                // a clock that moved: strictly newer chunks changed. A FROZEN clock (same tick as the last merge — a paused world still recorded): what was stamped AT that tick may be new too
                ns.MinVersion = ns.Tick == ns.LastTick ? ns.LastTick : ns.LastTick + 1;
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
            ulong mapHash = 0; int mapCount = 0;
            foreach (var kv in _corr)
            {
                var te = _target.EnsureNetworked(kv.Key);
                var list = kv.Value;
                for (int k = 0; k < list.Count; k++)
                {
                    var ne = list[k];
                    _nodes[ne.NodeId].Map[ne.Entity] = te;
                    mapHash += Mix(ne.NodeId, ne.Entity, te); mapCount++;
                }
            }

            // … and per local entity of the provider node (TransientMasterBuilder step 3b)
            int providerNodeId = manager.LocalEntitiesProviderNodeId;
            _locals.Clear();
            EntityRepository? providerRepo = null;
            if (manager.Contexts.TryGetValue(providerNodeId, out var providerCtx))
            {
                providerRepo = providerCtx.SandboxRepo;
                var pns = _nodes[providerNodeId];
                for (int i = 0; i <= providerRepo.MaxEntityIndex; i++)
                {
                    var e = new Entity(i, providerRepo.GetMetadata(i).Generation);
                    if (!providerRepo.IsAlive(e)) continue;
                    if (netIdTypeId >= 0 && providerRepo.GetComponentMask(i).IsSet(netIdTypeId)) continue;   // a networked entity
                    var te = _target.EnsureLocal(providerNodeId, e);
                    pns.Map[e] = te;
                    mapHash += Mix(providerNodeId, e, te); mapCount++;
                    _locals.Add((e, te));
                }
            }

            // ③ gone from every node ⇒ destroyed. ⭐ S4 — the entity SET moved (a birth, a death, a mapping), or a paradox stood: every holder of a raw handle is re-remapped below,
            //   exactly as a full pass rewrites them in the same call (the Sweep hazard, design §6.12 S1 note)
            _target.Sweep();
            _structural = !incremental || paradoxStood || mapHash != _lastMapHash || mapCount != _lastMapCount || _target.LastDestroyed > 0;
            _lastMapHash = mapHash; _lastMapCount = mapCount;

            // ④ the components
            foreach (var kv in _corr)
            {
                _target.TryGet(kv.Key, out var te);
                _curNet = kv.Key;
                var list = kv.Value;
                _ownership.Claim(manager, list, _extracts);
                var supplied = new BitMask512();
                for (int n = 0; n < list.Count; n++)
                    CopyComponents(_nodes[list[n].NodeId], list[n], te, _extracts[n], target, ref supplied, pass);
                RemoveUnsupplied(target, te, in supplied);
            }

            if (providerRepo != null)
            {
                _curNet = -1;
                var pns = _nodes[providerNodeId];
                for (int k = 0; k < _locals.Count; k++)
                {
                    var (source, te) = _locals[k];
                    var all = providerRepo.GetComponentMask(source.Index);   // every component it holds (not the authority mask)
                    var supplied = new BitMask512();
                    CopyComponents(pns, new NodeEntity(providerNodeId, source), te, all, target, ref supplied, pass);
                    RemoveUnsupplied(target, te, in supplied);
                }
            }

            for (int ci = 0; ci < manager.NodeCount; ci++) { var ns = _nodes[manager.NodeIdAt(ci)]; ns.LastTick = ns.Tick; }
            _lastPass = pass;
            _valid = true;
        }

        private NodeState StateOf(int nodeId, EntityRepository repo, ref bool incremental)
        {
            if (!_nodes.TryGetValue(nodeId, out var ns))
            {
                ns = new NodeState { NodeId = nodeId };
                var captured = ns;
                ns.Resolver = e => Resolve(captured, e);   // one per node, for the merger's life
                _nodes[nodeId] = ns;
            }
            if (!ReferenceEquals(ns.Repo, repo)) { ns.Repo = repo; Array.Clear(ns.Tables); ns.LastTick = -1; }
            return ns;
        }

        private static ulong Mix(int node, Entity s, Entity t)
        {
            ulong h = (ulong)(uint)node * 0x9E3779B97F4A7C15UL
                    ^ (((ulong)(uint)s.Index << 32) | s.Generation) * 0xC2B2AE3D27D4EB4FUL
                    ^ (((ulong)(uint)t.Index << 32) | t.Generation) * 0x165667B19E3779F9UL;
            h ^= h >> 29; h *= 0xBF58476D1CE4E5B9UL; h ^= h >> 32;
            return h;
        }

        // ── one source entity's share ───────────────────────────────────────────────────────

        private void CopyComponents(NodeState ns, NodeEntity src, Entity te, BitMask512 take, EntityRepository target, ref BitMask512 supplied, int pass)
        {
            var srcRepo = ns.Repo!;
            var resolve = ns.Resolver;

            // what this node supplied to this target entity LAST pass — valid only from the previous pass, for the same target and source entity (else: full for this row)
            ref var row = ref ns.RowOf(te.Index);
            bool rowValid = row.Pass == _lastPass && _lastPass >= 0 && row.Target == te && row.Src == src.Entity;
            bool full = ns.Full || !rowValid;
            var last = rowValid ? row.Take : default;
            var tgtMask = target.GetComponentMask(te.Index);

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

                    // ⭐ I3 / M4 — leave it alone when nothing moved: a supplier that has not changed, the chunk version is not newer than the node's last merge, the value is still there,
                    //   and (for a handle holder) the entity set is the same
                    if (!full && last.IsSet(typeId) && tgtMask.IsSet(typeId)
                        && !(_structural && (plan.Remap != null || plan.ManagedRemap != null))
                        && !Moved(ns, plan, typeId, src.Entity.Index))
                    {
                        supplied.SetBit(typeId); LastComponentsSkipped++; continue;
                    }

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

            row.Take = take; row.Target = te; row.Src = src.Entity; row.Pass = pass;
        }

        /// <summary>M4 — did the chunk of <paramref name="plan"/>'s source table that holds entity <paramref name="index"/> change since this node's last merge? (No table ⇒ yes.)</summary>
        private static bool Moved(NodeState ns, TypePlan plan, int typeId, int index)
        {
            var t = ns.TableOf(typeId, plan.Type);
            return t == null || (long)t.GetVersionForEntity(index) >= ns.MinVersion;
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

        private Entity Resolve(NodeState ns, Entity e)
        {
            if (e.IsNull) return Entity.Null;
            if (ns.Map.TryGetValue(e, out var t)) return t;
            // the paradox rule (design §7.6): a reference whose target is not in the merged world becomes Entity.Null AND is flagged — never thrown, never hidden
            _paradoxes.Add(new MergeParadox(_curNet, ns.NodeId, _curType!, e));
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
