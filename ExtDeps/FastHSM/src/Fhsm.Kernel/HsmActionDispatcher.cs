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

        public static bool EvaluateGuard(ushort guardId, void* instance, void* context, ushort eventId)
        {
            if (GuardTable.TryGetValue(guardId, out var guardPtr))
                return ((delegate* <void*, void*, ushort, bool>)guardPtr)(instance, context, eventId);
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
