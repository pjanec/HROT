namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// Registry of all compiled Blueprint definitions.
/// Minimal slice for Phase 1 test harness; full implementation in TASK-RT-001.
/// </summary>
public sealed class BlueprintRegistry
{
    private volatile Snapshot _current = new Snapshot();

    public event Action? OnRegistryChanged;

    public BlueprintRegistryStaging BeginStaging() => new BlueprintRegistryStaging();

    public void CommitStaging(BlueprintRegistryStaging staging)
    {
        var next = new Snapshot(staging);
        Interlocked.Exchange(ref _current, next);
        OnRegistryChanged?.Invoke();
    }

    public bool TryGetById(Guid id, out BlueprintDefinition? def)
        => _current.ById.TryGetValue(id, out def);

    public bool TryGetByName(string name, out BlueprintDefinition? def)
        => _current.ByName.TryGetValue(name, out def);

    public IReadOnlyCollection<BlueprintDefinition> GetAll()
        => _current.ById.Values;

    public void RegisterWorldSingleton(Guid blueprintId, BlackboardTier tier)
    {
        // Validated by CommitStaging in full impl; stub is permissive.
    }

    public bool TryGetWorldSingleton(Guid blueprintId, out BlackboardTier tier)
    {
        tier = BlackboardTier.B1024;
        return false;
    }

    public IReadOnlyList<(Guid, BlackboardTier)> GetAllWorldSingletons()
        => Array.Empty<(Guid, BlackboardTier)>();

    private sealed class Snapshot
    {
        public readonly Dictionary<Guid, BlueprintDefinition> ById = new();
        public readonly Dictionary<string, BlueprintDefinition> ByName = new();

        public Snapshot() { }

        public Snapshot(BlueprintRegistryStaging staging)
        {
            foreach (var def in staging.Definitions)
            {
                ById[def.AssetId] = def;
                ByName[def.Name] = def;
            }
        }
    }
}

/// <summary>Staging area for atomic registry updates.</summary>
public sealed class BlueprintRegistryStaging
{
    internal readonly List<BlueprintDefinition> Definitions = new();

    public void Add(BlueprintDefinition def)
    {
        if (Definitions.Any(d => d.AssetId == def.AssetId))
            throw new InvalidOperationException(
                $"Duplicate BlueprintId {def.AssetId} ('{def.Name}')");
        Definitions.Add(def);
    }
}
