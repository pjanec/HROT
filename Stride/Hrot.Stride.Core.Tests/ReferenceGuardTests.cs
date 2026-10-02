using System;
using System.Linq;
using System.Reflection;
using Xunit;
using Fdp.Core;

namespace Hrot.Stride.Core.Tests;

/// <summary>
/// Guard tests ensuring Hrot.Stride.Core's assembly reference closure is free of
/// Raylib, rlImGui, and the node composition root — per design §3 / BATCH-01 STR-P0-T1.
///
/// <para>
/// ST-016: the third clause used to name <c>Hrot.StrideMock</c>. That assembly no longer exists, so
/// the clause had gone VACUOUS — it could never match again and would have sat here reading like a
/// live guard. It is repointed at <c>Hrot.NodeComposition</c>, the project StrideNodeBootstrapper
/// moved to, which keeps the clause MEANINGFUL: the invariant was always "the pure Stride core does
/// not reach back into the host that composes nodes", and that host still exists under a new name.
/// Only <c>HrotStrideApp.Game</c> may reference it. The clause was NOT dropped.
/// </para>
/// </summary>
public class ReferenceGuardTests
{
    [Fact]
    public void Hrot_Stride_Core_References_No_Raylib_Or_NodeComposition()
    {
        // Load the real assembly and inspect its referenced assemblies at runtime.
        var asm = typeof(StrideCorePlaceholder).Assembly;
        var refs = asm.GetReferencedAssemblies()
                      .Select(r => r.Name ?? "")
                      .ToArray();

        var forbidden = refs.Where(n =>
            n.Contains("Raylib",      StringComparison.OrdinalIgnoreCase) ||
            n.Contains("rlImGui",     StringComparison.OrdinalIgnoreCase) ||
            n.Contains("NodeComposition", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(
            forbidden.Length == 0,
            $"Hrot.Stride.Core must not reference Raylib, rlImGui, or Hrot.NodeComposition. " +
            $"Found: {string.Join(", ", forbidden)}");
    }

    [Fact]
    public void CrossTfm_Placeholder_Instantiates_With_Both_Fields_Assignable()
    {
        // Prove the cross-TFM type can be constructed and both fields assigned.
        var placeholder = new StrideCorePlaceholder();

        // Stride.Engine.Entity type should be resolvable (Stride, net8.0-windows)
        var strideEntityType = typeof(global::Stride.Engine.Entity);
        Assert.Equal("Entity", strideEntityType.Name);

        // Fdp.Core.EntityRepository type should be resolvable (Fdp.Core, net8.0)
        var fdpRepoType = typeof(EntityRepository);
        Assert.Equal("EntityRepository", fdpRepoType.Name);

        // The fields are of the correct types (proves cross-TFM reference compiled)
        var prop1 = typeof(StrideCorePlaceholder).GetProperty("StrideEntity")!;
        var prop2 = typeof(StrideCorePlaceholder).GetProperty("FdpRepository")!;

        Assert.Equal(typeof(global::Stride.Engine.Entity), prop1.PropertyType);
        Assert.Equal(typeof(EntityRepository), prop2.PropertyType);

        // Assignment compiles and runs without exception
        placeholder.StrideEntity  = null;
        placeholder.FdpRepository = null;
        Assert.Null(placeholder.StrideEntity);
        Assert.Null(placeholder.FdpRepository);
    }
}
