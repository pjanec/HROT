# BATCH-03: MigrationPipeline spec tests + JM-P1-008 (DiffToJournalConverter, UnknownsJournal, HashUtilities)

**Batch Number:** BATCH-03
**Tasks:** Corrective D-005..D-010 (pipeline spec tests + assertion fix), JM-P1-008
**Phase:** Phase 1 — Core infrastructure
**Estimated Effort:** 12-16 hours
**Priority:** HIGH
**Dependencies:** BATCH-02 completed and committed

---

## Developer Role

Your role is described in `.github\skills\developer\SKILL.md`. Read it before starting.

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)

1. **Onboarding:** `.dev/json-migration/ONBOARDING.md`
2. **Previous review:** `.dev/json-migration/reviews/BATCH-02-REVIEW.md` — read all issues found; Corrective Task 0 below fixes D-005..D-010.
3. **Task Details:** `.dev/json-migration/TASK-DETAILS.md` — section JM-P1-008.
4. **Design — Wire formats doc 02 §5** (`Migration-system.md`): UnknownsJournal wire format, filename convention, field spec, operation objects, empty journal rule.
5. **Design — Wire formats doc 02 §7** (`Migration-system.md`): Journal application order (Set-first, then Remove).
6. **Design — Interfaces doc 03 §6.1** (`Migration-system.md`): `UnknownsJournal` class contract.
7. **Design — Interfaces doc 03 §6.2** (`Migration-system.md`): `JournalOperation` and `JournalOpKind`.
8. **Design — Interfaces doc 03 §6.4** (`Migration-system.md`): `DiffToJournalConverter` — walks DiffNode tree, emits flat JournalOperation list.
9. **Design — Interfaces doc 03 §6.5** (`Migration-system.md`): `HashUtilities.ComputeContentHash`.
10. **Design — Test plan doc 06 §3.8–3.10** (`Migration-system.md`): T1-240..T1-246, T1-260..T1-273, T1-290..T1-293.
11. **Design — Test plan doc 06 §3.5** (`Migration-system.md`): Full spec table for T1-120..T1-139 — use this to understand what T1-123/124/125/129/136/138 actually require.

### Source Code Locations

- **Pipeline tests (corrective task):** `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/MigrationPipelineTests.cs`
- **Test helpers:** `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/TestMigrators.cs`, `StubMigrator.cs`
- **Implementation (JM-P1-008):** `FDP/Engine/Fdp.Core/Serialization/Migrations/` and `Internal/`
- **Internal diff types (needed for DiffToJournalConverter):** `FDP/Engine/Fdp.Core/Serialization/Migrations/Internal/Diff/`
- **Test project:** `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/`
- **Core project file:** `FDP/Engine/Fdp.Core/Fdp.Core.csproj`

### Build & Test Commands

```powershell
# Build the core project
dotnet build FDP/Engine/Fdp.Core/Fdp.Core.csproj

# Run migration tests only
dotnet test FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj --filter "FullyQualifiedName~Migrations"

# Full regression
dotnet test FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj
```

### Report Submission

**When done, submit your report to:**
`.dev/json-migration/reports/BATCH-03-REPORT.md`

**If you have questions, create:**
`.dev/json-migration/questions/BATCH-03-QUESTIONS.md`

---

## Context

BATCH-02 delivered MigrationPipeline and DomDiffer extraction. However, the pipeline test suite had five spec-required tests entirely missing (the developer used those spec IDs for different scenarios). Additionally, one assertion was weaker than the spec required. BATCH-03 first corrects these test gaps, then builds JM-P1-008 which depends on both DomDiffer (extracted in BATCH-02) and HashUtilities.

**Related Tasks:**
- [JM-P1-008](../TASK-DETAILS.md#jm-p1-008--difftojournalconverter--unknownsjournal--hashutilities) — DiffToJournalConverter + UnknownsJournal + HashUtilities

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

0. **Corrective Task 0 (D-005..D-010):** Add 5 missing spec tests + fix assertion → **ALL existing + new tests pass** ✅
1. **JM-P1-008 (HashUtilities first):** Implement HashUtilities → Write T1-290..T1-293 → **pass** ✅
2. **JM-P1-008 (DiffToJournalConverter):** Implement DiffToJournalConverter → Write T1-240..T1-246 → **pass** ✅
3. **JM-P1-008 (UnknownsJournal):** Implement UnknownsJournal → Write T1-260..T1-273 → **pass** ✅

**DO NOT** move to the next step until current step's tests are all green.

**DO NOT stop to ask permission. Work autonomously. Fix failures immediately.**

---

## ✅ Tasks

---

### Corrective Task 0: Fix pipeline spec test gaps (D-005..D-010)

**File:** `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/MigrationPipelineTests.cs`

#### Background

The BATCH-02 developer reused spec-assigned T1-IDs (T1-123, T1-124, T1-125, T1-129, T1-136) for different test scenarios than the spec requires. The spec tests for those IDs are completely absent. Study the **full spec table in `Migration-system.md` doc 06 §3.5** to understand the expected test-to-ID mapping.

The existing tests at those ID positions are:
- `T1-123` → Developer wrote: `MigrateTo_Downgrade_ReturnsDownReport` (should be spec T1-135)
- `T1-124` → Developer wrote: `MigrateToCurrent_PassthroughDocType_ReturnsEmptyReport` (should be spec T1-127)
- `T1-125` → Developer wrote: `MigrateToCurrent_UnknownDocType_ThrowsMigrationException` (should be spec T1-126)
- `T1-129` → Developer wrote: `MigrateTo_Upgrade_DirectionIsUp` (tests valid behavior but wrong ID)
- `T1-136` → Developer wrote: `MigrateTo_Downgrade_DirectionIsDown` (tests valid behavior but wrong ID)
- `T1-138` → Uses `>= TimeSpan.Zero` (spec says "positive" which means `> TimeSpan.Zero`)

#### Fix 1 (D-005/D-006/D-007): Add T1-123, T1-124, T1-125 — diagnostic fields preserved

The pipeline has an invariant (invariant 4, design doc 03 §3.4): after each migrator step, the fields `engineVersion`, `createdUtc`, and `createdBy` in `$meta` must remain unchanged from their pre-migration values. The three tests verify this.

**What to do:**
- Rename the existing misidentified tests to their correct behavioral names (e.g., `MigrateTo_Downgrade_ReturnsDownReport` becomes the function name — just update the comment/ID tag to remove the wrong T1-ID so there is no confusion).
- Add three new test methods with the correct spec names and IDs:

```
// T1-123: engineVersion is preserved after migration
[Fact]
public void MigrateToCurrent_PreservesEngineVersionField()

// T1-124: createdUtc is preserved after migration  
[Fact]
public void MigrateToCurrent_PreservesCreatedUtcField()

// T1-125: createdBy is preserved after migration
[Fact]
public void MigrateToCurrent_PreservesCreatedByField()
```

**Test shape:** Build a doc that has `engineVersion`, `createdUtc`, `createdBy` in its `$meta`, run `MigrateToCurrent` (or `MigrateTo`), then assert those fields are still present with the same values after migration. Use `MakeDoc` or extend it to accept extra meta fields. The doc must be at a version that actually triggers migration so migrators run. Example:

```csharp
// Build a v1 doc with diagnostic fields
var doc = JsonNode.Parse(
    "{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":1," +
    "\"engineVersion\":\"1.2.3\",\"createdUtc\":\"2026-01-01T00:00:00Z\"," +
    "\"createdBy\":\"TestUser\"},\"items\":[]}")!.AsObject();

pipeline.MigrateToCurrent(doc);

var meta = doc["$meta"]!.AsObject();
Assert.Equal("1.2.3", meta["engineVersion"]!.GetValue<string>());
// etc.
```

#### Fix 2 (D-008): Add T1-129 — chain halts on first failure

**What to do:** Add:
```
// T1-129: MigratorThrowsAtStep2of3_DoesNotRunStep3
[Fact]
public void MigrateToCurrent_MigratorThrowsAtStep2of3_DoesNotRunStep3()
```

**Test shape:** Use a 3-version registry (v1→v3). The step 2 migrator (v2→v3) throws. Assert that a third stub migrator (if it existed) was NOT called. You can verify this using `StubMigrator.ApplyCallCount`: register a 3-version chain where step 2 always throws a `MigrationException`, and step 3 is a `StubMigrator`. After `Assert.Throws`, check that the step 3 stub's `ApplyCallCount == 0`. Example structure:

```csharp
var step1 = new TestDocV1ToV2();           // harmless
var step1down = new TestDocV2ToV1();       // harmless
var step2up = new ThrowingMigratorV2ToV3(); // throws at step 2 (you must add this class)
var step3up = new StubMigrator("Test.Doc", 3, 4); // step 3 — must NOT be called

// Register as 3-step chain: v1 current=4 (or however you structure it)
// Build doc at v1, call MigrateToCurrent
// Assert Throws<MigrationException>
// Assert step3up.ApplyCallCount == 0
```

Add a `ThrowingMigratorV2ToV3` (or similar) to `TestMigrators.cs`.

#### Fix 3 (D-009): Add T1-136 — MigrateTo unreachable version throws

**What to do:** Add:
```
// T1-136: MigrateTo_NoPathExists_Throws
[Fact]
public void MigrateTo_NoPathExists_Throws()
```

**Test shape:** Use the default registry which has `"Test.Doc"` registered with `currentVersion: 3`. Call `MigrateTo(doc, 99)` where v99 does not exist in the chain. Assert `throws MigrationException`. This is distinct from calling with an unknown docType (which is tested elsewhere): here the docType IS registered but the requested version is out of range.

#### Fix 4 (D-010): Fix T1-138 duration assertion

**What to do:** In the test `MigrateTo_WithMigratorsRun_DurationIsPositive`, change:
```csharp
Assert.True(report.Duration >= TimeSpan.Zero);
```
to:
```csharp
Assert.True(report.Duration > TimeSpan.Zero);
```

The spec says "Duration is positive" which means strictly greater than zero.

#### Fix 5: Relabel misidentified existing tests

The existing tests at the wrong IDs should have their `// T1-NNN:` comment tags updated to remove the incorrect spec ID (or reassign to the correct spec ID from the table in doc 06 §3.5). This prevents future confusion. Do not delete those tests — the behaviors they test are valid. Just fix the ID labels in the comments.

---

### Task 1: JM-P1-008 — DiffToJournalConverter + UnknownsJournal + HashUtilities

**Design ref:** `TASK-DETAILS.md` section `JM-P1-008`. Also read design doc 02 §5, §7, and doc 03 §6.1, §6.2, §6.4, §6.5.

**Deliverable files (all new):**

| File | What it contains |
|---|---|
| `FDP/Engine/Fdp.Core/Serialization/Migrations/Internal/JournalOpKind.cs` | `enum JournalOpKind { Set, Remove }` |
| `FDP/Engine/Fdp.Core/Serialization/Migrations/Internal/JournalOperation.cs` | `sealed record JournalOperation(JournalOpKind Kind, string Path, JsonNode? Value)` |
| `FDP/Engine/Fdp.Core/Serialization/Migrations/Internal/HashUtilities.cs` | `ComputeContentHash(string content)` — SHA-256 first 16 hex lowercase |
| `FDP/Engine/Fdp.Core/Serialization/Migrations/Internal/DiffToJournalConverter.cs` | Walks `DiffNode` tree, emits flat `IReadOnlyList<JournalOperation>` |
| `FDP/Engine/Fdp.Core/Serialization/Migrations/UnknownsJournal.cs` | `Compute`, `Serialize`, `Deserialize`, `ApplyTo` — see design §6.1 |

**Namespaces:**
- `JournalOpKind`, `JournalOperation`, `HashUtilities`, `DiffToJournalConverter` → `Fdp.Core.Serialization.Migrations.Internal`
- `UnknownsJournal` → `Fdp.Core.Serialization.Migrations` (it is `internal sealed` — used only within `Fdp.Core`)

#### Subpart A: HashUtilities

Straightforward. `System.Security.Cryptography.SHA256.HashData(bytes)` produces a 32-byte hash; take the first 16 bytes and convert to lowercase hex.

```csharp
public static string ComputeContentHash(string content)
{
    var bytes = System.Text.Encoding.UTF8.GetBytes(content);
    var hash = System.Security.Cryptography.SHA256.HashData(bytes);
    return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
}
```

Note: `Convert.ToHexString` is available in .NET 5+. Verify the project targets a compatible TFM.

#### Subpart B: JournalOpKind + JournalOperation

Simple value types. `JournalOperation` is a `sealed record`:

```csharp
internal sealed record JournalOperation(
    JournalOpKind Kind,
    string Path,
    JsonNode? Value);  // non-null for Set, null for Remove
```

#### Subpart C: DiffToJournalConverter

See design doc 03 §6.4. The converter walks the `DiffNode` tree returned by `DomDiffer.Diff` and flattens it into journal operations.

`DiffNode` is a sealed class hierarchy in `Fdp.Core.Serialization.Migrations.Internal.Diff`:
- `DiffObject` — a container node with child diff nodes keyed by property name or index.
- `DiffValue` — a leaf node representing a changed/added/removed value.

Walk the tree recursively. Maintain a path stack (list of string/int segments). At each `DiffValue` leaf:
- If the value was in `pre` (original) but not `post` (lossy) — emit **Set** with the original value from `preMigrationDom`.
- If the value was in `post` but not `pre` — emit **Remove**.
- If both exist but differ — emit **Set** with the original value from `preMigrationDom`.

Use `JsonPathParser.Build(segments)` to produce the canonical path string from the segment stack.

#### Subpart D: UnknownsJournal

See design doc 03 §6.1 and wire format doc 02 §5.

Properties:
```csharp
internal sealed class UnknownsJournal
{
    public DocumentMeta JournalMeta { get; }
    public string SourceDocType { get; }
    public int SourceFileVersion { get; }
    public int DownMigratedToVersion { get; }
    public string SourceContentHash { get; }
    public IReadOnlyList<JournalOperation> Operations { get; }
    ...
}
```

**`Compute` static method:**
1. Call `DomDiffer.Diff(preMigration, postMigration)` to get the diff tree.
2. Call `DiffToJournalConverter.Convert(diffRoot, preMigration)` to get operations.
3. Populate metadata from parameters (sourceDocType, sourceVersion, etc.).
4. Build `JournalMeta` as a `DocumentMeta` with `docType = FdpDocumentTypes.MigrationJournal`, `schemaVersion = 1`.
5. The `engineVersion` and `createdBy` params come from the caller (adapter will supply them).

**`Serialize` method:**
- Serialize to indented JSON (2 spaces, `\n` newline per design §8.2).
- `$meta` first property (per design §8.4).
- `operations` array: each element has `kind` (string: `"Set"` or `"Remove"`), `path`, and optionally `value`.
- Use `System.Text.Json` for serialization.

**`Deserialize` static method:**
- Parse the JSON, validate `$meta.docType == "Fdp.MigrationJournal"` and `$meta.schemaVersion == 1`.
- Validate required fields (`sourceContentHash`, etc.) are present; throw `MigrationException` on missing required fields or wrong docType.

**`ApplyTo` method:**
- Apply journal operations to the supplied `JsonObject` root.
- **Order:** all `Set` operations first (in journal order), then all `Remove` operations (in journal order). See design doc 02 §7.
- For each `Set`: call `JsonPathParser.Parse(op.Path).TryWrite(root, op.Value?.DeepClone())`. If returns false (parent missing), skip (user-deletion-wins D-16).
- For each `Remove`: call `JsonPathParser.Parse(op.Path).TryRemove(root)`. If returns false (parent missing), skip.
- `DeepClone()` is needed because `System.Text.Json.Nodes` nodes can only belong to one parent.

---

## 🧪 Testing Requirements

**Corrective Task 0** (D-005..D-010): 6 changes (5 new tests + 1 assertion fix). All 6 must be in.

**JM-P1-008** tests — implement exactly as spec'd in `Migration-system.md` doc 06 §3.8–3.10:

**T1-240..T1-246 (DiffToJournalConverter — 7 tests):**

| ID | Test method name | Focus |
|---|---|---|
| T1-240 | `Convert_EmptyDiff_ReturnsEmptyOperations` | Null diff (identical DOMs) → empty list |
| T1-241 | `Convert_FieldMissingInLossy_EmitsSetWithOriginalValue` | Field in pre, absent in post → Set op |
| T1-242 | `Convert_FieldPresentInLossyMissingInOriginal_EmitsRemove` | Field in post, absent in pre → Remove op |
| T1-243 | `Convert_DifferentValues_EmitsSetWithOriginalValue` | Same path, different values → Set with pre's value |
| T1-244 | `Convert_NestedStructure_EmitsCorrectJsonPaths` | Nested path uses canonical form |
| T1-245 | `Convert_HyphenatedKey_EmitsBracketedPath` | GUID key → `['key']` form |
| T1-246 | `Convert_ArrayElement_EmitsIndexedPath` | Array index → `[N]` form |

**T1-260..T1-273 (UnknownsJournal — 14 tests):**

| ID | Test method name | Focus |
|---|---|---|
| T1-260 | `Compute_LosslessRoundTrip_ReturnsEmptyOperations` | Identical DOMs → empty operations |
| T1-261 | `Compute_LossyRoundTrip_ReturnsCorrectOperations` | Lossy case → correct Set operations |
| T1-262 | `Compute_PopulatesMetadata` | sourceDocType, sourceFileVersion, etc. set correctly |
| T1-263 | `Compute_PopulatesJournalEnvelope` | JournalMeta.DocType == `"Fdp.MigrationJournal"`, SchemaVersion == 1 |
| T1-264 | `Serialize_RoundTripsThroughDeserialize` | Serialize then Deserialize yields identical journal |
| T1-265 | `Deserialize_ValidJournal_ReturnsInstance` | Standard parse |
| T1-266 | `Deserialize_WrongDocType_Throws` | docType != `"Fdp.MigrationJournal"` → throws |
| T1-267 | `Deserialize_MissingFields_Throws` | Missing `sourceContentHash` → throws |
| T1-268 | `ApplyTo_SetOpExistingParent_Sets` | Standard set operation |
| T1-269 | `ApplyTo_SetOpMissingParent_Skips` | User-deletion-wins: parent gone → skip |
| T1-270 | `ApplyTo_RemoveOpExistingPath_Removes` | Standard remove |
| T1-271 | `ApplyTo_RemoveOpMissingPath_NoOp` | Idempotent: path already absent |
| T1-272 | `ApplyTo_SetThenRemoveSamePath_RemoveWins` | Set then Remove on same path: final state is removed |
| T1-273 | `ApplyTo_OperationsAppliedSetFirstThenRemove_PerOrder` | Verify Set-before-Remove ordering |

For T1-273, you need to verify the order: build a journal with a Set and a Remove for different paths, spy on which was applied first (e.g., check intermediate state or use ordering-sensitive DOM mutations).

**T1-290..T1-293 (HashUtilities — 4 tests):**

| ID | Test method name | Focus |
|---|---|---|
| T1-290 | `ComputeContentHash_ProducesExpectedHash` | Known input → known expected SHA-256 first-16-hex |
| T1-291 | `ComputeContentHash_IdenticalInputs_IdenticalOutputs` | Determinism |
| T1-292 | `ComputeContentHash_DifferentInputs_DifferentOutputs` | Sensitivity |
| T1-293 | `ComputeContentHash_Utf8Bytes_NotPlatformDependent` | Non-ASCII input (e.g. `"\u00e9"`) produces the correct UTF-8-based hash |

**For T1-290**, compute the expected hash yourself:
- Input: `"hello"` (UTF-8 bytes: `68 65 6c 6c 6f`)
- SHA-256: `2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824`
- First 16 hex chars (8 bytes): `2cf24dba5fb0a30e`
- Assert `HashUtilities.ComputeContentHash("hello") == "2cf24dba5fb0a30e"`

**Test quality requirements:**
- Every test asserts on values, not just "does not throw".
- T1-240..T1-246: verify the `Kind`, `Path`, and `Value` (for Set ops) of emitted operations.
- T1-261: verify the operation `Kind == Set`, `Path` matches the field that differed, and `Value` equals the pre-migration value (not the post-migration value).
- T1-264: verify the deserialized journal's `Operations.Count`, `Operations[N].Kind`, `Operations[N].Path` match the original.
- T1-269: applying Set when parent is missing must NOT throw; just skip.
- T1-272/T1-273: check final DOM state after apply, not just "no exception".

---

## 📊 Report Requirements

**Submit to:** `.dev/json-migration/reports/BATCH-03-REPORT.md`

Include:

1. **Completion status** — each corrective fix (D-005..D-010) ✅/❌, JM-P1-008 ✅/❌.
2. **Test results** — exact counts from `dotnet test` output (total, passed, failed, skipped).
3. **Design decisions made beyond the spec** — e.g., how you handled the `DiffNode` tree walk, how you detect "field in pre but not post" vs "field in post but not pre", how UnknownsJournal's `internal` visibility interacts with test access.
4. **Issues encountered** — anything unclear in the design, ambiguities resolved, caveats.
5. **Weak points spotted** — anything in the existing or new code that seems fragile or could cause problems later.
