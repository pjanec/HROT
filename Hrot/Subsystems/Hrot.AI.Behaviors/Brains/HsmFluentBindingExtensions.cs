using System;
using System.Linq.Expressions;
using Fdp.Toolkit.Behavior;
using Fhsm.Compiler;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-3163</c> — a hand-written (fluent) HSM binds a shared C# method to fields of its block, the HSM twin of
    /// <see cref="SharedNodeBuilderExtensions"/>: <c>state.Activity(bb =&gt; bb.Order, bb =&gt; bb.State, Run)</c>. The machine's
    /// <c>[HsmDefinition(…, ParamsType = typeof(TBlock))]</c> names the block; each call reads THIS machine's block (the root's, or a
    /// hosted child's own). 📄 <c>docs/blueprints/DESIGN_Behavior_Action_Binding.md</c> §5.7.
    /// <para>⭐ A stateful or plain ACTIVITY whose method has a <c>[BTreeDeactivator]</c> also becomes the state's exit, unless the
    /// state already has one (D6, the CE-3082 D2 rule) — an explicit <c>OnExit(...)</c> after this call still wins (a name replaces it).</para>
    /// </summary>
    public static class HsmFluentBindingExtensions
    {
        // ── activities ───────────────────────────────────────────────────────────────────────────────────────────────

        public static StateBuilder Activity<TBlock, TParams>(this StateBuilder state,
            Expression<Func<TBlock, TParams>> paramSelector, SharedNodeAction<TParams> logic)
            where TBlock : unmanaged where TParams : unmanaged
        {
            string key = HsmSharedBinder.RegisterAction(paramSelector, logic);
            state.RegisterAction(key).Activity(key);
            return PairExit<TBlock, TParams, byte>(state, key, logic, HsmSharedBinder.OffsetOf(paramSelector), null);
        }

        public static StateBuilder Activity<TBlock, TParams, TWorkingState>(this StateBuilder state,
            Expression<Func<TBlock, TParams>> paramSelector, Expression<Func<TBlock, TWorkingState>> stateSelector,
            SharedNodeStatefulAction<TParams, TWorkingState> logic)
            where TBlock : unmanaged where TParams : unmanaged where TWorkingState : unmanaged
        {
            string key = HsmSharedBinder.RegisterStatefulAction(paramSelector, stateSelector, logic);
            state.RegisterAction(key).Activity(key);
            return PairExit<TBlock, TParams, TWorkingState>(state, key, logic,
                HsmSharedBinder.OffsetOf(paramSelector), HsmSharedBinder.OffsetOf(stateSelector));
        }

        // ── entry / exit ─────────────────────────────────────────────────────────────────────────────────────────────

        public static StateBuilder OnEntry<TBlock, TParams>(this StateBuilder state,
            Expression<Func<TBlock, TParams>> paramSelector, SharedNodeAction<TParams> logic)
            where TBlock : unmanaged where TParams : unmanaged
        {
            string key = HsmSharedBinder.RegisterAction(paramSelector, logic);
            return state.RegisterAction(key).OnEntry(key);
        }

        public static StateBuilder OnEntry<TBlock, TParams, TWorkingState>(this StateBuilder state,
            Expression<Func<TBlock, TParams>> paramSelector, Expression<Func<TBlock, TWorkingState>> stateSelector,
            SharedNodeStatefulAction<TParams, TWorkingState> logic)
            where TBlock : unmanaged where TParams : unmanaged where TWorkingState : unmanaged
        {
            string key = HsmSharedBinder.RegisterStatefulAction(paramSelector, stateSelector, logic);
            return state.RegisterAction(key).OnEntry(key);
        }

        public static StateBuilder OnExit<TBlock, TParams>(this StateBuilder state,
            Expression<Func<TBlock, TParams>> paramSelector, SharedNodeAction<TParams> logic)
            where TBlock : unmanaged where TParams : unmanaged
        {
            string key = HsmSharedBinder.RegisterAction(paramSelector, logic);
            state.State.ExitActionId = 0;   // ⭐ an explicit exit replaces a paired deactivator (the id would win over the name)
            return state.RegisterAction(key).OnExit(key);
        }

        public static StateBuilder OnExit<TBlock, TParams, TWorkingState>(this StateBuilder state,
            Expression<Func<TBlock, TParams>> paramSelector, Expression<Func<TBlock, TWorkingState>> stateSelector,
            SharedNodeStatefulAction<TParams, TWorkingState> logic)
            where TBlock : unmanaged where TParams : unmanaged where TWorkingState : unmanaged
        {
            string key = HsmSharedBinder.RegisterStatefulAction(paramSelector, stateSelector, logic);
            state.State.ExitActionId = 0;
            return state.RegisterAction(key).OnExit(key);
        }

        // ── transitions ──────────────────────────────────────────────────────────────────────────────────────────────

        public static TransitionBuilder Guard<TBlock, TParams>(this TransitionBuilder transition,
            Expression<Func<TBlock, TParams>> paramSelector, SharedNodeCondition<TParams> logic)
            where TBlock : unmanaged where TParams : unmanaged
        {
            string key = HsmSharedBinder.RegisterCondition(paramSelector, logic);
            return transition.RegisterGuard(key).Guard(key);
        }

        public static TransitionBuilder Guard<TBlock, TParams, TWorkingState>(this TransitionBuilder transition,
            Expression<Func<TBlock, TParams>> paramSelector, Expression<Func<TBlock, TWorkingState>> stateSelector,
            SharedNodeStatefulCondition<TParams, TWorkingState> logic)
            where TBlock : unmanaged where TParams : unmanaged where TWorkingState : unmanaged
        {
            string key = HsmSharedBinder.RegisterStatefulCondition(paramSelector, stateSelector, logic);
            return transition.RegisterGuard(key).Guard(key);
        }

        public static TransitionBuilder Action<TBlock, TParams>(this TransitionBuilder transition,
            Expression<Func<TBlock, TParams>> paramSelector, SharedNodeAction<TParams> logic)
            where TBlock : unmanaged where TParams : unmanaged
        {
            string key = HsmSharedBinder.RegisterAction(paramSelector, logic);
            return transition.RegisterAction(key).Action(key);
        }

        // ⭐ D6 — the activity's deactivator becomes the exit when the state has none (an exit by name, so the validator sees it).
        private static StateBuilder PairExit<TBlock, TParams, TWorkingState>(StateBuilder state, string key, Delegate logic,
            nint? paramOffset, nint? stateOffset)
            where TBlock : unmanaged where TParams : unmanaged where TWorkingState : unmanaged
        {
            if (state.State.OnExitAction != null || state.State.ExitActionId != 0) return state;
            string? exit = HsmSharedBinder.RegisterDeactivator<TBlock, TParams, TWorkingState>(key, logic, paramOffset, stateOffset);
            return exit == null ? state : state.RegisterAction(exit).OnExit(exit);
        }
    }
}
