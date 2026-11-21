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
    /// <summary>⭐ <c>CE-1410</c> — a node's editor id, written as a literal in a C#-built tree (<c>using static</c>), so a converted asset keeps
    /// the ids its JSON had: the editor's selection, breakpoints and decision views stay keyed to the same nodes.</summary>
    public static class TreeNodeIds
    {
        public static Guid Id(string id) => Guid.Parse(id);
    }

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

        /// <summary>⭐ <c>CE-2069</c> — a shared stateful <c>bool</c> condition leaf (params + a working memory it reads).</summary>
        public static BTreeBuilder<TBB, BTreeContext> StatefulCondition<TBB, TParams, TWorkingState>(
            this BTreeBuilder<TBB, BTreeContext> builder,
            Expression<Func<TBB, TParams>> paramSelector, Expression<Func<TBB, TWorkingState>> stateSelector,
            SharedNodeStatefulCondition<TParams, TWorkingState> logic, Guid visualId = default)
            where TBB : struct where TParams : unmanaged where TWorkingState : unmanaged
            => builder.Condition(
                SharedNodeBinder.RegisterStatefulCondition(builder.GetRegistry(), paramSelector, stateSelector, logic), visualId);

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

        /// <summary>⭐ <c>CE-1410</c> (R-1042) — the SOP order "React": start <paramref name="behavior"/> with its defaults as a reaction at
        /// <paramref name="urgency"/> (pauses the task; <see cref="SopActions.React(Fdp.Core.EntityRepository, Fdp.Core.Entity, string, Fdp.Toolkit.Behavior.Components.ReactionUrgency, string)"/>).
        /// A tree with one carries SOP orders — it IS an SOP. 📄 <c>docs/DESIGN_Decision_Layer.md</c> §4.6.</summary>
        public static BTreeBuilder<TBB, BTreeContext> React<TBB>(
            this BTreeBuilder<TBB, BTreeContext> builder, string behavior, Fdp.Toolkit.Behavior.Components.ReactionUrgency urgency,
            Guid visualId = default)
            where TBB : struct
            => builder.Action(SharedNodeBinder.RegisterSopOrder(builder.GetRegistry(), behavior, urgency), visualId);

        /// <summary>⭐ <c>CE-1410</c> (R-1042) — the SOP order "Do when idle": run <paramref name="behavior"/> with its defaults when no order or
        /// reaction holds the task slot (<see cref="SopActions.DoWhenIdle(Fdp.Core.EntityRepository, Fdp.Core.Entity, string, string)"/>).</summary>
        public static BTreeBuilder<TBB, BTreeContext> DoWhenIdle<TBB>(
            this BTreeBuilder<TBB, BTreeContext> builder, string behavior, Guid visualId = default)
            where TBB : struct
            => builder.Action(SharedNodeBinder.RegisterSopOrder(builder.GetRegistry(), behavior, urgency: null), visualId);
    }
}
