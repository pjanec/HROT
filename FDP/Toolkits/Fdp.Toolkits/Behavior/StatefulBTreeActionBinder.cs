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
    /// The code-built stateful-node seam plus the occurrence slot-key algorithms.
    ///
    /// <para>⭐ <see cref="RegisterBlockThunk{TBB,TParams,TWorkingState}"/> (<c>CE-430</c>) curries a four-parameter
    /// stateful node method over the behaviour's OWN block — params and working state are both fields of the
    /// code builder's <c>TBlackboard</c>. This runtime toolkit only touches <c>Fbt.Kernel</c> types; the
    /// authoring-side <c>StatefulAction</c> extension (which needs <c>Fbt.Compiler</c>) wraps it.</para>
    ///
    /// <para>The key helpers (<see cref="ComputeStatefulSlotKey"/>, <see cref="ComputeOccurrenceSlotKey"/>,
    /// <see cref="ComputeTypeNameHash"/>) still serve the generated and HSM occurrence paths.</para>
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
        /// <remarks>
        /// ⭐⭐ <b>A1 (<c>PLAN_Occurrence_Storage_Build</c>): this is now a THIN WRAPPER.</b> The
        /// algorithm lives in <see cref="Fdp.Toolkit.Behavior.Shared.OccurrenceSlotKey"/>, one file
        /// LINKED into the authoring assembly as well, so the compile-time and runtime keys cannot
        /// drift. ⛔ Do not re-inline the FNV here — that divergence is <c>F5</c> and it fails
        /// silently (the slot is never found, nothing throws).
        /// <para>⚠ The cast is safe because the two enums are pinned value-for-value by
        /// <c>OccurrenceSlotKeyParityTests</c>.</para>
        /// </remarks>
        public static int ComputeStatefulSlotKey(
            Guid assetId, StatefulSlotScope scope, Guid nodeVisualId, string variableId)
            => Fdp.Toolkit.Behavior.Shared.OccurrenceSlotKey.Compute(
                   assetId,
                   (Fdp.Toolkit.Behavior.Shared.OccurrenceSlotScope)(int)scope,
                   nodeVisualId,
                   variableId);

        /// <summary>
        /// ⭐ The NESTED form — <c>DESIGN_Occurrence_Scoped_Storage</c> §3's <c>(assetId, hostPath)</c>.
        /// <paramref name="hostKey"/> <c>== 0</c> means a ROOT occurrence and returns exactly what
        /// <see cref="ComputeStatefulSlotKey(Guid, StatefulSlotScope, Guid, string)"/> returns.
        /// </summary>
        public static int ComputeOccurrenceSlotKey(
            int hostKey, int siteId, Guid assetId, StatefulSlotScope scope, Guid nodeVisualId, string variableId)
            => Fdp.Toolkit.Behavior.Shared.OccurrenceSlotKey.ComputeNested(
                   hostKey,
                   siteId,
                   assetId,
                   (Fdp.Toolkit.Behavior.Shared.OccurrenceSlotScope)(int)scope,
                   nodeVisualId,
                   variableId);

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
        /// ⭐⭐ <c>CE-430</c> — curries a four-parameter stateful node method over the behaviour's OWN block:
        /// both the params and the working state are FIELDS of <typeparamref name="TBB"/>, projected from
        /// <c>ref bb</c> at offsets baked once here. Registers the thunk under
        /// <c>{MethodFqn}@{paramOffset}@{stateOffset}</c> and returns that key; the caller adds the leaf through
        /// FastBTree's generic <c>BTreeBuilder.Action(string methodKey)</c> seam (the authoring-side
        /// <c>StatefulAction</c> extension does exactly this, keeping <c>Fbt.Compiler</c> out of this toolkit).
        ///
        /// <para>⭐ Two nodes that project the SAME state field share it; two that project DIFFERENT fields keep
        /// independent state — the block expresses sharing with no slot key, scope or manifest.</para>
        ///
        /// <para>⛔⛔ HISTORY — <c>RegisterStatefulThunk</c> + <c>StatefulSlotManifestBuilder</c> (S3-G) put the
        /// state in a partition slot keyed by (scope, asset, variable) and needed the ingress to provision it
        /// from a manifest. Deleted by <c>CE-430</c> once the one production author (the hand-written
        /// PlatoonHillAttack tree) moved its state into its blackboard. 📄 <c>Q76</c> §12.23.</para>
        /// </summary>
        /// <param name="registry">The tree builder's action registry (<c>builder.GetRegistry()</c>).</param>
        /// <param name="paramSelector">Direct field access selecting the params, e.g. <c>bb =&gt; bb.Params</c>.</param>
        /// <param name="stateSelector">Direct field access selecting the working state, e.g. <c>bb =&gt; bb.State</c>.</param>
        /// <param name="logic">The four-parameter stateful node method.</param>
        /// <returns>The registry key the caller must pass to <c>BTreeBuilder.Action(string)</c>.</returns>
        public static string RegisterBlockThunk<TBB, TParams, TWorkingState>(
            ActionRegistry<TBB, BTreeContext> registry,
            Expression<Func<TBB, TParams>> paramSelector,
            Expression<Func<TBB, TWorkingState>> stateSelector,
            ReusableStatefulActionDelegate<TParams, TWorkingState, BTreeContext> logic)
            where TBB : struct
            where TParams : unmanaged
            where TWorkingState : unmanaged
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (logic == null) throw new ArgumentNullException(nameof(logic));

            nint paramOffset = ExtractFieldOffset(paramSelector);
            nint stateOffset = ExtractFieldOffset(stateSelector);

            NodeLogicDelegate<TBB, BTreeContext> thunk =
                (ref TBB bb, ref BehaviorTreeState st, ref BTreeContext ctx, int _) =>
                {
                    ref TParams p = ref Unsafe.As<TBB, TParams>(ref Unsafe.AddByteOffset(ref bb, paramOffset));
                    ref TWorkingState ws = ref Unsafe.As<TBB, TWorkingState>(ref Unsafe.AddByteOffset(ref bb, stateOffset));
                    return logic(ref p, ref ws, ref st, ref ctx);
                };

            string key = $"{logic.Method.DeclaringType!.FullName}.{logic.Method.Name}@{paramOffset}@{stateOffset}";
            registry.Register(key, thunk);
            return key;
        }

        private static nint ExtractFieldOffset<TBB, TValue>(Expression<Func<TBB, TValue>> selector)
        {
            MemberExpression? memberExpr = selector.Body as MemberExpression;
            if (memberExpr == null && selector.Body is UnaryExpression unary)
                memberExpr = unary.Operand as MemberExpression;
            if (memberExpr == null)
                throw new ArgumentException(
                    "The selector must be a direct field access (e.g. bb => bb.Params).",
                    nameof(selector));
            return (nint)Marshal.OffsetOf<TBB>(memberExpr.Member.Name);
        }
    }
}
