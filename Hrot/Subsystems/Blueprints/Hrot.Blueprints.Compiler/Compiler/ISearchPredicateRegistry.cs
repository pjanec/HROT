namespace Hrot.Blueprints.Core.Compiler;

/// <summary>
/// Registry of known DTO types used by blueprint predicate compilation.
/// Passed to <c>InitializePredicates</c> so the coordinator can inject it.
/// Implementation is provided by the editor host (M3-T2).
/// </summary>
public interface ISearchPredicateRegistry
{
}
