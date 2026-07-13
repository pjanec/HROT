using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Runtime;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// S3-G (stage 2): accumulates the <see cref="StatefulSlotInfo"/> manifest produced while a code
    /// <c>[BTreeDefinition]</c> builder binds stateful nodes via
    /// <see cref="StatefulBTreeActionBinder.StatefulAction{TBB,TParams,TWorkingState}"/>. Behavior/Entity
    /// scoped nodes that resolve to the same slot key are deduped to a single manifest entry (one shared
    /// partition slot), matching <c>BTreeBridgeEmitCore.EmitStatefulWorkingSlotsArray</c>. The caller reads
    /// <see cref="ToManifest"/> and hands it to the <see cref="BehaviorDefinition.StatefulWorkingSlots"/>
    /// so <c>BehaviorIngressSystem</c> provisions the slots before the first tick.
    /// </summary>
    public sealed class StatefulSlotManifestBuilder
    {
        private readonly Dictionary<int, StatefulSlotInfo> _slots = new();
        private readonly List<int> _order = new();

        /// <summary>Creates a manifest builder for the asset identified by <paramref name="assetId"/>.</summary>
        public StatefulSlotManifestBuilder(Guid assetId) => AssetId = assetId;

        /// <summary>Asset id folded into Node/Behavior-scoped slot keys.</summary>
        public Guid AssetId { get; }

        /// <summary>
        /// Records a slot (idempotent by <paramref name="slotKey"/>). Returns the manifest entry, whether
        /// freshly added or the existing shared one. Called by the binder; not usually called directly.
        /// </summary>
        internal StatefulSlotInfo Add(
            int slotKey, int payloadSize, uint structureHash,
            Type workingStateType, string? nodeLabel, StatefulSlotScope scope)
        {
            if (_slots.TryGetValue(slotKey, out var existing))
                return existing;

            var info = new StatefulSlotInfo(
                slotKey, payloadSize, structureHash, workingStateType, nodeLabel,
                Role: 1 /* State */, Scope: (byte)scope);
            _slots[slotKey] = info;
            _order.Add(slotKey);
            return info;
        }

        /// <summary>Returns the deduped manifest in first-seen order, or <c>null</c> if no stateful nodes were bound.</summary>
        public IReadOnlyList<StatefulSlotInfo>? ToManifest()
            => _order.Count == 0 ? null : _order.Select(k => _slots[k]).ToList();
    }

    /// <summary>
    /// S3-G (stage 2): curries a four-parameter stateful node method into a FastBTree
    /// <see cref="NodeLogicDelegate{TBlackboard,TContext}"/> and registers it in the tree builder's
    /// <see cref="ActionRegistry{TBlackboard,TContext}"/>, so the code <c>[BTreeDefinition]</c> builder can author
    /// <c>ThreeParamReusableStateful</c> nodes without any FastBTree change. This runtime toolkit only touches
    /// <c>Fbt.Kernel</c> types (delegate + registry); the authoring-side <c>StatefulAction</c> extension (which
    /// needs <c>Fbt.Compiler</c>'s <c>BTreeBuilder</c>) is a thin wrapper that calls this and then adds the leaf
    /// node via the generic <c>BTreeBuilder.Action(string methodKey)</c> seam.
    ///
    /// <para>The curried thunk is the run-time analogue of the JSON emitter's stateful thunk
    /// (<c>BTreeBridgeEmitCore.EmitStatefulActionThunks</c>): it projects the params at the blackboard field
    /// offset, dispatches across the entity's partition tier (16384 → 4096 → 1024),
    /// <c>TryGetSlotOffset(scopeKey)</c>, projects the working state, and calls
    /// <c>(ref TParams, ref TWorkingState, ref BehaviorTreeState, ref TContext)</c>. The scope key is computed
    /// with the same FNV-1a as <c>BTreeBridgeEmitCore.ComputeStatefulSlotKey</c>.</para>
    /// </summary>
    public static class StatefulBTreeActionBinder
    {
        private const uint FnvOffsetBasis = 2166136261u;
        private const uint FnvPrime       = 16777619u;

        /// <summary>
        /// Scope-aware FNV-1a-32 slot key, byte-identical to
        /// <c>BTreeBridgeEmitCore.ComputeStatefulSlotKey(assetId, scope, nodeVisualId, variableId)</c>:
        /// <list type="bullet">
        ///   <item><see cref="StatefulSlotScope.Node"/>: FNV(assetId bytes ++ nodeVisualId bytes).</item>
        ///   <item><see cref="StatefulSlotScope.Behavior"/>: FNV(assetId bytes ++ variableId UTF-8).</item>
        ///   <item><see cref="StatefulSlotScope.Entity"/>: FNV(variableId UTF-8 only).</item>
        /// </list>
        /// Result masked to a non-negative int.
        /// </summary>
        public static int ComputeStatefulSlotKey(
            Guid assetId, StatefulSlotScope scope, Guid nodeVisualId, string variableId)
        {
            unchecked
            {
                uint hash = FnvOffsetBasis;
                switch (scope)
                {
                    case StatefulSlotScope.Node:
                        foreach (byte b in assetId.ToByteArray())      { hash ^= b; hash *= FnvPrime; }
                        foreach (byte b in nodeVisualId.ToByteArray()) { hash ^= b; hash *= FnvPrime; }
                        return (int)(hash & 0x7FFFFFFFu);

                    case StatefulSlotScope.Behavior:
                        foreach (byte b in assetId.ToByteArray())                          { hash ^= b; hash *= FnvPrime; }
                        foreach (byte b in System.Text.Encoding.UTF8.GetBytes(variableId)) { hash ^= b; hash *= FnvPrime; }
                        return (int)(hash & 0x7FFFFFFFu);

                    case StatefulSlotScope.Entity:
                        foreach (byte b in System.Text.Encoding.UTF8.GetBytes(variableId)) { hash ^= b; hash *= FnvPrime; }
                        return (int)(hash & 0x7FFFFFFFu);

                    default:
                        throw new ArgumentOutOfRangeException(nameof(scope), scope, null);
                }
            }
        }

        /// <summary>
        /// FNV-1a-32 of the UTF-8-ish bytes of a type name, matching
        /// <c>BTreeBridgeEmitCore.ComputeTypeNameHash</c>. Used as the type-name component of the
        /// layout-sensitive <see cref="StatefulSlotInfo.StructureHash"/>.
        /// </summary>
        public static uint ComputeTypeNameHash(string typeName)
        {
            unchecked
            {
                uint hash = FnvOffsetBasis;
                foreach (char c in typeName)
                {
                    hash ^= (byte)(c & 0xFF);
                    hash *= FnvPrime;
                    if (c > 0xFF)
                    {
                        hash ^= (byte)(c >> 8);
                        hash *= FnvPrime;
                    }
                }
                return hash;
            }
        }

        /// <summary>
        /// Curries a four-parameter stateful node method into a <see cref="NodeLogicDelegate{TBlackboard,TContext}"/>,
        /// registers it in <paramref name="registry"/> under the emitter-compatible key
        /// <c>{MethodFqn}@{paramOffset}@{slotKey}</c>, records the slot in <paramref name="manifest"/>, and returns
        /// the key. The caller then adds the leaf node referencing that key through FastBTree's generic seam
        /// (<c>BTreeBuilder.Action(string methodKey)</c>) — the thin authoring-side <c>StatefulAction</c> extension
        /// does exactly this, keeping the FastBTree <c>Fbt.Compiler</c> dependency out of this runtime toolkit.
        ///
        /// <para>The param field is projected from the blackboard via <paramref name="paramSelector"/> (offset baked
        /// once at build time); the working state is projected from the partition slot keyed by
        /// (<paramref name="scope"/>, asset, <paramref name="variableId"/>).</para>
        /// </summary>
        /// <param name="registry">The tree builder's action registry (<c>builder.GetRegistry()</c>).</param>
        /// <param name="paramSelector">Direct field/property access selecting the params sub-field, e.g. <c>bb =&gt; bb.Params</c>.</param>
        /// <param name="logic">The four-parameter stateful node method.</param>
        /// <param name="manifest">Accumulates the working-slot manifest; supplies the asset id.</param>
        /// <param name="variableId">Working-state variable name; drives the slot key for Behavior/Entity scope.</param>
        /// <param name="scope">Slot scope. Behavior (default) shares one slot across co-bound nodes.</param>
        /// <param name="visualId">Stable node visual id. Required for <see cref="StatefulSlotScope.Node"/> (folded into the key).</param>
        /// <param name="label">Optional friendly label for the inspector; defaults to the method name.</param>
        /// <returns>The registry key the caller must pass to <c>BTreeBuilder.Action(string)</c>.</returns>
        public static string RegisterStatefulThunk<TBB, TParams, TWorkingState>(
            ActionRegistry<TBB, BTreeContext> registry,
            Expression<Func<TBB, TParams>> paramSelector,
            ReusableStatefulActionDelegate<TParams, TWorkingState, BTreeContext> logic,
            StatefulSlotManifestBuilder manifest,
            string variableId,
            StatefulSlotScope scope = StatefulSlotScope.Behavior,
            Guid visualId = default,
            string? label = null)
            where TBB : struct
            where TParams : unmanaged
            where TWorkingState : unmanaged
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (logic == null) throw new ArgumentNullException(nameof(logic));
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (string.IsNullOrEmpty(variableId)) throw new ArgumentException("variableId is required.", nameof(variableId));
            if (scope == StatefulSlotScope.Node && visualId == default)
                throw new ArgumentException("Node-scoped stateful bindings require a stable visualId (it is folded into the slot key).", nameof(visualId));

            nint paramOffset = ExtractFieldOffset(paramSelector);

            // Behavior/Entity scope ignore the node id; Node scope folds it in.
            Guid keyVisualId = scope == StatefulSlotScope.Node ? visualId : Guid.Empty;
            int slotKey = ComputeStatefulSlotKey(manifest.AssetId, scope, keyVisualId, variableId);

            // Curried thunk — runtime analogue of the emitted stateful thunk (tier dispatch + slot projection).
            NodeLogicDelegate<TBB, BTreeContext> thunk =
                (ref TBB bb, ref BehaviorTreeState st, ref BTreeContext ctx, int _) =>
                {
                    unsafe
                    {
                        ref TParams p = ref Unsafe.As<TBB, TParams>(
                            ref Unsafe.AddByteOffset(ref bb, paramOffset));

                        if (ctx.World.HasComponent<BlueprintBlackboard16384>(ctx.Self))
                        {
                            ref var tier = ref ctx.World.GetComponentRW<BlueprintBlackboard16384>(ctx.Self);
                            fixed (byte* mem = tier.Memory)
                            {
                                if (!BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out int wsOff))
                                {
                                    System.Diagnostics.Debug.Assert(false, $"S3-G: stateful slot {slotKey} missing from BlueprintBlackboard16384");
                                    return NodeStatus.Failure;
                                }
                                ref var ws = ref Unsafe.AsRef<TWorkingState>(mem + wsOff);
                                return logic(ref p, ref ws, ref st, ref ctx);
                            }
                        }
                        if (ctx.World.HasComponent<BlueprintBlackboard4096>(ctx.Self))
                        {
                            ref var tier = ref ctx.World.GetComponentRW<BlueprintBlackboard4096>(ctx.Self);
                            fixed (byte* mem = tier.Memory)
                            {
                                if (!BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out int wsOff))
                                {
                                    System.Diagnostics.Debug.Assert(false, $"S3-G: stateful slot {slotKey} missing from BlueprintBlackboard4096");
                                    return NodeStatus.Failure;
                                }
                                ref var ws = ref Unsafe.AsRef<TWorkingState>(mem + wsOff);
                                return logic(ref p, ref ws, ref st, ref ctx);
                            }
                        }
                        if (ctx.World.HasComponent<BlueprintBlackboard1024>(ctx.Self))
                        {
                            ref var tier = ref ctx.World.GetComponentRW<BlueprintBlackboard1024>(ctx.Self);
                            fixed (byte* mem = tier.Memory)
                            {
                                if (!BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out int wsOff))
                                {
                                    System.Diagnostics.Debug.Assert(false, $"S3-G: stateful slot {slotKey} missing from BlueprintBlackboard1024");
                                    return NodeStatus.Failure;
                                }
                                ref var ws = ref Unsafe.AsRef<TWorkingState>(mem + wsOff);
                                return logic(ref p, ref ws, ref st, ref ctx);
                            }
                        }
                        System.Diagnostics.Debug.Assert(false, $"S3-G: entity has no BlueprintBlackboard* tier component for stateful slot {slotKey}");
                        return NodeStatus.Failure;
                    }
                };

            // Explicit key (matches the emitter convention {MethodFqn}@{paramOffset}@{slotKey}) so
            // co-bound Behavior-scoped nodes on distinct methods each get a distinct registry entry,
            // rather than colliding on the auto-generated lambda name.
            string key = $"{logic.Method.DeclaringType!.FullName}.{logic.Method.Name}@{paramOffset}@{slotKey}";
            registry.Register(key, thunk);

            manifest.Add(
                slotKey,
                payloadSize: Marshal.SizeOf<TWorkingState>(),
                structureHash: unchecked(ComputeTypeNameHash(typeof(TWorkingState).FullName ?? string.Empty)
                                          ^ (uint)Marshal.SizeOf<TWorkingState>()),
                workingStateType: typeof(TWorkingState),
                nodeLabel: label ?? logic.Method.Name,
                scope: scope);

            return key;
        }

        private static nint ExtractFieldOffset<TBB, TValue>(Expression<Func<TBB, TValue>> selector)
        {
            MemberExpression? memberExpr = selector.Body as MemberExpression;
            if (memberExpr == null && selector.Body is UnaryExpression unary)
                memberExpr = unary.Operand as MemberExpression;
            if (memberExpr == null)
                throw new ArgumentException(
                    "paramSelector must be a direct field or property access (e.g. bb => bb.Params).",
                    nameof(selector));
            return (nint)Marshal.OffsetOf<TBB>(memberExpr.Member.Name);
        }
    }
}
