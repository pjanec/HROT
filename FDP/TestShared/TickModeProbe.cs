using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.TestShared
{
    /// <summary>
    /// ⭐ <c>CE-3293</c> S1 (R-3036) — walks every production assembly beside the test (<c>Fdp.*</c>, <c>Hrot.*</c>, <c>HrotStrideApp.*</c>;
    /// not a test assembly) and asks each concrete system and module for its <see cref="TickPolicy"/>. Linked into the test projects
    /// whose output holds the production assemblies: the runner's (most of the tree) and the Stride game's.
    /// 📄 <c>docs/blueprints/DESIGN_Paused_And_Stepped_Execution.md</c> §1 rule 1.
    /// </summary>
    public static class TickModeProbe
    {
        public sealed record Result(int Systems, int Modules, IReadOnlyList<string> Missing, IReadOnlyList<string> Assemblies);

        public static Result Walk()
        {
            int systems = 0, modules = 0;
            var missing = new List<string>();
            var assemblies = new List<string>();
            foreach (var asm in ProductionAssemblies())
            {
                assemblies.Add(asm.GetName().Name!);
                foreach (var type in LoadableTypes(asm))
                {
                    if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters) continue;
                    if (typeof(IEcsModuleSystem).IsAssignableFrom(type))
                    {
                        systems++;
                        try { TickModes.OfSystem(type); } catch (InvalidOperationException) { missing.Add("system " + type.FullName); }
                    }
                    if (typeof(IEcsModule).IsAssignableFrom(type))
                    {
                        modules++;
                        try { TickModes.OfModule(type); } catch (InvalidOperationException) { missing.Add("module " + type.FullName); }
                    }
                }
            }
            return new Result(systems, modules, missing, assemblies);
        }

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
