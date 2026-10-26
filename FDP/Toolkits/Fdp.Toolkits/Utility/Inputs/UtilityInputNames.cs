using System.Collections.Generic;
using System.Reflection;

namespace Fdp.Toolkit.Utility
{
    /// <summary>
    /// ⭐ <c>CE-1325</c> — an input id's NAME, from the generated id tables (<see cref="StandardInputIds"/>, <see cref="SquadInputIds"/>): the
    /// one lookup the decision view and the AI debug API read (moved from <c>DebugApiService.Utility</c>, CE-3069). Unknown ⇒ the hex id.
    /// </summary>
    public static class UtilityInputNames
    {
        private static Dictionary<ushort, string>? _names;

        public static string Of(ushort inputId)
        {
            if (_names is null)
            {
                var names = new Dictionary<ushort, string>();
                foreach (var t in new[] { typeof(StandardInputIds), typeof(SquadInputIds) })
                    foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
                        if (f.IsLiteral && f.FieldType == typeof(ushort))
                            names.TryAdd((ushort)f.GetRawConstantValue()!, f.Name);
                _names = names;
            }
            return _names.TryGetValue(inputId, out var n) ? n : $"0x{inputId:X4}";
        }
    }
}
