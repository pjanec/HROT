# BATCH-07: Base-name collision guard (D5)
**Tasks:** PU-502 (PU-501 deferred — see "Scope" below)  **Phase:** 5 (path-at-creation + roots)  **Est:** ~5h
**Dependencies:** BATCH-01 (`Hrot.AiEditor.Persistence` netstandard2.0 layer; `AtomicFileWriter` lives here). **Independent of PU-D06.**

## Scope decision (READ FIRST — the lead has already triaged this)
Phase 5 has two tasks. **Only PU-502 is shippable now.**
- **PU-501 (path-at-creation) is DEFERRED to PU-401** (recorded as debt **PU-D12**). Reason, lead-verified via research:
  - `AiAssetEmitService.Emit` (`Hrot.Editor.AiShared/Emit/AiAssetEmitService.cs:68-76`) writes the generated **C# source** to whatever `asset.SourceFilePath` points at. The debounced editor `flushAction` (`EditorSubsystem.cs:2283-2295`) is deliberately UNCHANGED (PU-D11) — it still routes BTree/HSM through `emitService.Emit`. So if path-at-creation set `SourceFilePath` to a `.btree.json`/`.hsm.json`, the **next edit would overwrite that JSON with C# text**. Pointing `SourceFilePath` at `.json` is only safe after the flushAction→JSON switch lands at PU-401 (blocked on PU-D06).
  - BTree/HSM also have **no new-asset creation flow** today (assets are assembly-reflection projections via `BTreeAssetContributor`/`HsmAssetContributor`, `SourceFilePath = string.Empty`). Building one now to write `.cs` would be throwaway work undone by migration.
  - **Do NOT touch the flushAction, do NOT set `SourceFilePath` to a `.json` anywhere, do NOT build a BTree/HSM creation command in this batch.**
- **PU-502 (base-name collision guard)** is fully safe + self-contained + headlessly testable, and it closes a real gap (research confirmed **no duplicate-name guard exists anywhere**). That is this batch.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your contract.
2. `.dev/_DONE/persistence-unification/BTree_HSM_JSON_Persistence_Detailed_Design.md` — **§3 (D5)** (base-name collision: a `.cs` and a `.json` must not share a base name in the same location) and **§9**. Cite D5.
3. `.dev/_DONE/persistence-unification/TASK-DETAIL.md` — PU-502 success conditions.
4. Codebase Memory MCP first; never `search_code`.

## Background facts (lead-verified — re-verify, cite)
- Fixed roots: `Trees/` (BTree), `Machines/` (HSM), `Blueprints/` (Blueprint), under `Hrot/Subsystems/Hrot.AI.Behaviors/`. Committed editor-owned `.cs` live there today (`Trees/SampleScout.cs`, `Machines/SampleGuard.cs`).
- Editor-owned JSON extensions (per the generators' AdditionalFiles globs in `Hrot.AI.Behaviors.csproj`): `*.btree.json` (BTree), `*.hsm.json` (HSM), `*.bp.json` (Blueprint).
- The collision D5 forbids: in the SAME directory, a hand-authored/generator-fed `<base>.cs` AND an editor-owned `<base>.{btree|hsm|bp}.json` both claiming the same logical asset (the generator would emit a registrar for the JSON while the committed `.cs` also defines one → duplicate registration / ambiguous source-of-truth).
- `AtomicFileWriter.Write(path, content)` already exists in `Hrot.AiEditor.Persistence` (netstandard2.0). `SaveAllAiDocumentsCommand.Execute` (BATCH-06, `Hrot.Editor.AiShared/Documents/`) is where we own the BTree/HSM JSON write (dispatch by `doc.Kind`; per-doc `report(...)` + leaves dirty on skip).

## Tasks (sequence; don't start the next until the current's tests pass)

### Task 1 — PU-502 core: `AssetBaseNameCollisionGuard` — NEW file in `Hrot.AiEditor.Persistence` (netstandard2.0, pure — no IO deps beyond what's passed in)
A pure, static guard. No filesystem scanning baked in (testable) — accept the sibling listing as input, plus a directory-scanning convenience overload.

Suggested API (adjust names to match house style; keep it pure + ns2.0):
```csharp
namespace Hrot.AiEditor.Persistence;

public static class AssetBaseNameCollisionGuard
{
    // Known editor-owned compound JSON suffixes (longest-match first): ".btree.json", ".hsm.json", ".bp.json".
    // Returns the logical base name for a file: "Foo.btree.json" -> "Foo", "Foo.cs" -> "Foo", "Foo.hsm.json" -> "Foo".
    public static string GetLogicalBaseName(string fileName);

    // Core rule: given a target file we intend to create/save and the file names already present in the SAME directory,
    // return null if OK, or a human-readable error if a cross-representation collision exists
    // (a .cs and a .{btree|hsm|bp}.json sharing the same logical base name).
    public static string? CheckCollision(string targetFileName, IEnumerable<string> siblingFileNames);

    // Convenience: scan the target's own directory from disk (uses the supplied directory listing delegate so it stays testable;
    // production passes Directory.EnumerateFiles). Returns null if OK or the error message.
    public static string? CheckCollisionOnDisk(string targetFilePath, Func<string, IEnumerable<string>> listFilesInDir);
}
```
Rules to implement precisely:
- `GetLogicalBaseName`: strip the longest matching known compound JSON suffix (`.btree.json`/`.hsm.json`/`.bp.json`); else strip a single `.cs`; else strip the final extension. Case-insensitive on the suffix; preserve the base name's original casing.
- `CheckCollision`: compute the target's logical base name + its "representation class" (CS vs JSON). A collision = a sibling with the SAME logical base name (case-insensitive compare of base names) but the OPPOSITE representation class (CS↔JSON). Same-class siblings (two JSONs, two `.cs`) are NOT a collision here (that's a different concern). The target itself appearing in the sibling list is ignored (skip exact-name self match). The error message must name both files + the directory and state the D5 rule.
- Only consider files in the SAME directory (the caller passes same-dir siblings; `CheckCollisionOnDisk` enforces this by listing only the target's directory).

**Tests required (Hrot.Editor.AiShared.Tests or a new Hrot.AiEditor.Persistence.Tests — match where AtomicFileWriter is tested):**
- `GetLogicalBaseName`: all three compound suffixes, `.cs`, plain — correct base + casing preserved; case-insensitive suffix (`Foo.BTree.JSON`).
- `CheckCollision` BOTH directions (the success condition): creating `Foo.btree.json` when `Foo.cs` exists → error; creating `Foo.cs` when `Foo.btree.json` exists → error; same for `.hsm.json` and `.bp.json`.
- No collision: `Foo.btree.json` + `Bar.cs`; two JSONs same base (not a CS↔JSON collision); empty sibling list; self in the list is ignored.
- Error message contains both file names + the directory.
- `CheckCollisionOnDisk`: with an injected lister, only the target's directory is consulted.

### Task 2 — PU-502 wiring: enforce the guard in the JSON write path we own — `SaveAllAiDocumentsCommand` (UPDATE) (+ Blueprint save guard only if safe; see constraint)
- In `SaveAllAiDocumentsCommand.Execute`, for the **BTree/HSM** branch (the path'd-doc JSON write), before writing call `AssetBaseNameCollisionGuard.CheckCollisionOnDisk(targetJsonPath, dir => Directory.EnumerateFiles(dir))`. If it returns a non-null error: **do NOT write**, emit it via `report(...)` as a `[BLOCKED]` (or `[ERROR]`) line, and **leave the doc dirty** (mirror the no-path skip behavior). Never throw.
- **Blueprint:** do NOT modify `SaveActiveBlueprintCommand`/`BlueprintJsonServices`/the Blueprint write path (§16 risk; same constraint as BATCH-06). The guard is available for the migration/creation batch to wire into the Blueprint path then. (If you can add the check in `SaveAllAiDocumentsCommand`'s Blueprint branch WITHOUT touching `SaveActiveBlueprintCommand`, do so symmetrically; otherwise skip Blueprint wiring and note it.)
**Tests required (headless):** a dirty path'd BTree doc whose target dir already contains a sibling `<base>.cs` → `Execute` does NOT write the `.json`, reports a `[BLOCKED]`/`[ERROR]` line, doc stays dirty. A doc with no colliding sibling → writes normally (regression: BATCH-06 round-trip still green).

## Success Criteria
- [ ] PU-502: `AssetBaseNameCollisionGuard` (pure, ns2.0) — base-name extraction (compound suffixes) + CS↔JSON collision detection BOTH directions + same-dir scoping. + tests covering both directions (the explicit success condition).
- [ ] Guard wired into the BTree/HSM JSON write in `SaveAllAiDocumentsCommand`: a collision **blocks the write**, reports it, leaves the doc dirty, never throws. + test.
- [ ] Blueprint write path UNCHANGED (`SaveActiveBlueprintCommand`/`BlueprintJsonServices` untouched).
- [ ] flushAction UNCHANGED; no `SourceFilePath` pointed at `.json`; no new BTree/HSM creation command (PU-501 deferred — note it in the report).
- [ ] Global gate: `dotnet build IOS-IG-SimHost.sln` 0 errors / 0 new warnings (touched); new tests green; `Hrot.Editor.AiShared.Tests` green (esp. `SaveAllAndFlushTests` — BATCH-06 regression); `SaveActiveBlueprintCommandTests` 8/8; `EditorSubsystemBoot` 10/10; `Hrot.Blueprints.Tests` only pre-existing (0 new). Report exact counts.
- [ ] Report → `.dev/_DONE/persistence-unification/reports/BATCH-07-REPORT.md`.

## Report Requirements
Where the guard lives; the base-name extraction rules + how compound suffixes are matched; the CS↔JSON collision semantics + what is intentionally NOT a collision (same-class); the wiring point + the block-not-throw + leave-dirty behavior; confirmation Blueprint write path + flushAction are UNCHANGED and no `SourceFilePath` was pointed at `.json`; confirmation PU-501 was NOT attempted (deferred, PU-D12); weak points; suggested commit message. No comprehension questions.

## Constraints
Branch `blueprint-integ-1`. GizmoMap.Contracts 0.2.2. No `Hrot.IG`/DDS/`Stride/`. No `editor_stride`. **Do NOT change the debounced `flushAction`; do NOT set `SourceFilePath` to a `.json`; do NOT build a BTree/HSM new-asset creation command; do NOT touch `BlueprintJsonServices`/`BlueprintAsset`/the Blueprint `Save` write path; do NOT decommit `.cs`.** The guard is netstandard2.0-safe (no net8-only APIs). Do NOT commit (the lead commits).
