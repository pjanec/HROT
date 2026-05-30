using Microsoft.CodeAnalysis;
using System.Reflection;

namespace Hrot.Blueprints.Core.Compiler.Roslyn;

public sealed class MetadataReferenceResolver
{
    private readonly IReadOnlyList<MetadataReference> _references;

    public MetadataReferenceResolver(IReadOnlyList<MetadataReference> references)
        => _references = references;

    public IReadOnlyList<MetadataReference> Resolve() => _references;

    /// <summary>
    /// Creates a resolver from assemblies loaded into the current AppDomain.
    /// Filters out dynamic assemblies and assemblies with no on-disk location
    /// (Patch 2: BOTH predicates required -- IsDynamic catches codegen assemblies;
    /// Location=="" catches collectible ALC assemblies that are NOT IsDynamic).
    /// </summary>
    public static MetadataReferenceResolver ForRuntimeAssemblies(
        IEnumerable<Assembly> assemblies)
    {
        var refs = assemblies
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .OrderBy(a => a.Location, StringComparer.Ordinal)
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .ToList<MetadataReference>();
        return new MetadataReferenceResolver(refs);
    }
}
