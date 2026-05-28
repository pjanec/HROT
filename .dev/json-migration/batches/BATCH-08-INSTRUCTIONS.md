# BATCH-08 — Phase 1 Acceptance Gate

**Task:** JM-P1-014  
**Design refs:** Migration-system.md §10 (acceptance criteria), §07 §4.1 Step 14  
**Workspace root:** `d:\Work\IOS-IG-SimHost-FDP`

---

## 1. Context

All JM-P1-001..013 are implemented and committed. 232/232 migration tests pass.
The Phase 1 acceptance gate requires:

1. All T1/T2/T3 tests pass. **(232 currently passing — do not break them.)**
2. T2-080 passes. **(Confirmed passing.)**
3. Coverage of `Fdp.Core.Serialization.Migrations.*` >= 90% line / >= 85% branch.
4. No warnings (`dotnet build FDP/Engine/Fdp.Core/Fdp.Core.csproj -q` reports 0 warnings).
5. No `[Skip]`/`[Ignore]` without rationale. (T3-007 is pre-approved: cross-platform.)
6. Dry-run smoke test: uses `MigrationBootstrap.Build` + `FileSystemMigrationStorage`,
   registers `Test.Doc` v1<->v2 pair, loads v1 fixture, edits, saves, reloads — verifies
   lossless round-trip on real filesystem.

**Current coverage gaps that need closing:**

| Class | Line | Branch |
|---|---|---|
| `JsonPathApplicator` | 61.5% | 48% |
| `ReadOnlyLoadOutcome` | 71.4% | 50% |
| `MigrationException` | 73.7% | 100% |
| `FileSystemMigrationStorage` | 69.8% | 75% |
| `JsonEnvelope` | 78.8% | 64.1% |
| `ReadOnlyMigrationAdapter` | 88.6% | 75% |
| `MigrationPipeline` | 93.9% | 76.7% |
| `UnknownsJournal` | 92.5% | 75.6% |
| `SidecarFileHelper` | 94.3% | 75% |
| `InMemoryMigrationStorage` | 93.2% | 88.5% |
| `MigrationBootstrap` | 100% | 50% |

---

## 2. Files to Read Before Starting

- `FDP/Engine/Fdp.Core/Serialization/Migrations/Internal/JsonPathApplicator.cs` — understand all 3 public methods (Read, TryWrite, TryRemove) and their edge cases
- `FDP/Engine/Fdp.Core/Serialization/Migrations/JsonEnvelope.cs` — understand `ParseMetaObject`, `CheckMetaIsFirst`, `WithSchemaVersion`, `WithEngineVersion`
- `FDP/Engine/Fdp.Core/Serialization/Migrations/Adapters/ReadOnlyLoadOutcome.cs` — understand `AsJsonObject()` and `AsJsonString()` invalid-state throws
- `FDP/Engine/Fdp.Core/Serialization/Migrations/Adapters/ReadOnlyMigrationAdapter.cs` — understand IOException branch and null stream branch
- `FDP/Engine/Fdp.Core/Serialization/Migrations/FileSystemMigrationStorage.cs` — understand error paths in ReadOriginalAsync, FindBestSnapshotAsync, FindJournalAsync
- `FDP/Engine/Fdp.Core/Serialization/Migrations/MigrationPipeline.cs` — understand null guards
- `FDP/Engine/Fdp.Core/Serialization/Migrations/MigrationException.cs` — all 3 constructors
- `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/TestMigrators.cs` — available `TestDocV1ToV2`, `TestDocV2ToV1`
- `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/StubMigrator.cs` — `MigratorFactory`
- `FDP/Engine/Fdp.Core/Serialization/Migrations/Bootstrap/MigrationBootstrap.cs` — `Build` method signature

---

## 3. Deliverables

### 3.1 New test file: `Internal/JsonPathApplicatorTests.cs`

**Location:** `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/Internal/JsonPathApplicatorTests.cs`

**Purpose:** Direct tests on `JsonPathApplicator` static class (internal) covering all branches.

Use `using Fdp.Core.Serialization.Migrations.Internal;` and `using Fdp.Core.Serialization.Migrations;`.
Access `JsonPathApplicator` directly (test project has `InternalsVisibleTo`).
Build paths using `JsonPathParser.Parse(...)`.

Tests to implement (T1-192 through T1-215):

**Read method:**

- **T1-192** `Read_RootPath_ReturnsRoot`  
  Path `$` (empty segments). `Read(root, emptyPath)` returns the root object itself.

- **T1-193** `Read_QuotedKeySegment_HappyPath`  
  DOM: `{"a":{"00000000-0000-0000-0000-000000000001": {"x": 5}}}`.  
  Path `$.a['00000000-0000-0000-0000-000000000001'].x` returns 5.

- **T1-194** `Read_ArrayIndex_InBounds_ReturnsElement`  
  DOM: `{"items":[10,20,30]}`. Path `$.items[1]` returns 20.

- **T1-195** `Read_ArrayIndex_OutOfBounds_ReturnsNull`  
  DOM: `{"items":[10]}`. Path `$.items[5]` returns null.

- **T1-196** `Read_DottedSegment_NotAnObject_ReturnsNull`  
  DOM: `{"items":[1,2,3]}`. Path `$.items.x` — `items` is array not object, returns null.

- **T1-197** `Read_QuotedKeySegment_NotAnObject_ReturnsNull`  
  DOM: `{"x": 42}`. Path `$.x['key']` — x is a value not object, returns null.

- **T1-198** `Read_ArrayIndexOnNonArray_ReturnsNull`  
  DOM: `{"x": {"y": 1}}`. Path `$.x[0]` — x.y is not an array, returns null.

**TryWrite method:**

- **T1-199** `TryWrite_RootPath_ReturnsFalse`  
  Path `$` (0 segments). Returns false (writing to root is unsupported).

- **T1-200** `TryWrite_QuotedKeySegment_HappyPath`  
  DOM: `{"map":{}}`. Path `$.map['my-hyphen-key']`. TryWrite returns true; DOM contains the value.

- **T1-201** `TryWrite_ArrayIndexSegment_InBounds_WritesValue`  
  DOM: `{"arr":[1,2,3]}`. Path `$.arr[1]`. TryWrite with 99 returns true; `arr[1] == 99`.

- **T1-202** `TryWrite_ArrayIndexSegment_OutOfBounds_ReturnsFalse`  
  DOM: `{"arr":[1]}`. Path `$.arr[10]`. Returns false.

- **T1-203** `TryWrite_LastSegment_NotAnObject_ForDotted_ReturnsFalse`  
  Build a DOM where the parent is a JsonArray, not a JsonObject, and the last segment is DottedSegment.  
  Navigate: `$.arr.name` where `arr` is array. Returns false (parent is not JsonObject).

- **T1-204** `TryWrite_MissingIntermediateParent_ReturnsFalse`  
  DOM: `{"a": {}}`. Path `$.b.c`. Returns false (intermediate `b` missing).

**TryRemove method:**

- **T1-205** `TryRemove_RootPath_ReturnsFalse`  
  Path `$` (0 segments). Returns false.

- **T1-206** `TryRemove_ExistingDottedProperty_RemovesAndReturnsTrue`  
  DOM: `{"a":1,"b":2}`. Path `$.a`. Returns true; DOM no longer has "a".

- **T1-207** `TryRemove_MissingDottedProperty_ReturnsTrue`  
  DOM: `{"a":1}`. Path `$.b`. Returns true (already absent — idempotent remove).

- **T1-208** `TryRemove_QuotedKeySegment_RemovesAndReturnsTrue`  
  DOM: `{"map":{"my-key":"val"}}`. Path `$.map['my-key']`. Returns true; key removed.

- **T1-209** `TryRemove_ArrayIndexSegment_InBounds_RemovesAndReturnsTrue`  
  DOM: `{"items":[10,20,30]}`. Path `$.items[1]`. Returns true; items has 2 elements, [0]=10, [1]=30.

- **T1-210** `TryRemove_ArrayIndexSegment_OutOfBounds_ReturnsTrue`  
  DOM: `{"items":[10]}`. Path `$.items[5]`. Returns true (already absent — idempotent).

- **T1-211** `TryRemove_ParentIsNotJsonObject_ForDottedFinalSegment_ReturnsFalse`  
  DOM: `{"arr":[1,2]}`. Path `$.arr.x` — `arr` is array, not object.  
  Returns false (parent is not JsonObject for dotted segment).

- **T1-212** `TryRemove_MissingIntermediateParent_ReturnsFalse`  
  DOM: `{"a":{}}`. Path `$.b.c`. Returns false.

**Descend helper (via indirect coverage through TryWrite/TryRemove):**

- **T1-213** `Descend_QuotedKeyOnNonObject_ReturnsFalse`  
  Use TryRemove with path `$.x['k'].deeper` where `x` is a scalar (42). Returns false.

- **T1-214** `Descend_ArrayIndexOnNonArray_ReturnsFalse`  
  Use TryRemove with path `$.x[0].deeper` where `x` is an object not array. Returns false.

- **T1-215** `Descend_ArrayIndex_OutOfBoundsOnIntermediateParent_ReturnsFalse`  
  Use TryRemove with path `$.arr[5].key` where `arr` has 1 element. Returns false.

---

### 3.2 New test file: `EndToEndSmokeTests.cs`

**Location:** `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/EndToEndSmokeTests.cs`

**Purpose:** Smoke test that exercises the full stack from MigrationBootstrap.Build through FileSystemMigrationStorage, verifying lossless round-trip on real filesystem.

Uses: `IDisposable`, `Path.GetTempPath()`, unique temp directory per test, cleanup in Dispose.

Test to implement:

- **T4-001** `FullStack_Bootstrap_RealFilesystem_RoundTripsLosslessly`

  ```
  // ARRANGE
  Create a temp directory.
  Use MigrationBootstrap.Build(
      reg => reg.RegisterDocType("Test.Doc", 2, [new TestDocV1ToV2(), new TestDocV2ToV1()]),
      new FileSystemMigrationStorage(),
      () => "smoke-test-1.0",
      "SmokeTestTool");

  Write v1 fixture to disk:
  {"$meta":{"docType":"Test.Doc","schemaVersion":1},"items":[{"name":"alpha"},{"name":"beta"}]}

  // ACT 1: Load v1 — triggers migration to v2 (adds "kind":"default" to each item)
  var loadResult = await services.Persistent.LoadAndMigrateAsync(path);

  // ASSERT 1
  Assert.True(loadResult.WasMigrated);
  Assert.Equal(2, loadResult.CurrentMeta.SchemaVersion);
  Assert.Equal("default", loadResult.Dom["items"][0]["kind"].GetValue<string>());

  // ACT 2: Edit — rename "alpha" to "alpha-edited"
  loadResult.Dom["items"][0]["name"] = "alpha-edited";

  // Save back
  await services.Persistent.SaveAsync(path, loadResult.Dom, loadResult);

  // ACT 3: Reload — should be fast path (already v2)
  var reloadResult = await services.Persistent.LoadAndMigrateAsync(path);

  // ASSERT 3
  Assert.False(reloadResult.WasMigrated);
  Assert.Equal(2, reloadResult.CurrentMeta.SchemaVersion);
  // Edit preserved
  Assert.Equal("alpha-edited", reloadResult.Dom["items"][0]["name"]!.GetValue<string>());
  // Original "beta" unchanged
  Assert.Equal("beta", reloadResult.Dom["items"][1]["name"]!.GetValue<string>());
  ```

- **T4-002** `FullStack_Bootstrap_DuplicateJournalRegistration_Throws`

  Verify that calling the registerFormats callback to register `FdpDocumentTypes.MigrationJournal`
  a second time throws `MigrationException` (duplicate registration detected at Build time).
  This tests the safety of the auto-registration + seal sequence.

  ```csharp
  // Attempt to register MigrationJournal again — should throw MigrationException
  Assert.Throws<MigrationException>(() =>
      MigrationBootstrap.Build(
          reg => reg.RegisterPassthroughDocType(FdpDocumentTypes.MigrationJournal, 1),
          new InMemoryMigrationStorage(),
          () => "1.0",
          "Test"));
  ```

---

### 3.3 Additions to `JsonEnvelopeTests.cs`

Add at the end of the class (after T1-018). Test IDs T1-019 through T1-024:

- **T1-019** `Read_DomMeta_WithNullOptionalFields_Succeeds`  
  Build: `{"$meta":{"docType":"Test.Doc","schemaVersion":1}}` (no engineVersion, createdBy, createdUtc).  
  `JsonEnvelope.Read(root)` succeeds; `EngineVersion`, `CreatedBy`, `CreatedUtc` are all null.

- **T1-020** `Read_DomMeta_MetaNotFirstProperty_LogsWarningButSucceeds`  
  Build: `{"other":99,"$meta":{"docType":"Test.Doc","schemaVersion":1}}`.  
  `JsonEnvelope.Read(root)` succeeds with correct DocType.

- **T1-021** `WithSchemaVersion_ReturnsNewMetaWithVersion`  
  Create a `DocumentMeta("Test.Doc", 1, "1.0", "test", null)`.  
  Call `JsonEnvelope.WithSchemaVersion(meta, 3)`.  
  Assert: returned meta has `SchemaVersion == 3`, same DocType, EngineVersion, CreatedBy.

- **T1-022** `WithEngineVersion_ReturnsNewMetaWithVersion`  
  Create a `DocumentMeta("Test.Doc", 1, "1.0", "test", null)`.  
  Call `JsonEnvelope.WithEngineVersion(meta, "2.5")`.  
  Assert: returned meta has `EngineVersion == "2.5"`, same SchemaVersion.

- **T1-023** `Peek_StreamNonSeekable_Works`  
  Wrap a MemoryStream in a non-seekable wrapper (see `ReadOnlyMigrationAdapterTests.NonSeekableStream` for pattern).  
  `JsonEnvelope.Peek(nonSeekableStream)` succeeds and returns correct meta.

- **T1-024** `Peek_DomMeta_NullDocType_ThrowsMigrationException`  
  Build DOM: `{"$meta":{"docType":null,"schemaVersion":1}}`.  
  `JsonEnvelope.Read(root)` throws `MigrationException`.

---

### 3.4 Additions to `ReadOnlyMigrationAdapterTests.cs`

Add at the end of the class (after T2-010). Test IDs T2-011..T2-014:

- **T2-011** `LoadAndMigrate_NullStream_ThrowsArgumentNullException`  
  `await Assert.ThrowsAsync<ArgumentNullException>(() => adapter.LoadAndMigrateAsync(null!, "src"))`.

- **T2-012** `LoadAndMigrate_UnknownDocType_SlowPath_Throws`  
  Write a v1 file where the schema is behind current (so slow path is taken) but with an unregistered docType.  
  This hits the exception path inside `ProcessBytes` when migration is attempted on unknown type.  
  Actually this is a slight duplicate of T2-009 (already covers unknown type). Instead use:

- **T2-012** `LoadAndMigrate_IoException_WrapsInMigrationException`  
  This test is necessarily cross-platform tricky. Instead, test the `ReadOnlyLoadOutcome` invalid state:
  Skip this test and implement T2-013 instead.

- **T2-013** `ReadOnlyLoadOutcome_AsJsonObject_InvalidState_Throws`  
  Manually construct a `ReadOnlyLoadOutcome` using object initializer with both `RawContent = null` and `MigratedDom = null`.  
  Call `outcome.AsJsonObject()` — assert `InvalidOperationException`.

- **T2-014** `ReadOnlyLoadOutcome_AsJsonString_InvalidState_Throws`  
  Same as T2-013 but call `AsJsonString()`.

---

### 3.5 Additions to `MigrationPipelineTests.cs`

Add at the end of the class. Test IDs T1-130..T1-133:

- **T1-130** `MigrateToCurrent_NullRoot_ThrowsArgumentNullException`  
  `Assert.Throws<ArgumentNullException>(() => pipeline.MigrateToCurrent(null!))`.

- **T1-131** `MigrateTo_NullRoot_ThrowsArgumentNullException`  
  `Assert.Throws<ArgumentNullException>(() => pipeline.MigrateTo(null!, 2))`.

- **T1-132** `MigrateTo_DiagnosticFields_EngineVersionChanged_ThrowsMigrationException`  
  Use a migrator that changes `$meta.engineVersion`. Assert `MigrationException` is thrown.

- **T1-133** `MigrateTo_DiagnosticFields_CreatedByChanged_ThrowsMigrationException`  
  Use a migrator that changes `$meta.createdBy`. Assert `MigrationException`.

For T1-132 and T1-133 you can add new violating migrators to `TestMigrators.cs`:

```csharp
internal sealed class TestDocV1ToV2_ChangesEngineVersion : IJsonDocumentMigrator
{
    public string DocType => "Test.Doc";
    public int FromVersion => 1;
    public int ToVersion => 2;
    public void Apply(JsonObject root, MigrationContext ctx)
        => root["$meta"]!.AsObject()["engineVersion"] = "tampered";
}

internal sealed class TestDocV1ToV2_ChangesCreatedBy : IJsonDocumentMigrator
{
    public string DocType => "Test.Doc";
    public int FromVersion => 1;
    public int ToVersion => 2;
    public void Apply(JsonObject root, MigrationContext ctx)
        => root["$meta"]!.AsObject()["createdBy"] = "tampered";
}
```

---

### 3.6 Additions to `DocumentMetaTests.cs`

Check current test coverage — if `MigrationException` has uncovered constructors, add:

- **T1-050** `MigrationException_FullConstructor_PropertiesSetCorrectly`  
  Create `new MigrationException("msg", "Test.Doc", 1, 2, "path/to/file.json", "$.items[0]")`.  
  Assert all 5 properties have the correct values, and `Message == "msg"`.

- **T1-051** `MigrationException_MessageAndInner_HasInnerException`  
  Create `var inner = new Exception("cause"); var ex = new MigrationException("msg", inner);`.  
  Assert `ex.InnerException == inner`.

Add these to a new file `MigrationExceptionTests.cs` rather than `DocumentMetaTests.cs`.

---

### 3.7 Coverage verification

After implementing all new tests, run:

```
dotnet build FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj -q
dotnet test FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj --filter "FullyQualifiedName~Migrations" --no-build --collect:"XPlat Code Coverage" -q
```

Then parse the resulting `coverage.cobertura.xml` to verify the migration namespace classes
all reach ≥ 90% line and ≥ 85% branch. Compute weighted averages if needed.

**If after adding all the above tests some class still fails the threshold:**
- Read the actual source for that class
- Identify the specific uncovered lines/branches from the cobertura XML
- Add targeted tests to cover them
- Do NOT add tests just for coverage — each test must assert meaningful behavior

---

## 4. Build and Test Commands

```
dotnet build FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj -q
dotnet test FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj --filter "FullyQualifiedName~Migrations" --no-build -q
```

Expected final test count: **~264+ tests pass** (232 existing + ~32 new)

---

## 5. Report

Write your report to `.dev/json-migration/reports/BATCH-08-REPORT.md`.

Format same as BATCH-07-REPORT.md. Include:
- Final test count
- Final coverage numbers (migration namespace line% and branch%)
- Files changed
- Developer insights (issues, weak spots, decisions, edge cases, performance)

Return your report content to the dev lead when complete.

---

## 6. Quality Rules

- Convention C-7: NO FluentAssertions. Use `Assert.Equal`, `Assert.True`, `Assert.Throws<T>()` etc.
- Convention C-3: Do not add tests just to satisfy coverage numbers — all tests must assert real behavior.
- Preserve existing test IDs — do not renumber or change existing tests.
- Build must have 0 errors and 0 warnings before you declare success.
- Do not modify any production source files (only test files).
