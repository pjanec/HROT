using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Fdp.Core;

namespace Fdp.TestShared
{
    /// <summary>
    /// ⭐ <c>CE-4046</c> / <c>CE-3309</c> (docs/DESIGN_Replay_Census_H0.md §2) — walks every production assembly (the same walk as
    /// <see cref="TickModeProbe"/>) for the RECORDED managed component types (a class with <c>[ComponentId]</c> whose policy lacks
    /// <c>NoReplay</c>) and lists each that is not immutable. 🔒 User, 2026-10-25: <i>"a managed component (besides singleton) should never be
    /// changed via read only accessor … The managed components was meant to be csharp records"</i>. ⛔ Why it matters: the recorder writes a
    /// managed chunk only when its version moved (<c>RecorderSystem.RecordManagedTable</c>), so an object changed IN PLACE never reaches a
    /// delta frame — a replay or seek between keyframes shows the old value. An immutable record cannot be changed in place: a change is a
    /// new value through <c>with</c> + <c>Set</c>, which bumps the chunk.
    /// <para>Immutable here means: a C# <c>record</c>; no settable field (every instance field <c>readonly</c>); no property with a
    /// non-<c>init</c> setter; no member of a mutable collection type (an array, <c>List</c>, <c>Dictionary</c>, <c>HashSet</c>, …) —
    /// <c>IReadOnly*</c> and <c>System.Collections.Immutable</c> types are allowed. Requires the test project to link
    /// <c>TickModeProbe.cs</c> too.</para>
    /// </summary>
    public static class ManagedComponentImmutabilityProbe
    {
        public sealed record Violation(string Type, IReadOnlyList<string> Reasons);
        public sealed record Result(int Types, IReadOnlyList<Violation> Violations);

        public static Result Walk()
        {
            int types = 0;
            var violations = new List<Violation>();
            foreach (var asm in TickModeProbe.ProductionAssemblies())
            foreach (var type in TickModeProbe.LoadableTypes(asm))
            {
                if (type.IsValueType || type.IsInterface || type.IsAbstract) continue;
                if (type.GetCustomAttribute<ComponentIdAttribute>(inherit: false) == null) continue;
                var policy = type.GetCustomAttribute<DataPolicyAttribute>(inherit: false)?.Policy ?? DataPolicy.Default;
                if (policy.HasFlag(DataPolicy.NoReplay)) continue;
                types++;
                var reasons = Reasons(type);
                if (reasons.Count > 0) violations.Add(new Violation(type.FullName!, reasons));
            }
            violations.Sort((a, b) => StringComparer.Ordinal.Compare(a.Type, b.Type));
            return new Result(types, violations);
        }

        public static List<string> Reasons(Type type)
        {
            var reasons = new List<string>();
            if (!ComponentTypeRegistry.IsRecordType(type)) reasons.Add("not a record");
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var f in t.GetFields(all))
                {
                    if (f.Name == "<EqualityContract>k__BackingField") continue;
                    var name = BackingName(f.Name);
                    bool backing = f.Name.StartsWith("<", StringComparison.Ordinal);   // a property's: its setter is judged below
                    if (!f.IsInitOnly && !backing) reasons.Add($"settable field {name}");
                    if (IsMutableCollection(f.FieldType)) reasons.Add($"mutable collection {name}: {Pretty(f.FieldType)}");
                }
                foreach (var p in t.GetProperties(all))
                {
                    if (p.SetMethod is { } set && !IsInit(set)) reasons.Add($"settable property {p.Name}");
                }
            }
            return reasons.Distinct().ToList();
        }

        private static bool IsInit(MethodInfo setter)
            => setter.ReturnParameter.GetRequiredCustomModifiers().Any(m => m == typeof(IsExternalInit));

        private static bool IsMutableCollection(Type t)
        {
            if (t == typeof(string)) return false;
            if (t.IsArray) return true;
            if (t.Namespace == "System.Collections.Immutable") return false;
            if (t.IsInterface && t.Name.StartsWith("IReadOnly", StringComparison.Ordinal)) return false;
            if (t.IsInterface && t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>)) return false;
            return typeof(System.Collections.IEnumerable).IsAssignableFrom(t);
        }

        private static string BackingName(string field)
            => field.StartsWith("<", StringComparison.Ordinal) && field.IndexOf('>') is var i and > 0 ? field.Substring(1, i - 1) : field;

        private static string Pretty(Type t)
            => !t.IsGenericType ? t.Name : $"{t.Name.Split('`')[0]}<{string.Join(", ", t.GetGenericArguments().Select(Pretty))}>";
    }
}
