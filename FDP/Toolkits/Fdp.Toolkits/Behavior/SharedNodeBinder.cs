using System;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Runtime;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>⭐ <c>CE-504</c> C-2 — the shared C# node: its params, the entity, the world. The same signature the HSM binds.</summary>
    public delegate NodeStatus SharedNodeAction<TParams>(ref TParams p, Entity self, EntityRepository world);

    /// <summary>⭐ <c>CE-504</c> C-2 — the shared C# condition (a <c>bool</c>; the BTree turns it into Success/Failure).</summary>
    public delegate bool SharedNodeCondition<TParams>(ref TParams p, Entity self, EntityRepository world);

    /// <summary>⭐ <c>CE-504</c> C-2 — the shared stateful C# node: params plus the node's own working memory.</summary>
    public delegate NodeStatus SharedNodeStatefulAction<TParams, TWorkingState>(
        ref TParams p, ref TWorkingState ws, Entity self, EntityRepository world);

    /// <summary>⭐ <c>CE-504</c> C-3 — the shared param-less C# node: binds no variable.</summary>
    public delegate NodeStatus SharedNodeNoParams(Entity self, EntityRepository world);

    /// <summary>⭐ <c>CE-504</c> C-3 — the shared param-less C# condition.</summary>
    public delegate bool SharedNodeNoParamsCondition(Entity self, EntityRepository world);

    /// <summary>
    /// ⭐⭐ <b><c>CE-504</c> C-4 — curated (C#-authored) BTrees bind the shared C# node signature through these.</b>
    /// 📄 <c>docs/blueprints/DESIGN_BTree_Node_Call_Shapes.md</c> §4 C-4.
    ///
    /// <para>Each method curries the shared signature into the kernel's <see cref="NodeLogicDelegate{TBlackboard,TContext}"/>
    /// ONCE, at tree-build time, registers it in the builder's registry under the key a JSON asset would use for the same
    /// binding, and returns that key; the authoring-side extension adds the leaf through <c>BTreeBuilder.Action(string)</c>.
    /// ⭐ No per-tick allocation: the thunk is built once; a tick costs one extra indirect call.</para>
    ///
    /// <para>⚠ The key is computed from the INNER method (<c>logic.Method</c>), never from the thunk — the thunk is a
    /// compiler-generated lambda, and keying by it is the reason a plain wrapping lambda cannot be passed to the builder.</para>
    /// </summary>
    public static class SharedNodeBinder
    {
        /// <summary>The plain form, projected at the selected field. Key <c>{Fqn}@{offset}</c>.</summary>
        public static string RegisterAction<TBB, TParams>(
            ActionRegistry<TBB, BTreeContext> registry, Expression<Func<TBB, TParams>> paramSelector,
            SharedNodeAction<TParams> logic)
            where TBB : struct where TParams : unmanaged
        {
            Require(registry, logic);
            nint offset = FieldOffset(paramSelector);
            string key = $"{Fqn(logic)}@{offset}";
            registry.Register(key, (ref TBB bb, ref BehaviorTreeState st, ref BTreeContext ctx, int _) =>
                logic(ref Unsafe.As<TBB, TParams>(ref Unsafe.AddByteOffset(ref bb, offset)), ctx.Self, ctx.World));
            return key;
        }

        /// <summary>The <c>bool</c> condition form. Key <c>{Fqn}@{offset}</c>.</summary>
        public static string RegisterCondition<TBB, TParams>(
            ActionRegistry<TBB, BTreeContext> registry, Expression<Func<TBB, TParams>> paramSelector,
            SharedNodeCondition<TParams> logic)
            where TBB : struct where TParams : unmanaged
        {
            Require(registry, logic);
            nint offset = FieldOffset(paramSelector);
            string key = $"{Fqn(logic)}@{offset}";
            registry.RegisterCondition(key, (ref TBB bb, ref BehaviorTreeState st, ref BTreeContext ctx, int _) =>
                logic(ref Unsafe.As<TBB, TParams>(ref Unsafe.AddByteOffset(ref bb, offset)), ctx.Self, ctx.World)
                    ? NodeStatus.Success : NodeStatus.Failure);
            return key;
        }

        /// <summary>The stateful form; both halves are fields of the block. Key <c>{Fqn}@{paramOffset}@{stateOffset}</c>.</summary>
        public static string RegisterStatefulAction<TBB, TParams, TWorkingState>(
            ActionRegistry<TBB, BTreeContext> registry,
            Expression<Func<TBB, TParams>> paramSelector, Expression<Func<TBB, TWorkingState>> stateSelector,
            SharedNodeStatefulAction<TParams, TWorkingState> logic)
            where TBB : struct where TParams : unmanaged where TWorkingState : unmanaged
        {
            Require(registry, logic);
            nint po = FieldOffset(paramSelector), so = FieldOffset(stateSelector);
            string key = $"{Fqn(logic)}@{po}@{so}";
            registry.Register(key, (ref TBB bb, ref BehaviorTreeState st, ref BTreeContext ctx, int _) =>
                logic(ref Unsafe.As<TBB, TParams>(ref Unsafe.AddByteOffset(ref bb, po)),
                      ref Unsafe.As<TBB, TWorkingState>(ref Unsafe.AddByteOffset(ref bb, so)),
                      ctx.Self, ctx.World));
            return key;
        }

        /// <summary>The param-less form. Key <c>{Fqn}</c> — the bare FQN a JSON asset's <c>NoParams</c> node uses.</summary>
        public static string RegisterNoParams<TBB>(
            ActionRegistry<TBB, BTreeContext> registry, SharedNodeNoParams logic, bool isCondition = false)
            where TBB : struct
        {
            Require(registry, logic);
            string key = Fqn(logic);
            NodeLogicDelegate<TBB, BTreeContext> thunk =
                (ref TBB bb, ref BehaviorTreeState st, ref BTreeContext ctx, int _) => logic(ctx.Self, ctx.World);
            if (isCondition) registry.RegisterCondition(key, thunk);
            else             registry.Register(key, thunk);
            return key;
        }

        /// <summary>The param-less <c>bool</c> condition. Key <c>{Fqn}</c>.</summary>
        public static string RegisterNoParamsCondition<TBB>(
            ActionRegistry<TBB, BTreeContext> registry, SharedNodeNoParamsCondition logic)
            where TBB : struct
        {
            Require(registry, logic);
            string key = Fqn(logic);
            registry.RegisterCondition(key, (ref TBB bb, ref BehaviorTreeState st, ref BTreeContext ctx, int _) =>
                logic(ctx.Self, ctx.World) ? NodeStatus.Success : NodeStatus.Failure);
            return key;
        }

        private static string Fqn(Delegate logic) => $"{logic.Method.DeclaringType!.FullName}.{logic.Method.Name}";

        private static void Require(object registry, Delegate logic)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (logic == null) throw new ArgumentNullException(nameof(logic));
            if (logic.Target != null || logic.Method.Name.Contains("<"))
                throw new ArgumentException(
                    "Pass a static node method (a method group), not a lambda: the registry key is the method's FQN.",
                    nameof(logic));
        }

        private static nint FieldOffset<TBB, TValue>(Expression<Func<TBB, TValue>> selector)
        {
            var member = selector.Body as MemberExpression ?? (selector.Body as UnaryExpression)?.Operand as MemberExpression;
            if (member == null)
                throw new ArgumentException("The selector must be a direct field access (e.g. bb => bb.Params).",
                                            nameof(selector));
            return (nint)Marshal.OffsetOf<TBB>(member.Member.Name);
        }
    }
}
