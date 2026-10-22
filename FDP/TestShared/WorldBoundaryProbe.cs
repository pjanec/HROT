using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.TestShared
{
    /// <summary>
    /// ⭐ <c>CE-3294</c> — walks every production assembly beside the test (as <see cref="TickModeProbe"/> does) and finds each concrete system
    /// that keeps ENTITIES in its own instance fields (a collection, tuple or array holding <see cref="Entity"/>). A system instance outlives a
    /// reload, and the editor's reload (<c>SoftClear</c>) hands the next run's entities the SAME handles — so such a system must forget at the
    /// world boundary: it calls <c>WorldEpoch.Moved</c> (read from its compiled code, its base classes included), or every such field carries
    /// a written exemption (scratch rebuilt each frame). 📄 <c>docs/blueprints/DESIGN_Paused_And_Stepped_Execution.md</c> §7f.
    /// </summary>
    public static class WorldBoundaryProbe
    {
        public sealed record Result(int Systems, int Holding, IReadOnlyList<string> Missing, IReadOnlyList<string> UnusedExemptions);

        /// <summary>Walks the tree. <paramref name="exempt"/>: <c>"Full.Type.Name._field"</c> → why it needs no reset.</summary>
        public static Result Walk(IReadOnlyDictionary<string, string> exempt)
        {
            int systems = 0, holding = 0;
            var missing = new List<string>();
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var asm in ProductionAssemblies())
                foreach (var type in LoadableTypes(asm))
                {
                    if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters) continue;
                    if (!typeof(IEcsModuleSystem).IsAssignableFrom(type)) continue;
                    systems++;
                    var fields = EntityFields(type).ToList();
                    if (fields.Count == 0) continue;
                    holding++;
                    var open = new List<string>();
                    foreach (var f in fields)
                    {
                        string key = f.DeclaringType!.FullName + "." + f.Name;
                        if (exempt.ContainsKey(key)) used.Add(key); else open.Add(f.Name);
                    }
                    if (open.Count > 0 && !CallsWorldEpochMoved(type))
                        missing.Add($"{type.FullName} [{string.Join(", ", open)}]");
                }
            return new Result(systems, holding, missing, exempt.Keys.Where(k => !used.Contains(k)).ToList());
        }

        /// <summary>The instance fields of <paramref name="type"/> and its bases whose type holds an <see cref="Entity"/>.</summary>
        public static IEnumerable<FieldInfo> EntityFields(Type type)
        {
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (f.FieldType != typeof(Entity) && !typeof(Delegate).IsAssignableFrom(f.FieldType) && Holds(f.FieldType, 0)) yield return f;   // a callback is no state
        }

        private static bool Holds(Type t, int depth)
        {
            if (depth > 4) return false;
            if (t == typeof(Entity)) return true;
            if (t.IsArray) return Holds(t.GetElementType()!, depth + 1);
            return t.IsGenericType && t.GetGenericArguments().Any(a => Holds(a, depth + 1));
        }

        /// <summary>Does any method of <paramref name="type"/> or its bases call <c>WorldEpoch.Moved</c>? Read from the IL.</summary>
        public static bool CallsWorldEpochMoved(Type type)
        {
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
                foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (Calls(m, "Fdp.Toolkit.Replication.Services.WorldEpoch", "Moved")) return true;
            return false;
        }

        private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);

        private static bool Calls(MethodInfo method, string declaringType, string name)
        {
            byte[]? il;
            try { il = method.GetMethodBody()?.GetILAsByteArray(); } catch { return false; }
            if (il == null) return false;
            int i = 0;
            while (i < il.Length)
            {
                short value = il[i] == 0xFE && i + 1 < il.Length ? (short)(0xFE00 | il[i + 1]) : il[i];
                if (!OpCodesByValue.TryGetValue(value, out var op)) return false;   // unknown byte: stop reading this body
                i += op.Size;
                if (op.OperandType == OperandType.InlineMethod && (op == OpCodes.Call || op == OpCodes.Callvirt))
                {
                    int token = BitConverter.ToInt32(il, i);
                    try
                    {
                        var target = method.Module.ResolveMethod(token);
                        if (target?.Name == name && target.DeclaringType?.FullName == declaringType) return true;
                    }
                    catch (ArgumentException) { }
                }
                i += OperandSize(op, il, i);
            }
            return false;
        }

        private static int OperandSize(OpCode op, byte[] il, int at) => op.OperandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, at),
            _ => 4,
        };

        private static IEnumerable<Assembly> ProductionAssemblies()
        {
            var dir = AppContext.BaseDirectory;
            foreach (var path in Directory.EnumerateFiles(dir, "*.dll").OrderBy(p => p, StringComparer.Ordinal))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                bool ours = name.StartsWith("Fdp.", StringComparison.Ordinal) || name.StartsWith("Hrot.", StringComparison.Ordinal)
                            || name.StartsWith("HrotStrideApp.", StringComparison.Ordinal);
                if (!ours || name.Contains("Test", StringComparison.Ordinal) || name.Contains("Example", StringComparison.Ordinal)) continue;
                Assembly? asm = null;
                try { asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name) ?? Assembly.LoadFrom(path); }
                catch (BadImageFormatException) { }
                if (asm != null) yield return asm;
            }
        }

        private static IEnumerable<Type> LoadableTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null)!; }
        }
    }
}
