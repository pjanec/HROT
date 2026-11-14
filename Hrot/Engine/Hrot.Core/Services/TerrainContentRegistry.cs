namespace Hrot.Map.Common.Services;

/// <summary>
/// ⭐ CE-4013 (<c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §4.1ae T1) — the extra terrain content ONE node loads with every
/// terrain (the first: the SUMO net, AQ93 R-4021). A capability adds a loader here; the node's <see cref="TerrainResidency"/> attaches
/// each one at its next <see cref="TerrainResidency.Prepare"/>, so the order between "the host built its residency" and "the capability
/// composed" does not matter.
/// </summary>
/// <remarks>Lives on <c>HrotNodeContext.TerrainContent</c>, one per node. Keyed: adding the same key twice keeps the first.</remarks>
public sealed class TerrainContentRegistry
{
    private readonly object _gate = new();
    private readonly List<(string Key, Func<ITerrainContentLoader> Factory)> _entries = new();

    /// <summary>Loads <paramref name="factory"/>'s content with every terrain from the next one on. Idempotent per <paramref name="key"/>.</summary>
    public void Add(string key, Func<ITerrainContentLoader> factory)
    {
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("A content loader needs a key.", nameof(key));
        if (factory is null) throw new ArgumentNullException(nameof(factory));
        lock (_gate)
        {
            foreach (var (k, _) in _entries) if (k == key) return;
            _entries.Add((key, factory));
        }
    }

    /// <summary>The registered loaders, in registration order (a copy: <see cref="TerrainResidency.Prepare"/> runs off the main thread).</summary>
    public IReadOnlyList<(string Key, Func<ITerrainContentLoader> Factory)> Entries()
    {
        lock (_gate) return _entries.ToArray();
    }
}
