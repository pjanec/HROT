# BATCH-07 Report

**Batch:** BATCH-07
**Status:** Complete

---

## Task Completion

| Task | Status | Notes |
|------|--------|-------|
| JM-P1-013 | Complete | `MigrationServices` record, `MigrationBootstrap` factory, T2-100..T2-103 all pass |

---

## Testing Results

228 + 4 = 232 / 232 passed

All prior migration tests (T1-xxx + T2-xxx from BATCH-01 through BATCH-06) continue to pass.
Four new tests added: T2-100, T2-101, T2-102, T2-103.

---

## Files Changed

### New files
- `FDP/Engine/Fdp.Core/Serialization/Migrations/Bootstrap/MigrationServices.cs`
- `FDP/Engine/Fdp.Core/Serialization/Migrations/Bootstrap/MigrationBootstrap.cs`
- `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/MigrationBootstrapTests.cs`

---

## Developer Insights

**Q1: Issues encountered?**

`IMigrationStorage` is `internal` (intentionally, because it references the `internal`
`UnknownsJournal` type via `WriteJournalAsync`/`FindJournalAsync`). The design spec shows
`Build` as `public`, but a `public` method cannot expose an `internal` parameter type
(CS0051 inconsistent accessibility). Resolution: `Build` is marked `internal`. Tests in
`Fdp.Core.Tests` can still call it because `InternalsVisibleTo("Fdp.Core.Tests")` is
already declared in `Fdp.Core.csproj`. External callers use `BuildForProduction`.

The `CycloneDdsGenerate` build error for `Fdp.Diagnostics.Contracts` is a pre-existing
issue unrelated to this batch; `Fdp.Core.Tests` does not depend on that project directly.

**Q2: Weak points spotted?**

`BuildForProduction` delegates to `Build` with `new FileSystemMigrationStorage()` and
uses `typeof(MigrationBootstrap).Assembly` for the version attribute. If the host process
wants the version from a different assembly (e.g. the app's own entry assembly), they
would need to call `Build` directly. This is acceptable for the current use cases but
worth documenting.

**Q3: Design decisions?**

- `Build` made `internal` (see Q1). This is the minimal change; no interface accessibility
  was modified.
- `MigrationServices` is a plain positional record with no logic, exactly as specified
  in §8.1.
- `BuildForProduction` reads `AssemblyInformationalVersionAttribute` from
  `typeof(MigrationBootstrap).Assembly`, matching the pattern in
  `WindowManager.cs` lines 604–607 and the spec note in TASK-DETAILS C-6.

**Q4: Edge cases discovered?**

- `registerFormats` callback running before `Seal()` means the callback could accidentally
  register `FdpDocumentTypes.MigrationJournal` again and get a `MigrationException` for
  duplicate registration. Callers must not re-register the journal type; this is documented
  via the XML doc remark.

**Q5: Performance concerns?**

None. `Build` is called once per process start-up. The `Seal()` call is O(1).
`BuildForProduction` does one reflection call to read the assembly attribute.
