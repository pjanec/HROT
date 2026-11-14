using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Xunit;

namespace Fdp.Toolkit.Tests.Core
{
    /// <summary>
    /// ⭐ <c>CE-3132</c> — a component that holds native memory is never recorded or saved. Its <c>NativeArray</c> (or raw pointer)
    /// is memory of THIS process: recorded, it carried the recording process's pointers into a replay, which restored them into a
    /// world that later code reads. 📌 Found as <c>ZoneEnvironmentData</c> (CE-3118, the road blob the Replay Browser's road layer
    /// draws), then four more by a sweep (<c>PathfindingBatchData</c>, <c>RaycastBatchData</c>, <c>TerrainQueryBatchData</c>,
    /// <c>EqsResultPool</c>). ⭐ One reflection rail so the next one cannot slip in. 📄 docs/DESIGN_Geo_Origin.md §5.6.
    /// </summary>
    public sealed class NativeMemoryIsNeverRecordedTests
    {
        private static readonly Assembly[] Scanned =
        {
            typeof(EntityRepository).Assembly,                       // Fdp.Core
            typeof(global::CarKinem.Road.ZoneEnvironmentData).Assembly, // Fdp.Toolkits
        };

        [Fact]
        public void CE3132_EveryComponentHoldingNativeMemory_IsNoReplayAndNoScenario()
        {
            var components = Scanned.SelectMany(SafeTypes)
                .Where(t => t.IsValueType && !t.IsEnum && t.GetCustomAttribute<ComponentIdAttribute>() != null)
                .ToList();
            Assert.True(components.Count > 50, $"the scan found only {components.Count} component structs — the rail proves nothing");

            var holders = components.Where(t => HoldsNativeMemory(t, new HashSet<Type>())).ToList();
            Assert.Contains(typeof(global::CarKinem.Road.ZoneEnvironmentData), holders);   // the scan sees the known case

            var offenders = holders
                .Where(t =>
                {
                    var policy = t.GetCustomAttribute<DataPolicyAttribute>()?.Policy ?? DataPolicy.Default;
                    return !policy.HasFlag(DataPolicy.NoReplay) || !policy.HasFlag(DataPolicy.NoScenario);
                })
                .Select(t => t.FullName)
                .ToList();
            Assert.True(offenders.Count == 0,
                "these components hold native memory but would be recorded or saved (add [DataPolicy(DataPolicy.NoScenario | "
                + "DataPolicy.NoReplay)]): " + string.Join(", ", offenders));
        }

        private static IEnumerable<Type> SafeTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null)!; }
        }

        /// <summary>A pointer, an <c>IntPtr</c>, a <c>Fdp.Core.Collections.Native*</c> collection, or a struct field that holds one.</summary>
        private static bool HoldsNativeMemory(Type t, HashSet<Type> seen)
        {
            if (!seen.Add(t)) return false;
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var ft = f.FieldType;
                if (ft.IsPointer || ft == typeof(IntPtr) || ft == typeof(UIntPtr)) return true;
                if (ft.IsGenericType && ft.Namespace == "Fdp.Core.Collections" && ft.Name.StartsWith("Native", StringComparison.Ordinal))
                    return true;
                if (ft.IsValueType && !ft.IsPrimitive && !ft.IsEnum && HoldsNativeMemory(ft, seen)) return true;
            }
            return false;
        }
    }
}
