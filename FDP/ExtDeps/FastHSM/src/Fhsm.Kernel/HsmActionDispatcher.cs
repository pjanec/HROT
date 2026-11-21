using System;
using System.Collections.Generic;
using Fhsm.Kernel.Data;

namespace Fhsm.Kernel
{
    public static unsafe class HsmActionDispatcher
    {
        private static readonly Dictionary<ushort, IntPtr> ActionTable = new()
        {
        };

        private static readonly Dictionary<ushort, IntPtr> GuardTable = new()
        {
        };

        /// <summary>⭐ <c>CE-3163</c> — an action registered as a managed delegate (a fluent binding: its closure carries the
        /// field offsets a static function pointer cannot). Same signature as the pointer table's.</summary>
        public delegate void ManagedAction(void* instance, void* context, HsmCommandWriter* writer);

        /// <summary>⭐ <c>CE-3163</c> — a guard registered as a managed delegate.</summary>
        public delegate bool ManagedGuard(void* instance, void* context, ushort eventId, HsmCommandWriter* writer);

        private static readonly Dictionary<ushort, ManagedAction> ManagedActionTable = new();
        private static readonly Dictionary<ushort, ManagedGuard> ManagedGuardTable = new();

        public static void ExecuteAction(ushort actionId, void* instance, void* context, HsmCommandWriter* writer)
        {
            if (ActionTable.TryGetValue(actionId, out var actionPtr))
                ((delegate* <void*, void*, HsmCommandWriter*, void>)actionPtr)(instance, context, writer);
            else if (ManagedActionTable.TryGetValue(actionId, out var managed))   // ⭐ CE-3163 — a fluent binding
                managed(instance, context, writer);
        }

        /// <summary>
        /// O6 / <c>D2</c> — the guard signature carries the <see cref="HsmCommandWriter"/>, so a
        /// guard learns which occurrence it is exactly as an action does.
        /// <para>
        /// ⚠ This is a SIGNATURE change, and it is safe for one reason only: every guard pointer in
        /// this table is registered from EMITTED source (<c>HsmActionGenerator</c>,
        /// <c>AiPrimitiveEmitter</c>, <c>CSharpEmitter</c>), and emitted behaviour source is
        /// machine-owned and regenerated whole on save (<c>R-50</c>) — so this is a rebuild, not a
        /// migration. ⛔ Not because the population is small.
        /// </para>
        /// </summary>
        public static bool EvaluateGuard(ushort guardId, void* instance, void* context, ushort eventId, HsmCommandWriter* writer)
        {
            if (GuardTable.TryGetValue(guardId, out var guardPtr))
                return ((delegate* <void*, void*, ushort, HsmCommandWriter*, bool>)guardPtr)(instance, context, eventId, writer);
            if (ManagedGuardTable.TryGetValue(guardId, out var managed))   // ⭐ CE-3163 — a fluent binding
                return managed(instance, context, eventId, writer);
            return true; // No guard = always pass
        }

        /// <summary>
        /// ⭐ <c>CE-442</c> — every WRITE to the two tables takes this lock, so two registrars running at
        /// once (parallel test classes today; any concurrent loader tomorrow) cannot corrupt a table.
        /// <para>⛔ The READ side (<see cref="ExecuteAction"/> / <see cref="EvaluateGuard"/>) is deliberately
        /// NOT locked: it is the per-tick hot path, and production registers everything before the first
        /// tick. ⚠ This does not make a registration concurrent with a TICK safe — a reload that re-registers
        /// on another thread while ticks run would need its own design.</para>
        /// </summary>
        private static readonly object WriteLock = new();

        public static void RegisterAction(ushort id, IntPtr action) { lock (WriteLock) ActionTable[id] = action; }
        public static void RegisterGuard(ushort id, IntPtr guard)   { lock (WriteLock) GuardTable[id] = guard; }

        /// <summary>⭐ <c>CE-3163</c> — register a managed action (the table keeps the delegate alive). ⚠ A function pointer
        /// under the same id wins; the fluent binder's ids are compound keys, so they do not meet an emitted one.</summary>
        public static void RegisterManagedAction(ushort id, ManagedAction action)
        { if (action == null) throw new ArgumentNullException(nameof(action)); lock (WriteLock) ManagedActionTable[id] = action; }

        /// <summary>⭐ <c>CE-3163</c> — register a managed guard.</summary>
        public static void RegisterManagedGuard(ushort id, ManagedGuard guard)
        { if (guard == null) throw new ArgumentNullException(nameof(guard)); lock (WriteLock) ManagedGuardTable[id] = guard; }

        public static void ClearAll()
        {
            lock (WriteLock)
            {
                ActionTable.Clear();
                GuardTable.Clear();
                ManagedActionTable.Clear();
                ManagedGuardTable.Clear();
            }
        }
    }
}
