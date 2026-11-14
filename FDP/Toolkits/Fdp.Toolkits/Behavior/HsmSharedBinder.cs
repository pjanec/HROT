using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Systems;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// ⭐⭐⭐ <c>CE-3163</c> — the HSM twin of <see cref="SharedNodeBinder"/>: a hand-written (fluent) HSM binds a shared C# method to
    /// FIELDS of its block — its params and its working state — by selector. 📄 <c>docs/blueprints/DESIGN_Behavior_Action_Binding.md</c>
    /// §5.7 (D3–D6).
    ///
    /// <para>⭐ Same delegate types, same keys (<c>Fqn@paramOffset[@stateOffset]</c>, <see cref="Shared.HsmActionKey.CompoundKeyName(string, long)"/>),
    /// same deactivator pairing as the BTree binder — so one C# method binds on both hosts. The key's id is
    /// <see cref="Shared.HsmActionKey.ForCompoundKey"/>, the flattener's own name hash, so a state that names the key and the
    /// dispatcher entry agree.</para>
    ///
    /// <para>⭐ The binding is a MANAGED DELEGATE (<see cref="HsmActionDispatcher.RegisterManagedAction"/>): its closure carries the
    /// field offsets, which a static function pointer cannot. Each call projects from THIS machine's block
    /// (<see cref="HsmBlock.Require"/>) — the root block at the root, the child's own when hosted — so working state is per
    /// occurrence by construction.</para>
    /// </summary>
    public static unsafe class HsmSharedBinder
    {
        /// <summary>The plain form, projected at the selected field. Key <c>{Fqn}@{offset}</c>.</summary>
        public static string RegisterAction<TBlock, TParams>(Expression<Func<TBlock, TParams>> paramSelector, SharedNodeAction<TParams> logic)
            where TBlock : unmanaged where TParams : unmanaged
        {
            RequireStatic(logic);
            nint po = SharedNodeBinder.FieldOffset(paramSelector);
            string key = Shared.HsmActionKey.CompoundKeyName(Fqn(logic), po);
            HsmActionDispatcher.RegisterManagedAction(Shared.HsmActionKey.ForCompoundKey(key), (instance, context, writer) =>
            {
                byte* b = Block<TBlock>(context, key);
                logic(ref *(TParams*)(b + po), Self(context), World(context));
            });
            return key;
        }

        /// <summary>The <c>bool</c> guard form. Key <c>{Fqn}@{offset}</c>.</summary>
        public static string RegisterCondition<TBlock, TParams>(Expression<Func<TBlock, TParams>> paramSelector, SharedNodeCondition<TParams> logic)
            where TBlock : unmanaged where TParams : unmanaged
        {
            RequireStatic(logic);
            nint po = SharedNodeBinder.FieldOffset(paramSelector);
            string key = Shared.HsmActionKey.CompoundKeyName(Fqn(logic), po);
            HsmActionDispatcher.RegisterManagedGuard(Shared.HsmActionKey.ForCompoundKey(key), (instance, context, eventId, writer) =>
            {
                byte* b = Block<TBlock>(context, key);
                return logic(ref *(TParams*)(b + po), Self(context), World(context));
            });
            return key;
        }

        /// <summary>The stateful form; both halves are fields of the block. Key <c>{Fqn}@{paramOffset}@{stateOffset}</c>.</summary>
        public static string RegisterStatefulAction<TBlock, TParams, TWorkingState>(
            Expression<Func<TBlock, TParams>> paramSelector, Expression<Func<TBlock, TWorkingState>> stateSelector,
            SharedNodeStatefulAction<TParams, TWorkingState> logic)
            where TBlock : unmanaged where TParams : unmanaged where TWorkingState : unmanaged
        {
            RequireStatic(logic);
            nint po = SharedNodeBinder.FieldOffset(paramSelector), so = SharedNodeBinder.FieldOffset(stateSelector);
            string key = Shared.HsmActionKey.CompoundKeyName(Fqn(logic), po, so);
            HsmActionDispatcher.RegisterManagedAction(Shared.HsmActionKey.ForCompoundKey(key), (instance, context, writer) =>
            {
                byte* b = Block<TBlock>(context, key);
                logic(ref *(TParams*)(b + po), ref *(TWorkingState*)(b + so), Self(context), World(context));
            });
            return key;
        }

        /// <summary>The stateful <c>bool</c> guard form. Key <c>{Fqn}@{paramOffset}@{stateOffset}</c>.</summary>
        public static string RegisterStatefulCondition<TBlock, TParams, TWorkingState>(
            Expression<Func<TBlock, TParams>> paramSelector, Expression<Func<TBlock, TWorkingState>> stateSelector,
            SharedNodeStatefulCondition<TParams, TWorkingState> logic)
            where TBlock : unmanaged where TParams : unmanaged where TWorkingState : unmanaged
        {
            RequireStatic(logic);
            nint po = SharedNodeBinder.FieldOffset(paramSelector), so = SharedNodeBinder.FieldOffset(stateSelector);
            string key = Shared.HsmActionKey.CompoundKeyName(Fqn(logic), po, so);
            HsmActionDispatcher.RegisterManagedGuard(Shared.HsmActionKey.ForCompoundKey(key), (instance, context, eventId, writer) =>
            {
                byte* b = Block<TBlock>(context, key);
                return logic(ref *(TParams*)(b + po), ref *(TWorkingState*)(b + so), Self(context), World(context));
            });
            return key;
        }

        /// <summary>
        /// ⭐ D6 — the EXIT for an action bound under <paramref name="key"/>: its <c>[BTreeDeactivator]</c> (the same attribute the
        /// BTree pairs), projected the same way, registered under <see cref="Shared.HsmActionKey.ExitCleanupName"/>; <c>null</c> when
        /// the method has none. CE-3082 D2's rule for a JSON HSM: a state whose activity has a deactivator exits through it.
        /// </summary>
        public static string? RegisterDeactivator<TBlock, TParams, TWorkingState>(string key, Delegate action, nint? paramOffset, nint? stateOffset)
            where TBlock : unmanaged where TParams : unmanaged where TWorkingState : unmanaged
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            MethodInfo? d = SharedNodeBinder.FindDeactivator(action.Method);
            if (d == null) return null;
            string exit = Shared.HsmActionKey.ExitCleanupName(key);
            ushort id = Shared.HsmActionKey.ForCompoundKey(exit);
            var ps = d.GetParameters();
            if (ps.Length == 2)
            {
                var f = (SharedNodeNoParamsDeactivator)Delegate.CreateDelegate(typeof(SharedNodeNoParamsDeactivator), d);
                HsmActionDispatcher.RegisterManagedAction(id, (i, c, w) => f(Self(c), World(c)));
            }
            else if (ps.Length == 3 && paramOffset is nint po)
            {
                var f = (SharedNodeDeactivator<TParams>)Delegate.CreateDelegate(typeof(SharedNodeDeactivator<TParams>), d);
                HsmActionDispatcher.RegisterManagedAction(id, (i, c, w) => f(ref *(TParams*)(Block<TBlock>(c, exit) + po), Self(c), World(c)));
            }
            else if (ps.Length == 4 && paramOffset is nint po2 && stateOffset is nint so)
            {
                var f = (SharedNodeStatefulDeactivator<TParams, TWorkingState>)Delegate.CreateDelegate(typeof(SharedNodeStatefulDeactivator<TParams, TWorkingState>), d);
                HsmActionDispatcher.RegisterManagedAction(id, (i, c, w) =>
                {
                    byte* b = Block<TBlock>(c, exit);
                    f(ref *(TParams*)(b + po2), ref *(TWorkingState*)(b + so), Self(c), World(c));
                });
            }
            else throw new ArgumentException(
                $"Deactivator '{d.DeclaringType!.FullName}.{d.Name}' does not take a form its action can feed (CE-504).");
            return exit;
        }

        /// <summary>The selected field's offset in <typeparamref name="TBlock"/> — the extension needs it for <see cref="RegisterDeactivator{TBlock, TParams, TWorkingState}"/>.</summary>
        public static nint OffsetOf<TBlock, TValue>(Expression<Func<TBlock, TValue>> selector) => SharedNodeBinder.FieldOffset(selector);

        // ── helpers ──────────────────────────────────────────────────────────────────────────────────────────────────

        private static byte* Block<TBlock>(void* context, string key) where TBlock : unmanaged
        {
            byte* b = HsmBlock.Require(context, out int len);
            if (sizeof(TBlock) > len)
                throw new InvalidOperationException(
                    $"CE-3163: '{key}' projects a {typeof(TBlock).Name} ({sizeof(TBlock)} bytes) but the running machine's block holds {len} — " +
                    "the machine's [HsmDefinition(ParamsType = …)] must be the block its bindings select from.");
            return b;
        }

        private static Entity Self(void* context) => ((HsmKernelBridge*)context)->Self;

        private static EntityRepository World(void* context)
            => (EntityRepository)System.Runtime.InteropServices.GCHandle.FromIntPtr(((HsmKernelBridge*)context)->WorldHandle).Target!;

        private static string Fqn(Delegate logic) => $"{logic.Method.DeclaringType!.FullName}.{logic.Method.Name}";

        private static void RequireStatic(Delegate logic)
        {
            if (logic == null) throw new ArgumentNullException(nameof(logic));
            if (logic.Target != null || logic.Method.Name.Contains("<"))
                throw new ArgumentException("Pass a static node method (a method group), not a lambda: the key is the method's FQN.", nameof(logic));
        }
    }
}
