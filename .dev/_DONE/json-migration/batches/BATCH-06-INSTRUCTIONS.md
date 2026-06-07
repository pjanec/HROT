# BATCH-06 Instructions — JM-P1-012: PersistentMigrationAdapter + Round-Trip Diff (GATE)

**Batch number:** BATCH-06  
**Task:** JM-P1-012  
**Design refs:** *Migration-system.md §7.2, §7.3*, *06 §4.2, §4.3*, *TASK-DETAILS.md §JM-P1-012*  
**Prerequisites:** BATCH-05 committed (198 migration tests passing). All prior infrastructure present.

---

## Objective

Implement `MigrationLoadResult` and `PersistentMigrationAdapter` — the editor-facing adapter that writes pre-migration snapshots, computes unknowns journals for down-migrations (Round-Trip Diff algorithm), and saves back with journal restoration.

This is the most complex batch in Phase 1. Read the design sections carefully before starting.

---

## What Exists (Do NOT reimplement)

All of these are already in the codebase and working:

| Type | File | Role |
|---|---|---|
| `MigrationPipeline` | `Migrations/MigrationPipeline.cs` | MigrateToCurrent/MigrateTo/GetCurrentVersion |
| `MigrationRegistry` | `Migrations/MigrationRegistry.cs` | GetPath, CanMigrate |
| `JsonEnvelope` | `Migrations/JsonEnvelope.cs` | Peek/Read/Write |
| `UnknownsJournal` | `Migrations/UnknownsJournal.cs` | Compute/ApplyTo/Serialize/Deserialize |
| `DomDiffer` | `Migrations/Internal/Diff/DomDiffer.cs` | Diff(pre, post) |
| `HashUtilities` | `Migrations/Internal/HashUtilities.cs` | ComputeContentHash |
| `IMigrationStorage` | `Migrations/IMigrationStorage.cs` | 9-method storage interface |
| `InMemoryMigrationStorage` | `Migrations/InMemoryMigrationStorage.cs` | Dictionary-backed test impl |
| `SidecarFileHelper` | `Migrations/SidecarFileHelper.cs` | Filename helpers |
| `SidecarFileInfo` | `Migrations/SidecarFileInfo.cs` | record: FileName, Kind, Version, ContentHash |
| `ReadOnlyMigrationAdapter` | `Migrations/Adapters/ReadOnlyMigrationAdapter.cs` | Fast/slow read-only paths |
| `MigratorFactory` | `Tests/.../StubMigrator.cs` | MakePair/MakeAllPairs test helpers |
| `TestDocV1ToV2/V2ToV1/V2ToV3/V3ToV2` | `Tests/.../TestMigrators.cs` | Test migrators |

---

## Step 1: Add `CanMigrateTo` to `MigrationPipeline`

The `PersistentMigrationAdapter` needs to check if a down-migration chain exists before attempting it (to choose the snapshot-fallback path). Add this `internal` delegation:

**File:** `FDP/Engine/Fdp.Core/Serialization/Migrations/MigrationPipeline.cs`

Add after `GetCurrentVersion`:

```csharp
/// <summary>
/// Returns true if the registry can migrate <paramref name="docType"/>
/// from <paramref name="fromVersion"/> to <paramref name="toVersion"/>
/// without any gaps. Never throws.
/// </summary>
internal bool CanMigrateTo(string docType, int fromVersion, int toVersion)
    => _registry.CanMigrate(docType, fromVersion, toVersion);
```

---

## Step 2: `MigrationLoadResult`

**File:** `FDP/Engine/Fdp.Core/Serialization/Migrations/Adapters/MigrationLoadResult.cs`

```csharp
using System.Text.Json.Nodes;

namespace Fdp.Core.Serialization.Migrations.Adapters;

/// <summary>
/// The result of <see cref="PersistentMigrationAdapter.LoadAndMigrateAsync"/>.
/// Carries the migrated DOM and the metadata needed to save it back correctly.
/// See design §7.3.
/// </summary>
public sealed class MigrationLoadResult
{
    /// <summary>
    /// The DOM as the caller should see it, migrated to the current registered
    /// version (or to the snapshot's version if degraded fallback was used).
    /// Always non-null; always a complete <see cref="JsonObject"/>.
    /// </summary>
    public JsonObject Dom { get; init; } = null!;

    /// <summary>The <c>$meta</c> envelope as it existed on disk before any migration.</summary>
    public DocumentMeta OriginalMeta { get; init; } = null!;

    /// <summary>The <c>$meta</c> envelope as the DOM is now shaped, after migration.</summary>
    public DocumentMeta CurrentMeta { get; init; } = null!;

    /// <summary>True if up- or down-migration was performed.</summary>
    public bool WasMigrated => OriginalMeta.SchemaVersion != CurrentMeta.SchemaVersion;

    /// <summary>
    /// True if down-migration was performed AND the resulting journal had at
    /// least one operation. False when no down-migration occurred OR the
    /// down-migration was loss-free (empty journal not written).
    /// When false, <see cref="PersistentMigrationAdapter.SaveAsync"/> skips
    /// journal application entirely.
    /// </summary>
    public bool HasUnknownsJournal { get; init; }

    /// <summary>
    /// True if the load fell back to a snapshot because down-migration was
    /// unavailable. Callers should surface a warning UI.
    /// </summary>
    public bool IsDegraded { get; init; }

    /// <summary>Path of the snapshot used during degraded fallback, if any.</summary>
    public string? UsedSnapshotPath { get; init; }

    /// <summary>The migration report, or null if no migration was performed.</summary>
    public MigrationReport? Report { get; init; }

    /// <summary>
    /// The journal, used by <see cref="PersistentMigrationAdapter.SaveAsync"/>.
    /// Non-null if and only if <see cref="HasUnknownsJournal"/> is true.
    /// </summary>
    internal UnknownsJournal? Journal { get; init; }

    /// <summary>
    /// The content hash of the source file (SHA-256, hex16), used to locate
    /// and verify the journal on save-back.
    /// </summary>
    internal string SourceContentHash { get; init; } = null!;
}
```

---

## Step 3: `PersistentMigrationAdapter`

**File:** `FDP/Engine/Fdp.Core/Serialization/Migrations/Adapters/PersistentMigrationAdapter.cs`

### Constructor + fields

```csharp
namespace Fdp.Core.Serialization.Migrations.Adapters;

public sealed class PersistentMigrationAdapter
{
    private readonly MigrationPipeline _pipeline;
    private readonly IMigrationStorage _storage;
    private readonly Func<string> _engineVersionProvider;
    private readonly string _writerIdentifier;

    public PersistentMigrationAdapter(
        MigrationPipeline pipeline,
        IMigrationStorage storage,
        Func<string> engineVersionProvider,
        string writerIdentifier)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _engineVersionProvider = engineVersionProvider ?? throw new ArgumentNullException(nameof(engineVersionProvider));
        _writerIdentifier = writerIdentifier ?? throw new ArgumentNullException(nameof(writerIdentifier));
    }
```

### `LoadAndMigrateAsync(string path, CancellationToken ct)`

Load algorithm (see design §7.2 remarks):

```
1. Peek envelope (streaming)
2. Read all file bytes
3. Compute content hash = HashUtilities.ComputeContentHash(text)
4. currentVersion = _pipeline.GetCurrentVersion(diskMeta.DocType)
5. diskVersion = diskMeta.SchemaVersion

Case A: diskVersion == currentVersion
   - Parse DOM (editor always needs a mutable DOM)
   - No sidecars written, no pruning
   - Return result with HasUnknownsJournal=false, IsDegraded=false

Case B: diskVersion < currentVersion
   - WriteSnapshotAsync(path, diskVersion, hash, rawText)
   - Parse DOM
   - MigrateToCurrent(dom, path) = report
   - UpdatedMeta = JsonEnvelope.Read(dom)
   - PruneStale(path, hash)
   - Return result with Report, HasUnknownsJournal=false

Case C: diskVersion > currentVersion AND _pipeline.CanMigrateTo(docType, diskVersion, currentVersion)
   - Parse DOM
   - preDown = DeepClone(dom)
   - MigrateTo(dom, currentVersion, path) = report
   - journal = UnknownsJournal.Compute(preDown, dom, docType, diskVersion, currentVersion,
                  hash, _engineVersionProvider(), _writerIdentifier)
   - if (journal.Operations.Count > 0):
       WriteJournalAsync(path, journal)
       hasJournal = true
     else:
       hasJournal = false
       journal = null
   - PruneStale(path, hash)
   - Return result with HasUnknownsJournal=hasJournal, Journal=journal

Case D: diskVersion > currentVersion AND !CanMigrateTo
   - snapshot = FindBestSnapshotAsync(path, maxVersion=currentVersion)
   - if snapshot == null: throw MigrationException
   - Parse snapshot.Content as DOM
   - up-migrate snapshot DOM from snapshot.Version to currentVersion (if needed)
   - IsDegraded=true, UsedSnapshotPath=snapshot.Path
```

### `SaveAsync(string path, JsonObject dom, MigrationLoadResult priorLoad, CancellationToken ct)`

Save algorithm (see design §7.2 remarks):

```
1. if (priorLoad.HasUnknownsJournal):
   a. domToSave = DeepClone(dom)
   b. MigrateTo(domToSave, priorLoad.Journal.SourceFileVersion, path)
      -- up-migrate from the down-migrated version back to original disk version
   c. priorLoad.Journal.ApplyTo(domToSave)
      -- restore v_higher-exclusive content
   d. targetVersion = priorLoad.Journal.SourceFileVersion
   else:
   a. domToSave = dom (no clone needed — no up-migration)
   b. targetVersion = priorLoad.CurrentMeta.SchemaVersion (current version)

2. Stamp $meta on domToSave:
   a. Update $meta.schemaVersion = targetVersion
   b. Update $meta.engineVersion = _engineVersionProvider()
   c. If $meta.createdBy absent: set to _writerIdentifier
   d. Preserve $meta.createdUtc unchanged

3. json = domToSave.ToJsonString()
4. WriteOriginalAsync(path, json) -- atomic write via storage
5. if (priorLoad.HasUnknownsJournal): DeleteJournalAsync(path, priorLoad.Journal!)
6. PruneStale(path, HashUtilities.ComputeContentHash(json))
```

### `DeepClone` and `PruneStale` helpers

```csharp
private static JsonObject DeepClone(JsonObject source)
    => JsonNode.Parse(source.ToJsonString())!.AsObject();

private async Task PruneStaleAsync(string path, string currentHash, CancellationToken ct)
{
    var sidecars = await _storage.ListSidecarsAsync(path, ct).ConfigureAwait(false);
    foreach (var s in sidecars)
    {
        if (!string.Equals(s.ContentHash, currentHash, StringComparison.Ordinal))
            await _storage.DeleteSidecarAsync(path, s.FileName, ct).ConfigureAwait(false);
    }
}
```

### `$meta` stamping helper

When stamping `$meta` during save:
- `$meta.schemaVersion` is updated directly on the `JsonObject`
- `$meta.engineVersion` is updated
- `$meta.createdBy`: set ONLY if the property is absent or null
- `$meta.createdUtc`: DO NOT touch (preserve exactly as-is)

Use `JsonEnvelope.Read(dom)` to read current meta before stamping. After stamping, use `JsonEnvelope.WithSchemaVersion` and `JsonEnvelope.WithEngineVersion` if available, OR manipulate `dom["$meta"]` directly.

Check which static methods `JsonEnvelope` actually exposes. If `WithSchemaVersion`/`WithEngineVersion` exist, use them. Otherwise, update the `$meta` JsonObject directly:
```csharp
var metaObj = dom["$meta"] as JsonObject
    ?? throw new MigrationException("...");
metaObj["schemaVersion"] = targetVersion;
metaObj["engineVersion"] = _engineVersionProvider();
if (metaObj["createdBy"] is null)
    metaObj["createdBy"] = _writerIdentifier;
// createdUtc: do not touch
```

---

## Step 4: Tests

**File:** `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/PersistentMigrationAdapterTests.cs`

### Test infrastructure

All tests use `InMemoryMigrationStorage` (no filesystem). Use the existing test migrators from `TestMigrators.cs`:
- `TestDocV1ToV2`: adds `"kind":"default"` to every item in `$.items[]`
- `TestDocV2ToV1`: removes `"kind"` from every item in `$.items[]`
- `TestDocV2ToV3`: adds `"metadata":{}` to every item
- `TestDocV3ToV2`: removes `"metadata"` from every item

**Key insight for lossless vs lossy tests:**
- **Lossless (T2-033)**: v2 doc with NO items array (or empty items) → `TestDocV2ToV1.Apply` is a no-op → DomDiffer produces empty diff → no journal
- **Lossy (T2-034)**: v2 doc WITH items that have "kind" values → `TestDocV2ToV1` removes them → diff non-empty → journal written

**Deep clone in tests:** Use `JsonNode.Parse(dom.ToJsonString())!.AsObject()`.

### Helper methods for the test class

```csharp
// Build a pipeline with Test.Doc at the given currentVersion.
// Uses the shared TestDocV1↔V2↔V3 migrators.
private static MigrationPipeline BuildPipeline(int currentVersion)
{
    var registry = new MigrationRegistry();
    var migrators = new List<IJsonDocumentMigrator>();
    if (currentVersion >= 2) { migrators.Add(new TestDocV1ToV2()); migrators.Add(new TestDocV2ToV1()); }
    if (currentVersion >= 3) { migrators.Add(new TestDocV2ToV3()); migrators.Add(new TestDocV3ToV2()); }
    registry.RegisterDocType("Test.Doc", currentVersion, migrators);
    return new MigrationPipeline(registry);
}

// Build an adapter backed by InMemoryMigrationStorage.
private static (PersistentMigrationAdapter adapter, InMemoryMigrationStorage storage)
    BuildAdapter(int currentVersion = 2)
{
    var storage = new InMemoryMigrationStorage();
    var adapter = new PersistentMigrationAdapter(
        BuildPipeline(currentVersion),
        storage,
        () => "test-engine-1.0",
        "Test.Editor");
    return (adapter, storage);
}

// Build a JSON doc for Test.Doc at the given schemaVersion.
// items: list of (name, kind) tuples. Pass null/empty for no items (lossless case).
private static string BuildTestDoc(int schemaVersion, IEnumerable<(string name, string? kind)>? items = null)
{
    var sb = new StringBuilder();
    sb.Append("{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":");
    sb.Append(schemaVersion);
    sb.Append("}");
    if (items is not null)
    {
        sb.Append(",\"items\":[");
        bool first = true;
        foreach (var (name, kind) in items)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"name\":\"");
            sb.Append(name);
            sb.Append('"');
            if (kind is not null) { sb.Append(",\"kind\":\""); sb.Append(kind); sb.Append('"'); }
            sb.Append('}');
        }
        sb.Append(']');
    }
    sb.Append('}');
    return sb.ToString();
}

// Seed the storage with a file at the given path.
private static async Task SeedFile(InMemoryMigrationStorage storage, string path, string content)
    => await storage.WriteOriginalAsync(path, content);
```

### T2-030 through T2-041 (Load tests)

```
T2-030: LoadAndMigrate_AtCurrentVersion_NoSidecarsCreated
  Setup: v2 doc, pipeline at currentVersion=2 (fast path)
  Seed storage with the file.
  Load → WasMigrated=false, HasUnknownsJournal=false, IsDegraded=false
  ListSidecarsAsync → empty

T2-031: LoadAndMigrate_OlderVersion_WritesSnapshot
  Setup: v1 doc (items:[{name:"a"}]), pipeline at v2
  Load → WasMigrated=true
  ListSidecarsAsync → exactly 1 sidecar with Kind=Snapshot

T2-032: LoadAndMigrate_OlderVersion_DomIsCurrentShape
  Same setup as T2-031
  After load, Dom["$meta"]["schemaVersion"].GetValue<int>() == 2
  Dom["items"][0]["kind"].GetValue<string>() == "default"  (added by TestDocV1ToV2)

T2-033: LoadAndMigrate_NewerVersion_RoundTripsLosslessly_NoJournal
  Setup: v2 doc with NO items (lossless — v2→v1 migrator is a no-op)
  Pipeline at currentVersion=1
  Load → HasUnknownsJournal=false, no journal sidecar written
  ListSidecarsAsync → sidecars contain no journal entries

T2-034: LoadAndMigrate_NewerVersion_RoundTripLossy_WritesJournal
  Setup: v2 doc with items:[{name:"tank",kind:"tank"}], pipeline at v1
  Load → HasUnknownsJournal=true
  ListSidecarsAsync → at least 1 sidecar with Kind=Journal

T2-035: LoadAndMigrate_NewerVersion_JournalContainsCorrectOperations
  Same setup as T2-034
  result.Journal.Operations.Count > 0
  Find the Set op for $.items[0].kind — Value should be "tank"

T2-036: LoadAndMigrate_NewerVersion_DomIsDownMigrated
  Same setup as T2-034
  result.Dom["$meta"]["schemaVersion"].GetValue<int>() == 1
  result.Dom["items"][0] as JsonObject — "kind" property should be absent

T2-037: LoadAndMigrate_NewerVersion_ResultHasHashAndJournal
  Same setup as T2-034
  result.SourceContentHash is non-null and non-empty
  result.Journal is non-null
  result.HasUnknownsJournal == true

T2-038: LoadAndMigrate_MuchNewerVersion_NoChain_FallsBackToSnapshot
  Setup:
    - Register Test.Doc with currentVersion=2 (v1↔v2 pairs only)
    - Build a v5 doc (no chain from v5 to v2)
    - Pre-seed a snapshot: storage.WriteSnapshotAsync(path, 2, hash, v2DocContent)
    - where hash = HashUtilities.ComputeContentHash(v5DocContent) - WRONG: hash must
      be the hash of the v5 doc (the content being loaded)
  Wait — the snapshot was taken at v2 at some earlier time. The snapshot hash is the
  hash of the ORIGINAL file at that point in time (which was v5 content).
  
  Actually, for T2-038, the scenario is:
    1. Previously, the file was at v5 (hash=H_v5). A snapshot was written at v2.
    2. But the snapshot hash in the filename matches the v5 file hash at the time the
       snapshot was created. Wait, that's not right either.
  
  Re-reading the design: The snapshot filename is `{base}.v{sourceVersion}.{hash16}.snapshot.json`
  where hash16 = hash of the ORIGINAL file (at the time of snapshot creation).
  
  For T2-038, the snapshot was written when the file was at v5. The v2 snapshot records
  the v5 content hash in its filename. But now we're loading a different v5 file (possibly
  with the same content). The snapshot lookup uses `FindBestSnapshotAsync(path, maxVersion=2)`.
  
  For the test, pre-seed the snapshot directly:
    - v5 content = BuildTestDoc(5) (no migrators above v2)  
    - v2Snapshot = BuildTestDoc(2) (manually crafted at schemaVersion=2)
    - hash = HashUtilities.ComputeContentHash(v5Content) 
    - await storage.WriteSnapshotAsync(path, 2, hash, v2Snapshot)
    - await storage.WriteOriginalAsync(path, v5Content)
  
  Load → IsDegraded=true, Dom reflects v2 content, WasMigrated=true (schemaVersion changed)

T2-039: LoadAndMigrate_MuchNewerVersion_NoSnapshot_Throws
  Same as T2-038 but don't pre-seed any snapshot
  Load → throws MigrationException

T2-040: LoadAndMigrate_PrunesStaleSidecars
  Setup:
    - v1 doc (content "C1", hash H1)
    - Pre-seed a stale snapshot with a fake hash "staledeadbeef00":
      await storage.WriteSnapshotAsync(path, 1, "staledeadbeef00", "stale content")
    - This creates a sidecar with ContentHash="staledeadbeef00" in its filename
    - The actual current file has hash H1 (different from "staledeadbeef00")
  Load (pipeline at v2 — will up-migrate and write real snapshot)
  After load: ListSidecarsAsync → verify stale sidecar was deleted

T2-041: LoadAndMigrate_DoesNotPruneCurrentMatchingSidecars
  Setup:
    - v1 doc (content with hash H1)
    - Pre-seed a snapshot with hash H1:
      await storage.WriteSnapshotAsync(path, 1, H1, v1Content)
    - H1 = HashUtilities.ComputeContentHash(v1Content)
  Load (pipeline at v2 — will try to up-migrate, writes another snapshot if needed)
  After load: ListSidecarsAsync → the H1 snapshot is still present
```

### T2-050 through T2-066 (Save tests)

All use InMemoryMigrationStorage. Load a file first to get a `MigrationLoadResult`, then call `SaveAsync` and verify results.

```
T2-050: Save_NoJournal_WritesCurrentVersionFile
  Load a v1 doc (pipeline v2) → up-migrate, no journal
  Save → ReadOriginalAsync → parse → schemaVersion == 2

T2-051: Save_NoJournal_PreservesUserEdits
  Load v1 doc, add a new field to the DOM
  Save → ReadOriginalAsync → parse → new field present

T2-052: Save_NoJournal_UpdatesEngineVersion
  Load, save with engineVersionProvider returning "test-v1.0"
  ReadOriginalAsync → parse → $meta.engineVersion == "test-v1.0"

T2-053: Save_NoJournal_PreservesCreatedUtc
  Load v1 doc that has createdUtc = "2024-01-01T00:00:00.0000000Z"
  Save → read back → createdUtc unchanged

T2-054: Save_NoJournal_SetsCreatedByIfAbsent
  Load a v1 doc with no $meta.createdBy
  Save (writerIdentifier = "Test.Editor")
  Read back → createdBy == "Test.Editor"

T2-055: Save_NoJournal_PreservesCreatedByIfPresent
  Load a v1 doc with $meta.createdBy = "Original.Author"
  Save → createdBy still "Original.Author"

T2-056: Save_WithJournal_UpMigratesUserDom
  Setup: v2 doc with items:[{name:"a",kind:"tank"}], pipeline at v1
  Load → HasUnknownsJournal=true, Dom is at v1 (no "kind")
  Save (dom = priorLoad.Dom, no user edits)
  Read back → schemaVersion == 2 (back to original)

T2-057: Save_WithJournal_AppliesJournalToUpMigratedDom
  Same as T2-056
  Read back → items[0].kind == "tank" (restored by journal)

T2-058: Save_WithJournal_PreservesUserAddedEntity
  Setup: v2 doc with items:[{name:"a",kind:"tank"}], pipeline at v1
  Load → Dom at v1, HasUnknownsJournal=true
  User edit: add new item: (dom["items"] as JsonArray)!.Add(JsonNode.Parse("{\"name\":\"new\"}"))
  Save → read back → items has 2 entries, items[1].name == "new"

T2-059: Save_WithJournal_PreservesUserEditsToMappedFields
  Setup: same
  User edit: dom["items"][0]["name"] = "edited"
  Save → read back → items[0].name == "edited"

T2-060: Save_WithJournal_RestoresVHigherExclusiveContent
  Setup: v2 doc with items:[{name:"a",kind:"rare"}], pipeline at v1
  Load → journal has Set op for $.items[0].kind = "rare"
  Save → read back → items[0].kind == "rare" (restored)

T2-061: Save_WithJournal_DeletedEntityStaysDeleted
  Setup: v2 doc with items:[{name:"a",kind:"tank"},{name:"b",kind:"scout"}], pipeline at v1
  Load → HasUnknownsJournal=true
  User edit: remove items[0] from the array
  Save → read back → only 1 item (items[1] = b at v2 shape)
  Note: the up-migrator on save will add "kind":"default" to the remaining item
  and the journal may set it back to "scout" — verify items[0].kind == "scout" after round-trip

T2-062: Save_WithJournal_DeletesJournalSidecar
  Load (HasUnknownsJournal=true), Save
  After Save: FindJournalAsync(path, originalHash) == null

T2-063: Save_WithJournal_KeepsSnapshotSidecar
  Load v1 doc (up-migration → snapshot written)
  Save
  After Save: ListSidecarsAsync → still has snapshot sidecar (save never deletes snapshots)

T2-064: Save_PrunesStaleSidecars
  Setup: pre-seed a stale snapshot with fake hash
  Load (up-migration → real snapshot written with real hash)
  Save
  After Save: stale snapshot is gone, real snapshot (if hash still matches) preserved

T2-065: Save_AtomicWriteSemantics
  Load, edit, save — verify the file content in storage is updated (atomic for InMemory is
  just a dict update; test verifies content changed and original not truncated mid-write).
  For InMemoryStorage, "atomic" is trivially satisfied. Test: after Save, ReadOriginalAsync
  returns non-empty, parseable JSON.

T2-066: Save_FailedJournalApply_DoesNotOverwriteOriginal
  This tests that if journal.ApplyTo throws (malformed path), the original is not overwritten.
  Since UnknownsJournal.ApplyTo can fail if JsonPathParser.Parse throws (e.g., malformed op path),
  this is best tested by simulating a bad journal.
  Strategy: create a valid load result manually with a crafted journal that has a path that
  causes issues (e.g., an unreachable but syntactically valid path). The save should either
  succeed or fail without overwriting the original.
  Simplified acceptable test: verify that if SaveAsync encounters a MigrationException during
  journal application, the original file (as returned by ReadOriginalAsync) is unchanged.
  Use try/catch + Assert.ThrowsAsync. If ApplyTo doesn't throw for valid but unreachable paths
  (TryWrite returns false silently), test that SaveAsync completes and the file is written.
  NOTE: If TryWrite/TryRemove never throw (they return false silently for unreachable paths),
  then the "failure" path is not testable via the real journal. In that case, test the
  "atomic write semantics" more directly: write initial content, load, save, verify new content
  replaces old. This is sufficient coverage given InMemoryStorage's atomic semantics.
```

### T2-080: Gate test (Round-Trip lossless)

```csharp
[Fact]
public async Task FullRoundTrip_VHigherToVLowerAndBack_PreservesAllEdits()
{
    // ARRANGE: v2 doc with v2-exclusive "kind" values
    const string path = @"C:\data\scenario.json";
    var v2Original = BuildTestDoc(2, new[] {
        ("alpha", "tank"),    // item 0: kind="tank" (v2-exclusive)
        ("beta",  "scout"),   // item 1: kind="scout" (v2-exclusive)
    });

    // v1 binary (pipeline at v1)
    var (v1Adapter, storage) = BuildAdapter(currentVersion: 1);
    await SeedFile(storage, path, v2Original);

    var v1LoadResult = await v1Adapter.LoadAndMigrateAsync(path);

    // Verify v1 view: no "kind" fields
    Assert.True(v1LoadResult.HasUnknownsJournal);
    Assert.Equal(1, v1LoadResult.CurrentMeta.SchemaVersion);
    var items = v1LoadResult.Dom["items"]!.AsArray();
    Assert.DoesNotContain(items, i => (i as JsonObject)?.ContainsKey("kind") == true);

    // User edits in v1:
    // 1. Rename alpha to "alpha-renamed"
    (items[0] as JsonObject)!["name"] = "alpha-renamed";
    // 2. Add a new item (no "kind" at v1)
    items.Add(JsonNode.Parse("{\"name\":\"gamma\"}"));

    // Save back with v1 adapter
    await v1Adapter.SaveAsync(path, v1LoadResult.Dom, v1LoadResult);

    // v2 binary reloads (pipeline at v2)
    var v2Registry = new MigrationRegistry();
    v2Registry.RegisterDocType("Test.Doc", 2, new IJsonDocumentMigrator[]
        { new TestDocV1ToV2(), new TestDocV2ToV1() });
    var v2Adapter = new PersistentMigrationAdapter(
        new MigrationPipeline(v2Registry), storage,
        () => "test-engine-2.0", "Test.V2Editor");

    var v2Result = await v2Adapter.LoadAndMigrateAsync(path);

    // ASSERT: file is at v2 (fast path)
    Assert.Equal(2, v2Result.CurrentMeta.SchemaVersion);
    Assert.False(v2Result.WasMigrated);

    var finalItems = v2Result.Dom["items"]!.AsArray();
    Assert.Equal(3, finalItems.Count);

    // User's rename preserved
    Assert.Equal("alpha-renamed", finalItems[0]!["name"]!.GetValue<string>());
    // v2-exclusive "kind" restored for original items
    Assert.Equal("tank",  finalItems[0]!["kind"]!.GetValue<string>());
    Assert.Equal("scout", finalItems[1]!["kind"]!.GetValue<string>());
    // New item preserved; up-migrator gave it "default" kind (no journal op for it)
    Assert.Equal("gamma", finalItems[2]!["name"]!.GetValue<string>());
    Assert.Equal("default", finalItems[2]!["kind"]!.GetValue<string>());
}
```

**Important C-7 note:** The design's T2-080 example uses FluentAssertions (`.Should().Be(...)`). Use `Assert.*` instead — see T2-080 implementation above.

---

## Implementation Notes

### Deep clone
Use `JsonNode.Parse(source.ToJsonString())!.AsObject()` for all deep clones.

### `$meta` stamping in SaveAsync
Read `dom["$meta"]` as a JsonObject and update fields directly. The pipeline invariants already ensure `$meta` object identity is preserved during migration.

### Active sidecar pruning (PruneStaleAsync)
After any sidecar write (snapshot, journal) during LoadAndMigrateAsync, AND after every successful SaveAsync:
```csharp
var sidecars = await _storage.ListSidecarsAsync(path, ct);
foreach (var s in sidecars)
{
    if (!string.Equals(s.ContentHash, currentContentHash, StringComparison.Ordinal))
        await _storage.DeleteSidecarAsync(path, s.FileName, ct);
}
```
The `currentContentHash` is the hash of the file content as of the current load (for load-time pruning) or the hash of the just-written content (for save-time pruning).

### Empty journal rule
```csharp
var journal = UnknownsJournal.Compute(preDown, dom, ...);
if (journal.Operations.Count > 0)
{
    await _storage.WriteJournalAsync(path, journal, ct);
    hasJournal = true;
}
// else: hasJournal = false, journal = null
```

### InMemoryMigrationStorage file access
The storage has no filesystem. `LoadAndMigrateAsync(string path, CancellationToken ct)` reads from `storage.ReadOriginalAsync(path)`. If the result is null, throw `MigrationException("File not found: {path}")`.

### How to read file content in PersistentMigrationAdapter
Unlike `ReadOnlyMigrationAdapter` which reads from the real filesystem, `PersistentMigrationAdapter` reads from `IMigrationStorage.ReadOriginalAsync`. This is the correct design — it abstracts over filesystem vs in-memory storage for tests.

The load method signature is:
```csharp
public async Task<MigrationLoadResult> LoadAndMigrateAsync(
    string path,
    CancellationToken ct = default)
```

The "path" is an abstract key in the storage, not necessarily a real filesystem path. Use `ReadOriginalAsync` to get the content.

Note: This is a deliberate difference from `ReadOnlyMigrationAdapter` which reads directly from the filesystem. `PersistentMigrationAdapter` always goes through `IMigrationStorage` for both reads and writes.

---

## Success Conditions

1. Build: 0 errors (warnings are acceptable if pre-existing)
2. All 30 tests (T2-030..T2-041, T2-050..T2-066, T2-080) pass
3. Total migration test count: 228 (198 existing + 30 new)
4. T2-080 must pass — this is the GATE test

---

## Deliverables

Files to create/modify:
1. `FDP/Engine/Fdp.Core/Serialization/Migrations/MigrationPipeline.cs` — add `internal bool CanMigrateTo(...)`
2. `FDP/Engine/Fdp.Core/Serialization/Migrations/Adapters/MigrationLoadResult.cs` (new)
3. `FDP/Engine/Fdp.Core/Serialization/Migrations/Adapters/PersistentMigrationAdapter.cs` (new)
4. `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/PersistentMigrationAdapterTests.cs` (new)

Do NOT create any other files. Do NOT modify test infrastructure files (`StubMigrator.cs`, `TestMigrators.cs`) — they are sufficient.

After implementation: run `dotnet test FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj --filter "FullyQualifiedName~Migrations"` and confirm 228/228 pass.

---

## Report Requirements

Write `BATCH-06-REPORT.md` to `.dev/json-migration/reports/`. Follow the standard report format. In particular:
- State actual test count (228 expected)
- Describe any design decisions made (especially around degraded-load seeding in T2-038)
- Identify any issues encountered and how they were resolved
- Confirm T2-080 passes
