namespace Hrot.Blueprints.Core.Compiler.Roslyn;

/// <summary>
/// Resolves FDP assembly metadata references for Roslyn compilation.
/// Full implementation in TASK-CP-005.
/// </summary>
internal static class MetadataReferenceResolver
{
    public static IReadOnlyList<string> GetRequiredAssemblyPaths()
        => throw new NotImplementedException("MetadataReferenceResolver not yet implemented (TASK-CP-005).");
}
