using System;
using System.Linq.Expressions;
using Fbt.Compiler;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// S3-G (stage 2): authoring-side glue that lets a code <c>[BTreeDefinition]</c> builder bind a four-parameter
    /// stateful node method through FastBTree's generic <c>BTreeBuilder.Action(string methodKey)</c> seam.
    ///
    /// <para>The heavy lifting — currying the tier-dispatch + partition-slot thunk, computing the scope key, and
    /// recording the <see cref="StatefulSlotInfo"/> manifest — lives in the runtime toolkit
    /// (<see cref="StatefulBTreeActionBinder"/>, which touches only <c>Fbt.Kernel</c>). This wrapper adds the only
    /// step that needs <c>Fbt.Compiler</c>: appending the leaf node that references the registered thunk key.
    /// Keeping it here (rather than in <c>Fdp.Toolkits</c>) preserves the runtime toolkit's freedom from the
    /// FastBTree compiler assembly.</para>
    /// </summary>
    public static class StatefulTreeBuilderExtensions
    {
        /// <summary>
        /// Adds a stateful action leaf: registers the curried four-param thunk and appends a node referencing it.
        /// Behavior/Entity-scoped nodes that resolve to the same slot key share one partition slot (deduped in
        /// <paramref name="manifest"/>).
        /// </summary>
        /// <param name="builder">Code-first tree builder (context fixed to <see cref="BTreeContext"/>).</param>
        /// <param name="paramSelector">Direct field/property access selecting the params sub-field, e.g. <c>bb =&gt; bb.Params</c>.</param>
        /// <param name="logic">The four-parameter stateful node method.</param>
        /// <param name="manifest">Accumulates the working-slot manifest; supplies the asset id.</param>
        /// <param name="variableId">Working-state variable name; drives the slot key for Behavior/Entity scope.</param>
        /// <param name="scope">Slot scope. Behavior (default) shares one slot across co-bound nodes.</param>
        /// <param name="visualId">Stable node visual id. Required for <see cref="StatefulSlotScope.Node"/> (folded into the key).</param>
        /// <param name="label">Optional friendly label for the inspector; defaults to the method name.</param>
        public static BTreeBuilder<TBB, BTreeContext> StatefulAction<TBB, TParams, TWorkingState>(
            this BTreeBuilder<TBB, BTreeContext> builder,
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
            if (builder == null) throw new ArgumentNullException(nameof(builder));

            string key = StatefulBTreeActionBinder.RegisterStatefulThunk(
                builder.GetRegistry(), paramSelector, logic, manifest, variableId, scope, visualId, label);

            builder.Action(key, visualId);
            return builder;
        }
    }
}
