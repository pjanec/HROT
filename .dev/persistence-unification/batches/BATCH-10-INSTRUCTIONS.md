# BATCH-10: Editor smoke-test fixes (post-migration regressions)
**Tasks:** 3 regressions surfaced by the user's manual smoke test after PU-402.  **Est:** ~5h
**Dependencies:** BATCH-09 (assets migrated to JSON). Root-cause analysis: `.dev/persistence-unification/SMOKE-FINDINGS.md` (read it).

## Context (lead already root-caused these — file:line given; re-verify, then fix)
After migrating SampleScout/SampleGuard to JSON + deleting their `.cs` (which removed `[BTreeLayout]`/`[HsmLayout]`), three editor regressions appeared. Lead-verified facts:
- Canvas node-move ALREADY updates the asset model + ToDto serializes it: `BTreeCommandSink.ApplyNodeMoves` does `node.Position = m.NewPosition; _asset.MarkDirty();` (`BTreeCommandSink.cs:96-104`); `BehaviorTreeAssetMapper.ToDto` reads `node.Position`→`EditorMetadata.X/Y` (`BehaviorTreeAssetMapper.cs:205-208`). So the model↔DTO position round-trip is fine — the bugs are in load/dedup/dirty/perspective wiring, NOT serialization.
- `AssetCatalog.Rebuild` builds `_byId[assetId]=asset` last-writer-wins, but `All => _cache` is the **merged list WITH duplicates** (`AssetCatalog.cs`). The browser lists `_catalog.All` and opens the clicked instance (`AssetBrowserWindow.cs:178,194`).
- JSON contributors are added to the catalog AFTER the assembly contributors (so JSON wins by id) — `AiAssetCatalogBuilder` ctor (~:73-85).

## Onboarding
1. `.dev/.guides/DEV-GUIDE_claude.md` — your contract.
2. `.dev/persistence-unification/SMOKE-FINDINGS.md` — the symptom→cause table.
3. Codebase Memory MCP first; never `search_code`.

## Tasks (sequence; build+test after each)

### Task 1 — Bug #1: migrated assets open with NO layout (nodes piled at origin)
Two coupled fixes (BOTH needed):
- **1a — load the JSON contributors.** In `EditorSubsystem.Initialize` (~line 601-602), the JSON contributors call `Discover(rootDirectory: …)` which only scans headers; `_assets` stays empty (`LoadAll` never runs) → the catalog has only the assembly-projected asset (no `[*Layout]` → flat). Change both to **`Refresh(rootDirectory: …)`** (Discover + LoadAll + ContributorChanged). Verify `BTreeJsonAssetContributor.Refresh(string? rootDirectory)` / `HsmJsonAssetContributor.Refresh(...)` signatures (`BTreeJsonAssetContributor.cs:111`). Confirm the root path (`BaseDirectory/../../../Hrot/Subsystems/Hrot.AI.Behaviors/Trees|Machines`) resolves at editor runtime; if it may not, log a warning when `Directory.Exists` is false (don't silently no-op).
- **1b — dedup the catalog so the browser shows ONE entry (the JSON one with layout).** After 1a, BOTH the assembly contributor (SampleScout via generated `[BTreeDefinition]`, NULL layout) AND the JSON contributor (with layout) hold the same AssetId → the browser (`_catalog.All`) would show it **twice** and could open the layout-less assembly copy. Fix `AssetCatalog.Rebuild` so `_cache` is **deduped by AssetId, last-writer (JSON) wins** (mirror `_byId`'s last-wins), preserving a stable order. Then `All`, the browser list, and `FindByName` all yield the single JSON (layout-bearing) instance.
  - Check other `All` consumers (comparison/aggregation/anything iterating the catalog) — dedup-by-id (JSON wins) is the correct semantic for all UI consumers; runtime registration is separate (the bridge), not via `All`. Note any consumer that genuinely needed duplicates (there shouldn't be).
**Tests required (headless):** an `AssetCatalog` test — add an "assembly-like" contributor and a "JSON-like" contributor both exposing the same AssetId with DIFFERENT layout/positions; assert `All` contains exactly ONE entry for that id and it's the LATER (JSON) contributor's instance (the one with non-zero positions); `FindByAssetId`/`FindByName` return the same JSON instance.

### Task 2 — Bug #2: Save-All doesn't persist layout (BTree/HSM/Blueprint)
In `EditorSubsystem` the `doc.Asset.Changed` handler (~line 2204) calls `schedulerRef.Schedule(doc.Asset)` but **never marks the DOCUMENT dirty** → `SaveAllAiDocumentsCommand.Execute` skips it (`if (!doc.IsDirty) continue;`). Add `doc.MarkDirty();` in that handler (before/with `Schedule`). (Model→DTO position sync already works — see Context — so once the doc is dirty, Save-All serializes the moved positions correctly.)
- Verify `AiDocument.MarkDirty()` exists + that `doc.IsDirty` is what `SaveAllAiDocumentsCommand` checks. Confirm this fixes all three kinds (the handler is per-doc regardless of kind).
**Tests required:** if there's a headless seam around the Changed→doc-dirty wiring, add a test (e.g. raising `asset.Changed` marks `doc.IsDirty`). If it's only reachable through `EditorSubsystem` (not headless), rely on `EditorSubsystemBoot` + the existing `SaveAllAndFlushTests` (which already prove a dirty doc is saved) and note the manual-verify.

### Task 3 — Bug #3: opening an HSM doesn't switch the perspective
`AssetKind.Hsm.ToString()` = `"Hsm"`, but the HSM perspective is registered as `"HSM"` (`EditorSubsystem.cs:1802-1803` registrar + `:2108` canvas window `assetKind:"HSM"`). The forward switch (`AiDocumentManager.Activate` → `_perspectiveSwitchCallback(doc.Kind.ToString())`) and the reverse (`WindowManagerPerspectiveSwitcher` ~:70 `if (doc.Kind.ToString() == newPerspective)`) both use `Kind.ToString()` → `"Hsm"` never matches `"HSM"` → no-op. (BTree/Blueprint match by luck.)
- Fix with a **single canonical `AssetKind → perspective-name` mapping** (BTree→"BTree", Hsm→"HSM", Blueprint→"Blueprint") used in BOTH directions (the Activate forward call site AND `WindowManagerPerspectiveSwitcher` ~:70), so display names stay "HSM" and casing fragility is gone. (Do NOT just lowercase the registrar — keep the "HSM" display.) Put the helper somewhere both sites can share (e.g. a small static on `AssetKind`/in AiShared).
**Tests required (headless):** a mapping test (Hsm→"HSM", BTree→"BTree", Blueprint→"Blueprint"); if `WindowManagerPerspectiveSwitcher`'s match is headless-reachable, a test that a doc of Kind=Hsm matches perspective "HSM".

## NOT in scope (report, don't fix)
- `[x]` close in the OPEN section: code is correctly wired (`AssetBrowserWindow.cs:164-165` → `mgr.Close(doc)`); likely a live ImGui interaction artifact. Note for the user to re-repro; do not change.
- "Blueprint not registered" on Run: pre-existing — requires the "Compile / Reload Blueprint" step first (`BlueprintAttachService.cs:100-104`). Not ours.
- Asset-browser friendliness/type indicators: that's Phase 7 (PU-701), out of scope.

## Success Criteria
- [ ] #1: JSON contributors loaded (`Refresh`); catalog deduped by AssetId (JSON wins) → migrated SampleScout/SampleGuard open with their saved layout, listed once. + AssetCatalog dedup test.
- [ ] #2: `doc.MarkDirty()` wired in the Changed handler → Save-All persists moved positions (all kinds). + test/where-headless.
- [ ] #3: canonical Kind→perspective map used both directions → opening an HSM switches to the HSM perspective; display still "HSM". + mapping test.
- [ ] Global gate: `dotnet build IOS-IG-SimHost.sln` 0/0; `Hrot.Editor.AiShared.Tests` green (+ new AssetCatalog/mapping tests); `Hrot.BTree.Editor.Tests`/`Hrot.Hsm.Editor.Tests` green; `EditorSubsystemBoot` 10/10; `Hrot.Blueprints.Tests` only pre-existing (0 new). Report exact counts.
- [ ] Report → `.dev/persistence-unification/reports/BATCH-10-REPORT.md`.

## Report Requirements
Each fix (what + file:line); the dedup semantics + any other `All` consumers checked; confirmation model→DTO position sync already worked (so #2 is just MarkDirty); the canonical map + both call sites; which bugs are headlessly tested vs manual-verify (the canvas render + perspective UI need the user's re-smoke); the out-of-scope items confirmed; weak points; suggested commit message. No comprehension questions.

## Constraints
Branch `blueprint-integ-1`. Do NOT touch the Blueprint write path / `BlueprintJsonServices` / the bridge / generators. Do NOT change `AssetKind` enum VALUES (only add a mapping helper). Keep the "HSM" display name. Do NOT commit (the lead commits).
