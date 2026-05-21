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
}
