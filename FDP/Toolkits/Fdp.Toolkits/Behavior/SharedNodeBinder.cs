using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
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

    /// <summary>⭐ <c>CE-504</c> slice 4 — the shared deactivator, plain form: the node's params, the entity, the world.</summary>
    public delegate void SharedNodeDeactivator<TParams>(ref TParams p, Entity self, EntityRepository world);

    /// <summary>⭐ <c>CE-504</c> slice 4 — the shared deactivator, stateful form (pairs with a stateful action only).</summary>
    public delegate void SharedNodeStatefulDeactivator<TParams, TWorkingState>(
        ref TParams p, ref TWorkingState ws, Entity self, EntityRepository world);

    /// <summary>⭐ <c>CE-504</c> slice 4 — the shared deactivator, param-less form (pairs with any action).</summary>
    public delegate void SharedNodeNoParamsDeactivator(Entity self, EntityRepository world);

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
        // ⭐⭐ CE-504 slice 3 — the RUNTIME half. 📐 Measured: a curated tree runs on the BYTE interpreter
        //   (`Interpreter<byte, BTreeContext>`, P4-②) bound to the assembly-wide registry, and the builder's own registry —
        //   typed by the tree's blackboard struct — is DISCARDED by the curated registrar. ⇒ each binding also gets the
        //   byte thunk the runtime calls (the projection the retired per-method adapters used:
        //   `BehaviorBlock.Require(ref bb) + offset`), held per builder registry until the generated catalogue copies it
        //   into the runtime registry (`CopyRuntimeThunks`, called by `FbtTreeCatalog.Get{Tree}(…, runtimeActions)`).
        //   ⭐ Slice 4: a paired DEACTIVATOR rides the same list, so the copy registers it before the catalogue compiles
        //   the blob — the curated registrar's resource-owning predicate reads the runtime registry.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object,
            List<(string Key, NodeLogicDelegate<byte, BTreeContext>? Thunk, NodeDeactivatorDelegate<byte, BTreeContext>? Deactivator)>>
            RuntimeThunks = new();

        private static void AddRuntime(object builderRegistry, string key, NodeLogicDelegate<byte, BTreeContext> thunk)
        {
            var list = RuntimeThunks.GetOrCreateValue(builderRegistry);
            lock (list) list.Add((key, thunk, null));
        }

        private static void AddRuntimeDeactivator(object builderRegistry, string key, NodeDeactivatorDelegate<byte, BTreeContext> d)
        {
            var list = RuntimeThunks.GetOrCreateValue(builderRegistry);
            lock (list) list.Add((key, null, d));
        }

        /// <summary>
        /// Copies every runtime (<c>byte</c>) thunk the shared-node verbs registered while building a curated tree into the
        /// registry its interpreter is bound to. <paramref name="builderRegistry"/> is <c>builder.GetRegistry()</c>.
        /// <returns>How many thunks were copied.</returns>
        /// </summary>
        public static int CopyRuntimeThunks(object builderRegistry, ActionRegistry<byte, BTreeContext> into)
        {
            if (builderRegistry == null) throw new ArgumentNullException(nameof(builderRegistry));
            if (into == null) throw new ArgumentNullException(nameof(into));
            if (!RuntimeThunks.TryGetValue(builderRegistry, out var list)) return 0;
            lock (list)
                foreach (var (key, thunk, deactivator) in list)
                {
                    if (thunk != null) into.Register(key, thunk);
                    if (deactivator != null) into.RegisterDeactivator(key, deactivator);
                }
            return list.Count;
        }

        /// <summary>The plain form, projected at the selected field. Key <c>{Fqn}@{offset}</c>.</summary>
        public static string RegisterAction<TBB, TParams>(
            ActionRegistry<TBB, BTreeContext> registry, Expression<Func<TBB, TParams>> paramSelector,
            SharedNodeAction<TParams> logic)
            where TBB : struct where TParams : unmanaged
        {
            Require(registry, logic);
            nint offset = FieldOffset(paramSelector);
            string key = Shared.HsmActionKey.CompoundKeyName(Fqn(logic), offset);   // ⭐ CE-2032
            registry.Register(key, (ref TBB bb, ref BehaviorTreeState st, ref BTreeContext ctx, int _) =>
                logic(ref Unsafe.As<TBB, TParams>(ref Unsafe.AddByteOffset(ref bb, offset)), ctx.Self, ctx.World));
            AddRuntime(registry, key, (ref byte bb, ref BehaviorTreeState st, ref BTreeContext ctx, int _) =>
                logic(ref Unsafe.As<byte, TParams>(ref Unsafe.AddByteOffset(ref BehaviorBlock.Require(ref bb), offset)), ctx.Self, ctx.World));
            PairDeactivator<TBB, TParams, byte>(registry, key, logic.Method, offset, stateOffset: null);
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
            string key = Shared.HsmActionKey.CompoundKeyName(Fqn(logic), offset);   // ⭐ CE-2032
            registry.RegisterCondition(key, (ref TBB bb, ref BehaviorTreeState st, ref BTreeContext ctx, int _) =>
                logic(ref Unsafe.As<TBB, TParams>(ref Unsafe.AddByteOffset(ref bb, offset)), ctx.Self, ctx.World)
                    ? NodeStatus.Success : NodeStatus.Failure);
            AddRuntime(registry, key, (ref byte bb, ref BehaviorTreeState st, ref BTreeContext ctx, int _) =>
                logic(ref Unsafe.As<byte, TParams>(ref Unsafe.AddByteOffset(ref BehaviorBlock.Require(ref bb), offset)), ctx.Self, ctx.World)
                    ? NodeStatus.Success : NodeStatus.Failure);
            PairDeactivator<TBB, TParams, byte>(registry, key, logic.Method, offset, stateOffset: null);
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
            string key = Shared.HsmActionKey.CompoundKeyName(Fqn(logic), po, so);   // ⭐ CE-2032
            registry.Register(key, (ref TBB bb, ref BehaviorTreeState st, ref BTreeContext ctx, int _) =>
                logic(ref Unsafe.As<TBB, TParams>(ref Unsafe.AddByteOffset(ref bb, po)),
                      ref Unsafe.As<TBB, TWorkingState>(ref Unsafe.AddByteOffset(ref bb, so)),
                      ctx.Self, ctx.World));
            AddRuntime(registry, key, (ref byte bb, ref BehaviorTreeState st, ref BTreeContext ctx, int _) =>
                logic(ref Unsafe.As<byte, TParams>(ref Unsafe.AddByteOffset(ref BehaviorBlock.Require(ref bb), po)),
                      ref Unsafe.As<byte, TWorkingState>(ref Unsafe.AddByteOffset(ref BehaviorBlock.Require(ref bb), so)),
                      ctx.Self, ctx.World));
            PairDeactivator<TBB, TParams, TWorkingState>(registry, key, logic.Method, po, so);
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
            AddRuntime(registry, key, (ref byte bb, ref BehaviorTreeState st, ref BTreeContext ctx, int _) => logic(ctx.Self, ctx.World));
            PairDeactivator<TBB, byte, byte>(registry, key, logic.Method, offset: null, stateOffset: null);
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
            AddRuntime(registry, key, (ref byte bb, ref BehaviorTreeState st, ref BTreeContext ctx, int _) =>
                logic(ctx.Self, ctx.World) ? NodeStatus.Success : NodeStatus.Failure);
            PairDeactivator<TBB, byte, byte>(registry, key, logic.Method, offset: null, stateOffset: null);
            return key;
        }

        // ⚠ The registry key keeps the CLR spelling (a nested type's '+'), exactly as before slice 4; only the deactivator
        //   PAIRING normalises ('+' → '.') so a target written either way matches.
        private static string Fqn(Delegate logic) => $"{logic.Method.DeclaringType!.FullName}.{logic.Method.Name}";

        private static string MethodFqn(MethodInfo m) => $"{m.DeclaringType!.FullName!.Replace('+', '.')}.{m.Name}";

        /// <summary>
        /// ⭐ <c>CE-504</c> slice 4 — the method a <c>[BTreeDeactivator]</c>'s target names. The target is the action
        /// method's FQN; a legacy <c>@offset</c> suffix (the pre-slice-4 key form) is tolerated and ignored, because the
        /// pairing is now per BINDING — the deactivator is registered under every key its action is bound at.
        /// </summary>
        public static string DeactivatorTargetMethod(string targetAction)
        {
            if (targetAction == null) throw new ArgumentNullException(nameof(targetAction));
            int at = targetAction.IndexOf('@');
            return (at < 0 ? targetAction : targetAction.Substring(0, at)).Replace('+', '.');
        }

        /// <summary>
        /// ⭐ <c>CE-504</c> slice 4 — the deactivator declared BESIDE <paramref name="action"/> (same type) whose
        /// <c>[BTreeDeactivator]</c> targets it, or <c>null</c>. ⭐ The JSON scanner applies the same rule.
        /// </summary>
        public static MethodInfo? FindDeactivator(MethodInfo action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            string fqn = MethodFqn(action);
            foreach (var m in action.DeclaringType!.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var attr = m.GetCustomAttribute<BTreeDeactivatorAttribute>();
                if (attr != null && string.Equals(DeactivatorTargetMethod(attr.TargetAction), fqn, StringComparison.Ordinal))
                    return m;
            }
            return null;
        }

        /// <summary>
        /// Registers <paramref name="action"/>'s paired deactivator (if any) under <paramref name="key"/> in both registries, with
        /// the action's own projection. <paramref name="offset"/> is null for a param-less action, <paramref name="stateOffset"/>
        /// null unless the action is stateful. ⛔ A deactivator whose form the binding cannot feed throws here, at build time.
        /// </summary>
        private static void PairDeactivator<TBB, TParams, TWorkingState>(
            ActionRegistry<TBB, BTreeContext> registry, string key, MethodInfo action, nint? offset, nint? stateOffset)
            where TBB : struct where TParams : unmanaged where TWorkingState : unmanaged
        {
            var d = FindDeactivator(action);
            if (d == null) return;
            var ps = d.GetParameters();

            if (ps.Length == 2)
            {
                var f = Bind<SharedNodeNoParamsDeactivator>(d, action);
                registry.RegisterDeactivator(key, (ref TBB bb, ref BehaviorTreeState st, ref BTreeContext ctx, int _) => f(ctx.Self, ctx.World));
                AddRuntimeDeactivator(registry, key, (ref byte bb, ref BehaviorTreeState st, ref BTreeContext ctx, int _) => f(ctx.Self, ctx.World));
                return;
            }
            if (ps.Length == 3 && offset is nint o)
            {
                var f = Bind<SharedNodeDeactivator<TParams>>(d, action);
                registry.RegisterDeactivator(key, (ref TBB bb, ref BehaviorTreeState st, ref BTreeContext ctx, int _) =>
                    f(ref Unsafe.As<TBB, TParams>(ref Unsafe.AddByteOffset(ref bb, o)), ctx.Self, ctx.World));
                AddRuntimeDeactivator(registry, key, (ref byte bb, ref BehaviorTreeState st, ref BTreeContext ctx, int _) =>
                    f(ref Unsafe.As<byte, TParams>(ref Unsafe.AddByteOffset(ref BehaviorBlock.Require(ref bb), o)), ctx.Self, ctx.World));
                return;
            }
            if (ps.Length == 4 && offset is nint po && stateOffset is nint so)
            {
                var f = Bind<SharedNodeStatefulDeactivator<TParams, TWorkingState>>(d, action);
                registry.RegisterDeactivator(key, (ref TBB bb, ref BehaviorTreeState st, ref BTreeContext ctx, int _) =>
                    f(ref Unsafe.As<TBB, TParams>(ref Unsafe.AddByteOffset(ref bb, po)),
                      ref Unsafe.As<TBB, TWorkingState>(ref Unsafe.AddByteOffset(ref bb, so)), ctx.Self, ctx.World));
                AddRuntimeDeactivator(registry, key, (ref byte bb, ref BehaviorTreeState st, ref BTreeContext ctx, int _) =>
                    f(ref Unsafe.As<byte, TParams>(ref Unsafe.AddByteOffset(ref BehaviorBlock.Require(ref bb), po)),
                      ref Unsafe.As<byte, TWorkingState>(ref Unsafe.AddByteOffset(ref BehaviorBlock.Require(ref bb), so)),
                      ctx.Self, ctx.World));
                return;
            }
            throw new ArgumentException(
                $"Deactivator '{MethodFqn(d)}' does not take a form its action '{MethodFqn(action)}' can feed: use (Entity, EntityRepository), " +
                "(ref TParams, Entity, EntityRepository) for an action with params, or (ref TParams, ref TWorkingState, Entity, " +
                "EntityRepository) for a stateful action (CE-504).");
        }

        private static T Bind<T>(MethodInfo deactivator, MethodInfo action) where T : Delegate
            => (T?)Delegate.CreateDelegate(typeof(T), deactivator, throwOnBindFailure: false)
               ?? throw new ArgumentException(
                   $"Deactivator '{MethodFqn(deactivator)}' does not match the {typeof(T).Name} form its action '{MethodFqn(action)}' " +
                   "needs — its parameter types must be the action's own (CE-504).");

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
