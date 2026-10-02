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
    /// The occurrence slot-key algorithms.
    ///
    /// <para>⛔ <c>CE-504</c> slice 4: the code-built stateful-node seam (<c>RegisterBlockThunk</c>) moved to
    /// <see cref="SharedNodeBinder.RegisterStatefulAction{TBB,TParams,TWorkingState}"/>, which curries the shared form.</para>
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
        ///   <item><c>Entity</c>: ⛔ removed by <c>CE-441</c> slice 1.</item>
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

        // ⛔ CE-504 slice 4 — RegisterBlockThunk (CE-430: curried the BTree-only (ref P, ref WS, ref BTS, ref Ctx) form over the
        //   block) is RETIRED with that form: SharedNodeBinder.RegisterStatefulAction curries the shared stateful form the
        //   same way, with the same {fqn}@{paramOffset}@{stateOffset} key.
    }
}
