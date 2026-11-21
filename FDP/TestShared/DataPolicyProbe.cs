using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fdp.Core;

namespace Fdp.TestShared
{
    /// <summary>
    /// ⭐ <c>CE-3299</c> D7 (R-3046) — walks every production assembly (the same walk as <see cref="TickModeProbe"/>) for the types the ECS
    /// can register — every one carries <c>[ComponentId]</c>, the registry refuses any other (<c>ComponentType.cs</c>) — and lists those
    /// that declare no <c>[DataPolicy]</c>. A type without one is recorded, saved into scenarios and snapshotted BY DEFAULT: right for most
    /// simulation state, wrong for content and services (the TKB and the navmesh provider were written into every keyframe until D7), so
    /// a new type must say which it is. Requires the test project to link <c>TickModeProbe.cs</c> too.
    /// 📄 <c>docs/DESIGN_Checkpoint_And_Dry_Run_Restore.md</c> D7.
    /// </summary>
    public static class DataPolicyProbe
    {
        public sealed record Result(int Types, IReadOnlyList<string> Undeclared, IReadOnlyList<string> Assemblies);

        public static Result Walk()
        {
            int types = 0;
            var undeclared = new List<string>();
            var assemblies = new List<string>();
            foreach (var asm in TickModeProbe.ProductionAssemblies())
            {
                assemblies.Add(asm.GetName().Name!);
                foreach (var type in TickModeProbe.LoadableTypes(asm))
                {
                    if (type.GetCustomAttribute<ComponentIdAttribute>(inherit: false) == null) continue;
                    types++;
                    if (type.GetCustomAttribute<DataPolicyAttribute>(inherit: false) == null) undeclared.Add(type.FullName!);
                }
            }
            undeclared.Sort(StringComparer.Ordinal);
            return new Result(types, undeclared, assemblies);
        }
    }
}
