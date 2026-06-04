namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// Runtime registry of compiled Blueprint definitions.
/// Atomic staging+commit protocol ensures tick systems always see a consistent snapshot.
/// Per Runtime DD §2.
/// </summary>
public sealed class BlueprintRegistry
{
    // Single field read is atomic for reference types; Interlocked.Exchange ensures
    // ordering when CommitStaging publishes a new snapshot.
    private Snapshot _current = new Snapshot();

    /// <summary>Fires after every CommitStaging, even when staging is empty.</summary>
    public event Action? OnRegistryChanged;

    // ---- Direct registration (cold boot / convenience) ----------------------

    /// <summary>Registers a Library-dispatch Blueprint directly into the current snapshot.</summary>
    public void RegisterLibrary(int blueprintId, string name)
    {
        var def = new BlueprintDefinition
        {
            Name          = name,
            Kind          = BlueprintDispatchKind.Library,
            StructureHash = 0,
            StateSize     = 0,
        };
        RegisterDirect(blueprintId, def);
    }

    /// <summary>Registers an AiPrimitive-dispatch Blueprint directly.</summary>
    public void RegisterAiPrimitive(int blueprintId, BlueprintDefinition def)
    {
        if (def.Kind != BlueprintDispatchKind.AiPrimitive)
            throw new ArgumentException(
                $"RegisterAiPrimitive called with definition of kind {def.Kind}");
        RegisterDirect(blueprintId, def);
    }

    /// <summary>Registers an Instance-dispatch Blueprint directly.</summary>
    public void RegisterInstance(int blueprintId, BlueprintDefinition def)
    {
        if (def.Kind != BlueprintDispatchKind.Instance)
            throw new ArgumentException(
                $"RegisterInstance called with definition of kind {def.Kind}");
        RegisterDirect(blueprintId, def);
    }

    // ---- Lookup (lock-free reads) -------------------------------------------

    /// <summary>Looks up a Blueprint definition by its 32-bit ID.</summary>
    public bool TryGetById(int blueprintId, out BlueprintDefinition? def)
    {
        var snapshot = _current;  // single atomic read
        return snapshot.ById.TryGetValue(blueprintId, out def);
    }

    /// <summary>Looks up a Blueprint definition by name.</summary>
    public bool TryGetByName(string name, out BlueprintDefinition? def)
    {
        var snapshot = _current;
        if (!snapshot.ByName.TryGetValue(name, out var id))
        {
            def = default;
            return false;
        }
        return snapshot.ById.TryGetValue(id, out def);
    }

    /// <summary>Returns all registered definitions as (Id, Def) tuples. Safe to enumerate mid-reload.</summary>
    public IReadOnlyList<(int Id, BlueprintDefinition Def)> GetAll()
    {
        var snapshot = _current;
        return snapshot.ById.Select(kv => (kv.Key, kv.Value)).ToArray();
    }

    // ---- World singletons ---------------------------------------------------

    /// <summary>
    /// Marks an already-registered Blueprint as a world singleton on the given tier.
    /// Throws if blueprintId is not yet in ById.
    /// </summary>
    public void RegisterWorldSingleton(int blueprintId, BlackboardTier tier)
    {
        if (!_current.ById.ContainsKey(blueprintId))
            throw new InvalidOperationException(
                $"RegisterWorldSingleton(0x{blueprintId:X8}): no Blueprint registered with that id.");
        _current.WorldSingletons[blueprintId] = tier;
        // Rebuild the pre-materialized list so GetAllWorldSingletons stays consistent.
        _current.WorldSingletonList = BuildWorldSingletonList(_current.WorldSingletons);
    }

    /// <summary>Returns true if blueprintId is marked as a world singleton.</summary>
    public bool TryGetWorldSingleton(int blueprintId, out BlackboardTier tier)
    {
        var snapshot = _current;
        return snapshot.WorldSingletons.TryGetValue(blueprintId, out tier);
    }

    /// <summary>
    /// Returns the pre-materialized list of world singletons.
    /// Zero-allocation hot path; same reference returned on every call between commits.
    /// Per Runtime DD Inline Patches Hot-path Correction 1.
    /// </summary>
    public IReadOnlyList<(int, BlackboardTier)> GetAllWorldSingletons()
        => _current.WorldSingletonList;

    // ---- Hot reload protocol ------------------------------------------------

    /// <summary>Returns a new empty staging buffer for an upcoming reload.</summary>
    public BlueprintRegistryStaging BeginStaging() => new BlueprintRegistryStaging();

    /// <summary>
    /// Atomically publishes the staging buffer as the new current snapshot and fires
    /// OnRegistryChanged. Each commit fully replaces the previous registry.
    /// </summary>
    public void CommitStaging(BlueprintRegistryStaging staging)
    {
        var byIdDict = staging.Definitions.ToDictionary(kv => kv.Key, kv => kv.Value);
        var byNameDict = staging.Definitions.ToDictionary(
            kv => kv.Value.Name, kv => kv.Key, StringComparer.Ordinal);
        var worldSingletonsDict = staging.WorldSingletons.ToDictionary(
            kv => kv.Key, kv => kv.Value);

        var next = new Snapshot
        {
            ById              = byIdDict,
            ByName            = byNameDict,
            WorldSingletons   = worldSingletonsDict,
            WorldSingletonList = BuildWorldSingletonList(worldSingletonsDict),
        };

        // Atomic publish -- readers see either old or new snapshot, never partial state.
        Interlocked.Exchange(ref _current, next);

        OnRegistryChanged?.Invoke();
    }

    /// <summary>
    /// Atomically merges <paramref name="staging"/> into the current snapshot by UPSERTING each
    /// staged definition by id, preserving every definition not present in staging. Used by the
    /// Quick-Reload path, where staging contains only the recompiled blueprint(s); siblings and
    /// code-defined definitions must survive. Contrast with <see cref="CommitStaging"/>, which
    /// fully replaces the snapshot (file-watcher full-rebuild path). Fires OnRegistryChanged.
    /// <para>
    /// Note: merge upserts world-singleton markings; it does not remove a singleton marking for
    /// a blueprint that stops being a singleton. Acceptable for the quick-reload path.
    /// </para>
    /// </summary>
    public void CommitStagingMerge(BlueprintRegistryStaging staging)
    {
        var prev = _current; // single atomic read

        var byId            = new Dictionary<int, BlueprintDefinition>(prev.ById);
        var byName          = new Dictionary<string, int>(prev.ByName, StringComparer.Ordinal);
        var worldSingletons = new Dictionary<int, BlackboardTier>(prev.WorldSingletons);

        foreach (var kv in staging.Definitions)
        {
            // Upsert by id. If the id already maps to a different name, drop the stale name entry
            // so ByName never points at a replaced definition.
            if (byId.TryGetValue(kv.Key, out var old) &&
                !string.Equals(old.Name, kv.Value.Name, StringComparison.Ordinal))
            {
                byName.Remove(old.Name);
            }
            byId[kv.Key]          = kv.Value;
            byName[kv.Value.Name] = kv.Key;
        }

        foreach (var kv in staging.WorldSingletons)
            worldSingletons[kv.Key] = kv.Value;

        var next = new Snapshot
        {
            ById               = byId,
            ByName             = byName,
            WorldSingletons    = worldSingletons,
            WorldSingletonList = BuildWorldSingletonList(worldSingletons),
        };

        Interlocked.Exchange(ref _current, next);
        OnRegistryChanged?.Invoke();
    }

    // ---- Private helpers ----------------------------------------------------

    private void RegisterDirect(int blueprintId, BlueprintDefinition def)
    {
        if (_current.ById.TryGetValue(blueprintId, out var existing))
            throw new InvalidOperationException(
                $"BlueprintId 0x{blueprintId:X8} collision: '{def.Name}' " +
                $"would replace '{existing.Name}'. Regenerate one asset's Guid.");
        _current.ById[blueprintId]  = def;
        _current.ByName[def.Name]   = blueprintId;
    }

    private static IReadOnlyList<(int, BlackboardTier)> BuildWorldSingletonList(
        Dictionary<int, BlackboardTier> worldSingletons)
    {
        var list = new List<(int, BlackboardTier)>(worldSingletons.Count);
        foreach (var kv in worldSingletons)
            list.Add((kv.Key, kv.Value));
        return list.AsReadOnly();
    }

    // Mutable per-snapshot state (replaced atomically by CommitStaging)
    private sealed class Snapshot
    {
        public Dictionary<int, BlueprintDefinition>    ById              = new();
        public Dictionary<string, int>                 ByName            = new(StringComparer.Ordinal);
        public Dictionary<int, BlackboardTier>         WorldSingletons   = new();
        public IReadOnlyList<(int, BlackboardTier)>    WorldSingletonList = Array.Empty<(int, BlackboardTier)>();
    }
}

/// <summary>
/// Staging buffer populated by [BlueprintRegistrar].Register during hot reload,
/// then atomically committed via BlueprintRegistry.CommitStaging.
/// </summary>
public sealed class BlueprintRegistryStaging
{
    internal readonly Dictionary<int, BlueprintDefinition> Definitions   = new();
    internal readonly Dictionary<int, BlackboardTier>      WorldSingletons = new();

    /// <summary>The blueprint ids staged in this buffer (the recompiled set for a Quick-Reload).</summary>
    public IReadOnlyCollection<int> StagedBlueprintIds => Definitions.Keys;

    /// <summary>
    /// Adds a Blueprint definition to the staging buffer.
    /// Throws InvalidOperationException if blueprintId is already present.
    /// </summary>
    public void Add(int blueprintId, BlueprintDefinition def)
    {
        if (Definitions.TryGetValue(blueprintId, out var existing))
            throw new InvalidOperationException(
                $"BlueprintId 0x{blueprintId:X8} collision during staging: " +
                $"'{def.Name}' would replace '{existing.Name}'.");
        Definitions[blueprintId] = def;
    }

    /// <summary>Marks a Blueprint as a world singleton in this staging buffer.</summary>
    public void AddWorldSingleton(int blueprintId, BlackboardTier tier)
        => WorldSingletons[blueprintId] = tier;
}

