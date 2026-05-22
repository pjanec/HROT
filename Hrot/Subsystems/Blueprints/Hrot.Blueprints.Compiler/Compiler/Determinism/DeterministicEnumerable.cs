namespace Hrot.Blueprints.Core.Compiler.Determinism;

/// <summary>
/// Deterministic enumerable helpers that sort by stable identifiers.
/// Full implementation in TASK-CP-001 (sorting helpers used by compiler stages).
/// </summary>
internal static class DeterministicEnumerable
{
    public static IEnumerable<T> OrderById<T>(IEnumerable<T> source, Func<T, Guid> idSelector)
        => source.OrderBy(idSelector);

    public static IEnumerable<T> OrderByName<T>(IEnumerable<T> source, Func<T, string> nameSelector)
        => source.OrderBy(nameSelector, StringComparer.Ordinal);
}
