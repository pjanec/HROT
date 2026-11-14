using System;
using System.Collections.Generic;
using System.Threading;
using System.Reflection;

namespace Fdp.Toolkit.Utility
{
    /// <summary>
    /// ⭐ <c>CE-1325</c> — an input id's NAME, from the generated id tables (<see cref="StandardInputIds"/>, <see cref="SquadInputIds"/>): the
    /// one lookup the decision view and the AI debug API read (moved from <c>DebugApiService.Utility</c>, CE-3069). Unknown ⇒ the hex id.
    /// </summary>
    public static class UtilityInputNames
    {
        // ⭐ POC 2 / P-2 (A-2 finding) — built exactly once, thread-safe: the old check-then-build let two threads each scan the id tables.
        // (Not readonly: the race rail resets it by reflection. Production never reassigns it.)
        private static Lazy<Dictionary<ushort, string>> _names = NewTable();

        /// <summary>A fresh, thread-safe lazy table (the rail resets <c>_names</c> with one).</summary>
        internal static Lazy<Dictionary<ushort, string>> NewTable() => new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>How many times the id tables were scanned — a rail asserts one per reset, whatever the thread count.</summary>
        internal static int BuildCount;

        private static Dictionary<ushort, string> Build()
        {
            Interlocked.Increment(ref BuildCount);
            var names = new Dictionary<ushort, string>();
            foreach (var t in new[] { typeof(StandardInputIds), typeof(SquadInputIds) })
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
                    if (f.IsLiteral && f.FieldType == typeof(ushort))
                        names.TryAdd((ushort)f.GetRawConstantValue()!, f.Name);
            return names;
        }

        public static string Of(ushort inputId)
        {
            return _names.Value.TryGetValue(inputId, out var n) ? n : $"0x{inputId:X4}";
        }
    }
}
