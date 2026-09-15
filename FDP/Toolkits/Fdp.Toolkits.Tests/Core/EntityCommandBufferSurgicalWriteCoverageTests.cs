using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Fdp.Interfaces;
using Xunit;

namespace Fdp.Toolkit.Tests.Core;

/// <summary>
/// ⭐⭐⭐ <b>Every NON-TEST <see cref="IEntityCommandBuffer"/> implementer overrides
/// <c>SetComponentFieldRaw</c>.</b>
///
/// <para>
/// 🔴 <b>What the default implementation buys, and what it costs.</b> <c>SetComponentFieldRaw</c> was
/// added with a <b>default interface implementation that throws</b>, because the interface has 12
/// implementers and nine of them are test mocks that would otherwise each grow a body for a method
/// they never call. ⛔ <b>The cost is that a NEW production wrapper compiles clean while inheriting a
/// throw</b> — and it throws at the first surgical write, not at build time.
/// </para>
///
/// <para>
/// ⭐⭐ <b>This is a DISCOVERY rail, not a checklist.</b> It walks the loaded closure and finds
/// implementers rather than naming them, so a wrapper added tomorrow is caught by a test written
/// today. ⚠ The mocks are exempt <b>by assembly</b> (<c>*.Tests</c>), not by name — a production type
/// cannot opt itself out by being called <c>FakeSomething</c>.
/// </para>
///
/// <para>
/// ⚠⚠ <b>The closure boundary, stated rather than hidden.</b> This suite sees <c>Fdp.Core</c> and
/// <c>Fdp.Toolkits</c>; it does <b>not</b> see <c>Hrot.SimHost</c>, whose
/// <c>CognitiveSpatialModule.PerceptionScopedCommandBuffer</c> is the third production implementer.
/// That one currently delegates correctly (verified at
/// <c>Hrot/Subsystems/Hrot.SimHost/Modules/CognitiveSpatialModule.cs:145</c>) but is <b>outside this
/// rail</b> — covering it needs the same scan run from a suite whose closure includes SimHost.
/// ⭐ Recorded so the gap reads as a known boundary rather than as coverage.
/// </para>
/// </summary>
public sealed class EntityCommandBufferSurgicalWriteCoverageTests
{
    /// <summary>
    /// Loads every non-framework assembly sitting next to the test binary, so the scan sees the whole
    /// build closure and not merely what happens to be JIT-loaded at the moment the test runs.
    /// </summary>
    private static IReadOnlyList<Assembly> ClosureAssemblies()
    {
        var dir = Path.GetDirectoryName(typeof(EntityCommandBufferSurgicalWriteCoverageTests).Assembly.Location)!;
        var result = new List<Assembly>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.dll"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.StartsWith("System.", StringComparison.Ordinal) ||
                name.StartsWith("Microsoft.", StringComparison.Ordinal) ||
                name.StartsWith("xunit", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("runtime.", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Newtonsoft.", StringComparison.Ordinal))
                continue;

            try { result.Add(Assembly.LoadFrom(path)); }
            catch (BadImageFormatException) { /* native or non-managed payload */ }
            catch (FileLoadException)       { /* already loaded from another path */ }
        }
        return result;
    }

    private static bool IsTestAssembly(Assembly a)
    {
        var name = a.GetName().Name ?? string.Empty;
        return name.EndsWith(".Tests", StringComparison.Ordinal)
            || name.EndsWith(".Test", StringComparison.Ordinal);
    }

    private static IEnumerable<Type> ProductionImplementers()
    {
        foreach (var asm in ClosureAssemblies())
        {
            if (IsTestAssembly(asm)) continue;

            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }

            foreach (var t in types)
            {
                if (t is null || t.IsInterface || t.IsAbstract) continue;
                if (!typeof(IEntityCommandBuffer).IsAssignableFrom(t)) continue;
                yield return t;
            }
        }
    }

    /// <summary>
    /// ⛔ <b>Fails if the scan finds nothing.</b> A rail that discovers zero implementers is a rail that
    /// cannot fail — the assertion below would be vacuously satisfied forever.
    /// </summary>
    [Fact]
    public void TheScanFindsTheRealBuffer_SoTheRailIsNotVacuous()
    {
        var found = ProductionImplementers().ToList();
        Assert.Contains(typeof(EntityCommandBuffer), found);
        Assert.True(found.Count >= 2,
            "expected at least the real buffer and one production wrapper; found: "
            + string.Join(", ", found.Select(t => t.FullName)));
    }

    /// <summary>
    /// ⭐⭐ <b>The rail.</b> An implementer whose interface map still points at
    /// <see cref="IEntityCommandBuffer"/> is inheriting the throwing default — which means a surgical
    /// field write through it is a runtime <c>NotSupportedException</c>, i.e. a lost edit reported as
    /// a crash rather than caught at build time.
    /// </summary>
    [Fact]
    public void EveryProductionImplementer_OverridesSetComponentFieldRaw()
    {
        var target = typeof(IEntityCommandBuffer).GetMethod(nameof(IEntityCommandBuffer.SetComponentFieldRaw));
        Assert.NotNull(target);

        var inheritingTheThrow = new List<string>();
        foreach (var t in ProductionImplementers())
        {
            var map   = t.GetInterfaceMap(typeof(IEntityCommandBuffer));
            int index = Array.IndexOf(map.InterfaceMethods, target);
            if (index < 0) continue;   // not present in the map at all — nothing to judge

            if (map.TargetMethods[index].DeclaringType == typeof(IEntityCommandBuffer))
                inheritingTheThrow.Add(t.FullName!);
        }

        Assert.True(inheritingTheThrow.Count == 0,
            "these production IEntityCommandBuffer implementers inherit the THROWING default "
            + "SetComponentFieldRaw and will fail at the first surgical field write: "
            + string.Join(", ", inheritingTheThrow));
    }
}
