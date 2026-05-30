using Hrot.Blueprints.Core.Compiler.Roslyn;
using Fdp.Toolkit.Blueprints;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Tests for MetadataReferenceResolver — ensures it excludes in-memory assemblies
/// and that Resolve() returns a non-empty collection for a typical runtime.
/// </summary>
public sealed class MetadataReferenceResolverTests
{
    [Fact]
    public void ForRuntimeAssemblies_ReturnsNonEmptyCollection()
    {
        var resolver = MetadataReferenceResolver.ForRuntimeAssemblies(
            AppDomain.CurrentDomain.GetAssemblies());

        var refs = resolver.Resolve();
        Assert.NotNull(refs);
        Assert.True(refs.Count > 0, "Resolver should return at least one reference.");
    }

    [Fact]
    public void ForRuntimeAssemblies_ExcludesAssembliesWithNoLocation()
    {
        var resolver = MetadataReferenceResolver.ForRuntimeAssemblies(
            AppDomain.CurrentDomain.GetAssemblies());

        var refs = resolver.Resolve()
            .Cast<Microsoft.CodeAnalysis.PortableExecutableReference>()
            .ToList();

        // No reference should have a null or empty file path.
        foreach (var r in refs)
        {
            Assert.False(string.IsNullOrEmpty(r.FilePath),
                $"Unexpected reference with empty file path: {r}");
        }
    }

    [Fact]
    public void ForRuntimeAssemblies_IncludesCoreLib()
    {
        var resolver = MetadataReferenceResolver.ForRuntimeAssemblies(
            AppDomain.CurrentDomain.GetAssemblies());

        var paths = resolver.Resolve()
            .Cast<Microsoft.CodeAnalysis.PortableExecutableReference>()
            .Select(r => r.FilePath ?? "")
            .ToList();

        // System.Private.CoreLib.dll or mscorlib.dll should be present.
        var hasCoreLib = paths.Any(p =>
            p.EndsWith("System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase) ||
            p.EndsWith("mscorlib.dll", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasCoreLib, "Resolver should include a core runtime assembly.");
    }

    /// <summary>
    /// BPF-040: ForRuntimeAssemblies must return references sorted by Location so that
    /// repeated calls with the same runtime assemblies produce identical lists.
    /// </summary>
    [Fact]
    public void ForRuntimeAssemblies_ReferencesAreSortedByPath()
    {
        var resolver = MetadataReferenceResolver.ForRuntimeAssemblies(
            AppDomain.CurrentDomain.GetAssemblies());

        var paths = resolver.Resolve()
            .Cast<Microsoft.CodeAnalysis.PortableExecutableReference>()
            .Select(r => r.FilePath ?? "")
            .ToList();

        Assert.True(paths.Count > 1, "Need at least two references to verify sort.");

        for (int i = 1; i < paths.Count; i++)
        {
            int cmp = StringComparer.Ordinal.Compare(paths[i - 1], paths[i]);
            Assert.True(cmp <= 0,
                $"References not sorted: '{paths[i - 1]}' should come before '{paths[i]}'");
        }
    }

    /// <summary>
    /// BPF-040: Two calls with the same assemblies (but potentially different
    /// AppDomain enumeration order) must return identical sorted reference lists.
    /// </summary>
    [Fact]
    public void ForRuntimeAssemblies_TwoCallsProduceSameOrder()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        var r1 = MetadataReferenceResolver.ForRuntimeAssemblies(assemblies)
            .Resolve()
            .Cast<Microsoft.CodeAnalysis.PortableExecutableReference>()
            .Select(r => r.FilePath ?? "")
            .ToList();

        // Reverse the array to simulate a different enumeration order.
        var r2 = MetadataReferenceResolver.ForRuntimeAssemblies(assemblies.Reverse())
            .Resolve()
            .Cast<Microsoft.CodeAnalysis.PortableExecutableReference>()
            .Select(r => r.FilePath ?? "")
            .ToList();

        Assert.Equal(r1, r2);
    }
}
