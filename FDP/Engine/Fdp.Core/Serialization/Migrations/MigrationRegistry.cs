namespace Fdp.Core.Serialization.Migrations;

/// <summary>
/// Thread-safe registry of JSON document type registrations and their
/// version migration chains. Supports both full migrator chains and
/// passthrough registrations (doc types that need no migration).
/// </summary>
public sealed class MigrationRegistry
{
    // Internal entry for each registered doc type.
    private sealed class DocTypeEntry
    {
        public int CurrentVersion { get; }
        public bool IsPassthrough { get; }

        // Maps (fromVersion, toVersion) -> migrator. Key contains canonical direction.
        private readonly Dictionary<(int, int), IJsonDocumentMigrator> _migrators;

        public DocTypeEntry(int currentVersion, bool isPassthrough,
            Dictionary<(int, int), IJsonDocumentMigrator> migrators)
        {
            CurrentVersion = currentVersion;
            IsPassthrough = isPassthrough;
            _migrators = migrators;
        }

        public IJsonDocumentMigrator? Find(int from, int to)
            => _migrators.TryGetValue((from, to), out var m) ? m : null;

        public IReadOnlyDictionary<(int, int), IJsonDocumentMigrator> Migrators => _migrators;
    }

    private readonly Dictionary<string, DocTypeEntry> _entries = new(StringComparer.Ordinal);
    private bool _sealed;

    // ---------------------------------------------------------------
    // Registration
    // ---------------------------------------------------------------

    /// <summary>
    /// Registers a document type with a full set of version migrators.
    /// </summary>
    /// <param name="docType">Non-null, non-empty document type identifier.</param>
    /// <param name="currentVersion">The highest version understood by the current engine (>= 1).</param>
    /// <param name="migrators">
    /// The complete set of up and down migrators that cover every version step
    /// from 1 to <paramref name="currentVersion"/> - 1.
    /// </param>
    /// <exception cref="MigrationException">
    /// Thrown if: the registry is sealed; <paramref name="docType"/> is already
    /// registered; migrators reference the wrong doc type; any migrator step is
    /// not exactly one version apart; duplicate or missing steps are detected.
    /// </exception>
    public void RegisterDocType(
        string docType,
        int currentVersion,
        IEnumerable<IJsonDocumentMigrator> migrators)
    {
        ValidateNotSealed();
        ValidateDocType(docType);

        if (currentVersion < 1)
            throw new MigrationException(
                $"currentVersion must be >= 1 for '{docType}'; got {currentVersion}.");

        if (_entries.ContainsKey(docType))
            throw new MigrationException(
                $"Doc type '{docType}' is already registered.");

        var migratorList = migrators?.ToList()
            ?? throw new ArgumentNullException(nameof(migrators));

        var map = new Dictionary<(int, int), IJsonDocumentMigrator>();

        foreach (var m in migratorList)
        {
            if (m is null)
                throw new MigrationException(
                    $"Migrator list for '{docType}' contains a null entry.");

            if (!string.Equals(m.DocType, docType, StringComparison.Ordinal))
                throw new MigrationException(
                    $"Migrator DocType '{m.DocType}' does not match registered doc type '{docType}'.");

            int diff = m.ToVersion - m.FromVersion;
            if (diff != 1 && diff != -1)
                throw new MigrationException(
                    $"Migrator for '{docType}' steps from {m.FromVersion} to {m.ToVersion}; " +
                    $"each migrator must advance exactly one version.");

            var key = (m.FromVersion, m.ToVersion);
            if (map.ContainsKey(key))
                throw new MigrationException(
                    $"Duplicate migrator for '{docType}' step {m.FromVersion}->{m.ToVersion}.");

            map[key] = m;
        }

        // Validate coverage: every step 1..(currentVersion-1) needs both an Up and a Down migrator.
        if (currentVersion > 1)
        {
            for (int v = 1; v < currentVersion; v++)
            {
                if (!map.ContainsKey((v, v + 1)))
                    throw new MigrationException(
                        $"Missing Up migrator for '{docType}' step {v}->{v + 1}.");
                if (!map.ContainsKey((v + 1, v)))
                    throw new MigrationException(
                        $"Missing Down migrator for '{docType}' step {v + 1}->{v}.");
            }
        }

        _entries[docType] = new DocTypeEntry(currentVersion, isPassthrough: false, map);
    }

    /// <summary>
    /// Registers a document type that needs no version migration (schema is stable).
    /// </summary>
    public void RegisterPassthroughDocType(string docType, int currentVersion)
    {
        ValidateNotSealed();
        ValidateDocType(docType);

        if (currentVersion < 1)
            throw new MigrationException(
                $"currentVersion must be >= 1 for '{docType}'; got {currentVersion}.");

        if (_entries.ContainsKey(docType))
            throw new MigrationException(
                $"Doc type '{docType}' is already registered.");

        _entries[docType] = new DocTypeEntry(
            currentVersion,
            isPassthrough: true,
            new Dictionary<(int, int), IJsonDocumentMigrator>());
    }

    // ---------------------------------------------------------------
    // Query API
    // ---------------------------------------------------------------

    /// <summary>
    /// Returns <c>true</c> if <paramref name="docType"/> has been registered.
    /// </summary>
    public bool IsRegistered(string docType)
        => _entries.ContainsKey(docType);

    /// <summary>
    /// Returns <c>true</c> if <paramref name="docType"/> was registered as a
    /// passthrough type.
    /// </summary>
    /// <exception cref="MigrationException">
    /// The doc type is not registered.
    /// </exception>
    public bool IsPassthrough(string docType)
    {
        var entry = GetEntry(docType);
        return entry.IsPassthrough;
    }

    /// <summary>
    /// Returns the current (highest understood) schema version for
    /// <paramref name="docType"/>.
    /// </summary>
    /// <exception cref="MigrationException">The doc type is not registered.</exception>
    public int GetCurrentVersion(string docType) => GetEntry(docType).CurrentVersion;

    /// <summary>
    /// Returns the ordered chain of migrators that transforms a document of
    /// <paramref name="docType"/> from <paramref name="fromVersion"/> to
    /// <paramref name="toVersion"/>.
    /// </summary>
    /// <exception cref="MigrationException">
    /// The doc type is not registered, is a passthrough, or no path exists.
    /// </exception>
    public IReadOnlyList<IJsonDocumentMigrator> GetPath(
        string docType, int fromVersion, int toVersion)
    {
        var entry = GetEntry(docType);

        if (entry.IsPassthrough)
            throw new MigrationException(
                $"Doc type '{docType}' is a passthrough type; it has no migration path.");

        if (fromVersion == toVersion)
            return Array.Empty<IJsonDocumentMigrator>();

        bool goingUp = toVersion > fromVersion;
        int step = goingUp ? 1 : -1;
        var chain = new List<IJsonDocumentMigrator>();

        for (int v = fromVersion; v != toVersion; v += step)
        {
            int nextV = v + step;
            var migrator = entry.Find(v, nextV);
            if (migrator is null)
                throw new MigrationException(
                    $"No migrator registered for '{docType}' step {v}->{nextV}. " +
                    $"Cannot build migration path from {fromVersion} to {toVersion}.");
            chain.Add(migrator);
        }

        return chain;
    }

    /// <summary>
    /// Returns <c>true</c> if the registry can migrate <paramref name="docType"/>
    /// from <paramref name="fromVersion"/> to <paramref name="toVersion"/> without
    /// any gaps. Never throws.
    /// </summary>
    public bool CanMigrate(string docType, int fromVersion, int toVersion)
    {
        try
        {
            if (!_entries.TryGetValue(docType, out var entry) || entry.IsPassthrough)
                return false;

            if (fromVersion == toVersion)
                return true;

            bool goingUp = toVersion > fromVersion;
            int step = goingUp ? 1 : -1;

            for (int v = fromVersion; v != toVersion; v += step)
            {
                if (entry.Find(v, v + step) is null)
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Enumerates all registered doc type identifiers.</summary>
    public IEnumerable<string> RegisteredDocTypes => _entries.Keys;

    // ---------------------------------------------------------------
    // Sealing
    // ---------------------------------------------------------------

    /// <summary>
    /// Seals the registry. After sealing, all Register calls throw
    /// <see cref="MigrationException"/>.
    /// </summary>
    internal void Seal() => _sealed = true;

    // ---------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------

    private void ValidateNotSealed()
    {
        if (_sealed)
            throw new MigrationException(
                "The MigrationRegistry has been sealed and no further registrations are allowed.");
    }

    private static void ValidateDocType(string docType)
    {
        if (docType is null)
            throw new ArgumentNullException(nameof(docType));
        if (docType.Length == 0)
            throw new MigrationException("docType must not be empty.");
    }

    private DocTypeEntry GetEntry(string docType)
    {
        if (!_entries.TryGetValue(docType, out var entry))
            throw new MigrationException($"Doc type '{docType}' is not registered.");
        return entry;
    }
}
