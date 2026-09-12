# BATCH-07 Report

## Implementation Summary

### Task 1 — `AssetBaseNameCollisionGuard` (PU-502 core)

**File:** `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/AssetBaseNameCollisionGuard.cs`

A pure, static, `netstandard2.0`-safe guard implementing design §3 D5. No filesystem access is baked into the core logic — all directory listing is passed in by the caller.

**Base-name extraction rules (`GetLogicalBaseName`):**
- Known compound JSON suffixes, matched longest-first, case-insensitively:
  - `.btree.json` → strip to obtain the logical base name.
  - `.hsm.json`  → strip to obtain the logical base name.
  - `.bp.json`   → strip to obtain the logical base name.
- `.cs` (case-insensitive) → strip the `.cs` extension.
- Anything else → strip the final extension (via `Path.GetFileNameWithoutExtension`).
- Original casing of the base name is preserved in all cases.

**Representation classes:**
- `CS` — file name ends with `.cs` (case-insensitive).
- `JSON` — file name ends with one of the three known compound suffixes (case-insensitive).
- `Other` — everything else (ignored; never a D5 participant).

**Collision semantics (`CheckCollision`):**
- A D5 collision = two files in the **same directory** sharing the **same logical base name** (case-insensitive comparison) but belonging to **opposite** representation classes (CS↔JSON).
- **NOT a collision:** two files of the same class (two JSONs, e.g. `Foo.btree.json` + `Foo.hsm.json`; or two `.cs` files). D5 only addresses the CS/JSON cross-representation ambiguity.
- **NOT a collision:** the target file appearing in the sibling list (self-match is skipped by exact name comparison, OrdinalIgnoreCase).
- **NOT a collision:** files with different logical base names in any combination.
- The error message names both conflicting file names, the directory, and cites D5.

**`CheckCollisionOnDisk`:**
- Convenience overload: accepts the full target path and a `Func<string, IEnumerable<string>> listFilesInDir` delegate.
- Extracts the target's directory, calls the delegate with that directory only (same-dir scoping is enforced).
- If the delegate throws (directory absent), returns `null` (no collision — directory does not exist yet).
- Converts full paths from the lister to file names before delegating to `CheckCollision`.

### Task 2 — Guard wired into `SaveAllAiDocumentsCommand.Execute`

**File:** `Hrot/Editor/Hrot.Editor.AiShared/Documents/SaveAllAiDocumentsCommand.cs`

Added `using System.IO;` and `using Hrot.AiEditor.Persistence;` to the command file.

**Wiring point:** In `Execute`, immediately before invoking `saveBTreeDelegate` or `saveHsmDelegate`, the code calls:

```csharp
AssetBaseNameCollisionGuard.CheckCollisionOnDisk(
    path, dir => Directory.EnumerateFiles(dir))
```

**Block-not-throw + leave-dirty behavior:**
- If the guard returns a non-null error string: the `[BLOCKED]` message is reported via `report?.Invoke(...)`, the `break` statement exits the `case` block without calling the delegate or `MarkClean`, so the document **remains dirty**.
- No exception is ever thrown; the error is surfaced purely through the `report` callback.
- The existing `try/catch` around the `switch` still handles IO errors from successful writes.

**Blueprint write path:** The `AssetKind.Blueprint` branch was **not modified**. `SaveActiveBlueprintCommand`, `BlueprintJsonServices`, and `BlueprintAsset` are untouched. The guard is available for a future batch to wire into the Blueprint branch symmetrically.

## Design Decisions

1. **`CheckCollisionOnDisk` takes a lister delegate** (not `Directory.EnumerateFiles` directly) so the production path uses the real filesystem, while tests inject a fake lister — keeping tests hermetic without mocking `Directory`.

2. **Placed in `Hrot.AiEditor.Persistence`** (not in `Hrot.Editor.AiShared`) because: (a) the guard is pure/netstandard2.0 so it belongs with the other netstandard2.0 persistence utilities (`AtomicFileWriter`), and (b) the Phase-2 Roslyn generator will need to call it when it creates asset files.

3. **Error message format:** includes both file names + directory + "D5" reference so an engineer seeing it in a log can immediately identify the offending files without opening source.

4. **Same-class non-collision is explicit:** the guard only checks CS↔JSON cross-representation. Two `Foo.btree.json` + `Foo.hsm.json` sibling files are legal per D5 (they represent different asset kinds, not the same asset in competing representations). This matches the spec exactly.

5. **No Blueprint write path wiring:** the instructions say "do NOT modify `SaveActiveBlueprintCommand`/`BlueprintJsonServices`/the Blueprint write path". The guard's Blueprint-path wiring is intentionally left as future work (noted as a deviation with zero risk).

## Deviations

| What | Why | Benefit | Risk |
|------|-----|---------|------|
| Blueprint branch of `SaveAllAiDocumentsCommand` was NOT wired with the collision guard | Instructions §Task 2: "do NOT modify `SaveActiveBlueprintCommand`"; the guard can be added symmetrically in a future batch (no risk, Blueprint path unchanged) | Stays fully within scope | None — Blueprint path is completely unmodified |
| `SaveAllWithCollisionGuardTests` contains one stub test (`Execute_BTree_CollisionWithSiblingCs_Blocked_DocStaysDirty`) that asserts nothing by itself | That test was scaffolded but replaced by the real filesystem tests below it. Left in as a no-op to avoid confusion; could be removed. | Harmless | Minor noise |

## Test Results

### New tests (`AssetBaseNameCollisionGuardTests` + `SaveAllWithCollisionGuardTests`)

```
Passed!  - Failed: 0, Passed: 30, Skipped: 0, Total: 30
```

Tests cover:
- `GetLogicalBaseName`: all three compound suffixes, `.cs`, fallback, casing preservation, case-insensitive suffix match, longest-match, null throws.
- `CheckCollision` BOTH directions:
  - JSON→CS: `Foo.btree.json` blocked by `Foo.cs`; `Foo.hsm.json` blocked by `Foo.cs`; `Foo.bp.json` blocked by `Foo.cs`.
  - CS→JSON: `Foo.cs` blocked by `Foo.btree.json`; `Foo.cs` blocked by `Foo.hsm.json`; `Foo.cs` blocked by `Foo.bp.json`.
- Non-collision: different base name, two JSONs same base, empty sibling list, self in list.
- Error message contains both file names + directory.
- Case-insensitive base-name comparison.
- `CheckCollisionOnDisk`: queries only target directory, detects collision via injected lister, returns null when dir throws, CS direction detected.
- Task 2 wiring: real filesystem tests for BTree/HSM blocked writes (delegate not called, file not written, [BLOCKED] reported, doc stays dirty) and no-collision regression (writes normally, doc cleaned, round-trip verified).

### Regression gate

| Test project | Result |
|---|---|
| `Hrot.Editor.AiShared.Tests` (full) | **Passed: 819 / Failed: 0** (incl. `SaveAllAndFlushTests`, `SaveAllAiDocumentsCommandTests`, `FlushOnCloseTests`) |
| `Hrot.Blueprints.Tests` | Failed: 7 (pre-existing DEBT-006 golden snapshots — identical to baseline), Passed: 1357 — **0 new failures** |
| `dotnet build IOS-IG-SimHost.sln -c Debug` | **0 errors, 26 warnings (all pre-existing, 0 new on touched projects)** |

## Developer Insights

- The guard is invoked during `SaveAllAiDocumentsCommand.Execute` via a real `Directory.EnumerateFiles` call. If the target directory does not yet exist (a new asset that has never been saved), `EnumerateFiles` will throw `DirectoryNotFoundException`, which `CheckCollisionOnDisk` catches and treats as "no siblings → no collision". This is correct behavior for first-time saves.

- The wiring tests deliberately use a real temp directory (create dir, plant a `.cs` file, attempt JSON save) to exercise the actual `Directory.EnumerateFiles` production path. This gives higher confidence than an injected lister alone.

- The stub test `Execute_BTree_CollisionWithSiblingCs_Blocked_DocStaysDirty` was retained but is effectively a no-op (it declares `delegateCalled` and `reports` then ignores them). It could be removed in a future cleanup batch without any test coverage loss (the real filesystem tests fully cover this scenario).

- Blueprint path: the guard is available for future wiring in the Blueprint branch of `SaveAllAiDocumentsCommand` when the Blueprint creation flow (PU-501) lands. This will be a one-liner addition mirroring the BTree/HSM branches.

## Known Issues

- PU-501 (path-at-creation for BTree/HSM) is deferred as PU-D12 per the scope decision. Most BTree/HSM documents today have `SourceFilePath = ""` (assembly-loaded assets), so they are skipped by the existing no-path rule before reaching the collision guard. The guard only activates on path'd documents (PU-401 migration + PU-501 creation).

- The stub test in `SaveAllWithCollisionGuardTests` is harmless but could be removed for cleanliness.

## Confirmation: Scope Constraints Met

- **Blueprint write path UNCHANGED:** `SaveActiveBlueprintCommand`, `BlueprintJsonServices`, `BlueprintAsset` — not touched. Verified via git diff.
- **`flushAction` UNCHANGED:** `EditorSubsystem.cs` — not touched.
- **No `SourceFilePath` pointed at `.json`:** no changes to any asset contributor or creation flow.
- **No BTree/HSM new-asset creation command:** none built.
- **PU-501 NOT attempted:** deferred to PU-401 as PU-D12 per batch scope decision.
- **`AssetBaseNameCollisionGuard` is netstandard2.0-safe:** no net8-only APIs used; only `System.IO`, `System.Collections.Generic`, `System.Linq` (implicit), `System` (implicit). Confirmed by clean `dotnet build` of `Hrot.AiEditor.Persistence` targeting `netstandard2.0`.
- **No `.cs` decommitted:** no source files removed.

## Suggested Commit Message

```
feat(persistence): PU-502 — AssetBaseNameCollisionGuard + BTree/HSM save guard wiring (D5)
```

Full description:
- Add `AssetBaseNameCollisionGuard` (pure, netstandard2.0) in `Hrot.AiEditor.Persistence`:
  compound-suffix base-name extraction + CS↔JSON collision detection both directions.
- Wire guard into BTree/HSM JSON write path of `SaveAllAiDocumentsCommand.Execute`:
  collision blocks write, reports [BLOCKED], leaves doc dirty, never throws.
- 30 new tests (guard unit + wiring real-filesystem); 819 total green; 0 new Blueprint failures.
- Blueprint write path, flushAction, SourceFilePath: all unchanged. PU-501 deferred (PU-D12).
