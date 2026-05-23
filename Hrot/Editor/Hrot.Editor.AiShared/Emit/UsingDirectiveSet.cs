namespace Hrot.Editor.AiShared.Emit;

/// <summary>
/// Maintains the set of namespaces for a generated file and produces
/// sorted using-directive lists.
/// </summary>
public sealed class UsingDirectiveSet
{
    private readonly HashSet<string> _namespaces = new();

    public void Add(string ns) => _namespaces.Add(ns);

    public void AddRange(IEnumerable<string> namespaces)
    {
        foreach (var ns in namespaces)
            _namespaces.Add(ns);
    }

    /// <summary>Returns the sorted using-directive list with blank-line separator.</summary>
    public IReadOnlyList<string> ToSortedList() =>
        FluentCSharpEmitterBase.SortUsings(_namespaces);
}
