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

        public static void ExecuteAction(ushort actionId, void* instance, void* context, HsmCommandWriter* writer)
        {
            if (ActionTable.TryGetValue(actionId, out var actionPtr))
                ((delegate* <void*, void*, HsmCommandWriter*, void>)actionPtr)(instance, context, writer);
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
            return true; // No guard = always pass
        }

        public static void RegisterAction(ushort id, IntPtr action) => ActionTable[id] = action;
        public static void RegisterGuard(ushort id, IntPtr guard) => GuardTable[id] = guard;

        public static void ClearAll()
        {
            ActionTable.Clear();
            GuardTable.Clear();
        }
    }
}
