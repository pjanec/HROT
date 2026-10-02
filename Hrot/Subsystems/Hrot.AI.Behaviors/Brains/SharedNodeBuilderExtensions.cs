using System;
using System.Linq.Expressions;
using Fbt.Compiler;
using Fdp.Toolkit.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-504</c> C-4 — a curated <c>[BTreeDefinition]</c> tree binds a shared C# node method
    /// (<c>(ref P, Entity, EntityRepository)</c>, its stateful and param-less forms) exactly as it binds the old 3-param one:
    /// <c>.Action(bb =&gt; bb.Params, Nodes.Action_X)</c>. The currying is <see cref="SharedNodeBinder"/> (runtime toolkit,
    /// <c>Fbt.Kernel</c> only); this adds the leaf, which needs <c>Fbt.Compiler</c> — the former <c>StatefulTreeBuilderExtensions</c>
    /// split. 📄 <c>docs/blueprints/DESIGN_BTree_Node_Call_Shapes.md</c> §4 C-4.
    ///
    /// <para>⚠ These are extension methods with the builder's own names. C# picks an instance method first, but the builder's
    /// <c>Action(selector, ReusableActionDelegate)</c> cannot take a method of the shared signature, so the call resolves here.</para>
    /// </summary>
    public static class SharedNodeBuilderExtensions
    {
        public static BTreeBuilder<TBB, BTreeContext> Action<TBB, TParams>(
            this BTreeBuilder<TBB, BTreeContext> builder, Expression<Func<TBB, TParams>> paramSelector,
            SharedNodeAction<TParams> logic, Guid visualId = default)
            where TBB : struct where TParams : unmanaged
            => builder.Action(SharedNodeBinder.RegisterAction(builder.GetRegistry(), paramSelector, logic), visualId);

        public static BTreeBuilder<TBB, BTreeContext> Condition<TBB, TParams>(
            this BTreeBuilder<TBB, BTreeContext> builder, Expression<Func<TBB, TParams>> paramSelector,
            SharedNodeCondition<TParams> logic, Guid visualId = default)
            where TBB : struct where TParams : unmanaged
            => builder.Condition(SharedNodeBinder.RegisterCondition(builder.GetRegistry(), paramSelector, logic), visualId);

        /// <summary>A shared method returning <c>NodeStatus</c>, used as a condition leaf.</summary>
        public static BTreeBuilder<TBB, BTreeContext> Condition<TBB, TParams>(
            this BTreeBuilder<TBB, BTreeContext> builder, Expression<Func<TBB, TParams>> paramSelector,
            SharedNodeAction<TParams> logic, Guid visualId = default)
            where TBB : struct where TParams : unmanaged
            // ⭐ the registry has ONE table (RegisterCondition is an alias), so the action thunk serves the condition leaf.
            => builder.Condition(SharedNodeBinder.RegisterAction(builder.GetRegistry(), paramSelector, logic), visualId);

        public static BTreeBuilder<TBB, BTreeContext> StatefulAction<TBB, TParams, TWorkingState>(
            this BTreeBuilder<TBB, BTreeContext> builder,
            Expression<Func<TBB, TParams>> paramSelector, Expression<Func<TBB, TWorkingState>> stateSelector,
            SharedNodeStatefulAction<TParams, TWorkingState> logic, Guid visualId = default)
            where TBB : struct where TParams : unmanaged where TWorkingState : unmanaged
            => builder.Action(
                SharedNodeBinder.RegisterStatefulAction(builder.GetRegistry(), paramSelector, stateSelector, logic), visualId);

        public static BTreeBuilder<TBB, BTreeContext> Action<TBB>(
            this BTreeBuilder<TBB, BTreeContext> builder, SharedNodeNoParams logic, Guid visualId = default)
            where TBB : struct
            => builder.Action(SharedNodeBinder.RegisterNoParams(builder.GetRegistry(), logic), visualId);

        public static BTreeBuilder<TBB, BTreeContext> Condition<TBB>(
            this BTreeBuilder<TBB, BTreeContext> builder, SharedNodeNoParams logic, Guid visualId = default)
            where TBB : struct
            => builder.Condition(SharedNodeBinder.RegisterNoParams(builder.GetRegistry(), logic, isCondition: true), visualId);

        public static BTreeBuilder<TBB, BTreeContext> Condition<TBB>(
            this BTreeBuilder<TBB, BTreeContext> builder, SharedNodeNoParamsCondition logic, Guid visualId = default)
            where TBB : struct
            => builder.Condition(SharedNodeBinder.RegisterNoParamsCondition(builder.GetRegistry(), logic), visualId);
    }
}
