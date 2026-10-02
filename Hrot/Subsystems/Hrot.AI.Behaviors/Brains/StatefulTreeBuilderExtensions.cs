using System;
using System.Linq.Expressions;
using Fbt.Compiler;
using Fdp.Toolkit.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// Authoring-side glue that lets a code <c>[BTreeDefinition]</c> builder bind a four-parameter stateful node
    /// method through FastBTree's generic <c>BTreeBuilder.Action(string methodKey)</c> seam.
    ///
    /// <para>⭐ <c>CE-430</c> — the working state is a FIELD of the builder's <c>TBlackboard</c>, the behaviour's
    /// one block (<c>Q76</c> §12.23). The currying lives in the runtime toolkit
    /// (<see cref="StatefulBTreeActionBinder.RegisterBlockThunk{TBB,TParams,TWorkingState}"/>, which touches only
    /// <c>Fbt.Kernel</c>); this wrapper adds the one step that needs <c>Fbt.Compiler</c>: the leaf node.</para>
    ///
    /// <para>⛔⛔ HISTORY — the S3-G overload took a slot manifest, a variable id, a scope and a visual id and
    /// bound the state to a partition slot. Retired by <c>CE-430</c>.</para>
    /// </summary>
    public static class StatefulTreeBuilderExtensions
    {
        /// <summary>
        /// Adds a stateful action leaf over the block: <paramref name="paramSelector"/> and
        /// <paramref name="stateSelector"/> pick two fields of <typeparamref name="TBB"/>. Nodes that select the
        /// same state field share it.
        /// </summary>
        public static BTreeBuilder<TBB, BTreeContext> StatefulAction<TBB, TParams, TWorkingState>(
            this BTreeBuilder<TBB, BTreeContext> builder,
            Expression<Func<TBB, TParams>> paramSelector,
            Expression<Func<TBB, TWorkingState>> stateSelector,
            ReusableStatefulActionDelegate<TParams, TWorkingState, BTreeContext> logic,
            Guid visualId = default)
            where TBB : struct
            where TParams : unmanaged
            where TWorkingState : unmanaged
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));

            string key = StatefulBTreeActionBinder.RegisterBlockThunk(
                builder.GetRegistry(), paramSelector, stateSelector, logic);

            builder.Action(key, visualId);
            return builder;
        }
    }
}
