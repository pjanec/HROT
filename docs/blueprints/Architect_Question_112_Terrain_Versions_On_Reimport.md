<!--STATUS
state: LIVE
updated: 2026-11-16
current-answer: §0 the ruling (R-4280) · §2 the sub-questions as approved · §4 AS BUILT (B, A, C built; D not)
stale-below: nothing
related-designs:
  - docs/DESIGN_Ground_Patches_And_Road_Blockage.md — OWNS D-4 (R-4235: "a re-import is a NEW terrain version … a batch step re-compiles every scenario on that terrain"); this question settles HOW the old version is found
  - docs/DESIGN_Terrain_Authoring.md — OWNS the kept ground (E-1 / E-3: source/base INSIDE a terrain folder, excluded from its hash) — the precedent for A
-->

# Architect Question 112 — terrain VERSIONS on a re-import (D-4's "how")

**Asked by:** `terrain` lane, `2026-11-14`, while building D-4 (CE-4403's last open item). **Decides:** where the old version of a re-imported terrain lives and how a
scenario or a recording finds it. ⚠ Large blast radius: the residency and the catalog every node loads with.

## 0. ✅ RULED `2026-11-16` (R-4280)

🔒 User, verbatim: *"approved · R-5000 lean accepted, vehicles should be able to cross the 10cm curb"* — A–D approved as recommended, **plus one change to
A** (coordinator): `versions/` is GIT-IGNORED even inside a committed demo terrain (`.gitignore` keeps generated terrain out of git, lines 73–80; the
demo terrains are committed, city-battle 9.6 MB) — an old version lives on disk and the content root like every generated terrain. Built by
`terrain` after its minefield batch, in the order B → A → C (D later).

## 1. Why this is a question, not a build

🔒 D-4 (R-4235, approved): *"a re-import is a NEW terrain version: the old one is kept by hash (recordings replay on it, as kept grounds already are); a batch step
re-compiles every scenario on that terrain and reports the ones it could not."* It does not say how a kept version is FOUND — and today nothing resolves a terrain by hash.

### INVENTORY

`scripts/cbm.sh search_graph '{"name_pattern":".*(TerrainCatalog|TerrainResidency|TerrainContentHash|TerrainImporter|OverlayCompiler|TerrainBake|Kept).*","label":"Class"}'`
→ **13** classes (tests and fakes included): `TerrainCatalog`, `TerrainContentHash`, `TerrainImporter`, `OverlayCompiler`, `TerrainBake`, `LiveTerrainBakeInfo`,
the residency, and their tests. ⚠ Coverage could not be checked (the CLI has no `check_index_coverage`).

| the lean rests on | code — how it IS | design — how it was MEANT |
|---|---|---|
| a scenario names its terrain by NAME; its overlay records the base's HASH, and the load REFUSES a different base | `TerrainResidency.cs:240-245` (C-5) | D-4: "C-5's hash check stays a guard, never a surprise" |
| a recording records the terrain's name AND hash | `RecorderTickSystem.cs:250` (`TerrainHash` = the resident's `ResolvedBaseHash`), `RecordingModule.cs:88` | O-8 |
| a terrain is resolved by name only | `TerrainCatalog.ResolveDefinition` (`:57-71`), then a kept ground | ⛔ searched docs/ and .dev/ for a resolve-by-hash: none |
| a ground can already be KEPT inside a terrain folder, excluded from its hash | `TerrainCatalog.KeptGroundOf` (`source/base`); `TerrainContentHash` skips `source/`, `navtiles/` | E-1 / E-3 |
| the importer overwrites on replace | `TerrainImporter.ImportAsync(…, replace)` (`:44`) | D-4 |

## 2. The sub-questions — each with a recommended answer

| # | question | ⭐ recommended | why | rejected |
|---|---|---|---|---|
| **A** | where does the old version live? | **inside the terrain's own folder, `versions/<hash>/`** — the whole old folder moved there by the importer before it writes the new one; `versions/` excluded from the content hash (as `source/` and `navtiles/` are) | moves, copies and deletes WITH the terrain; one folder per name, as today; the kept-ground precedent (E-1) | a sibling folder `<name>@<hash>`: a second entry in every terrain list and picker · a global store by hash: a new root every node must be told about |
| **B** | how is a version found? | **`TerrainCatalog.ResolveDefinition(name, expectedHash)`**: the current definition when its hash matches, else `versions/<expectedHash>/terrain.json`, else null (the C-5 error as today, now naming the missing version). The two callers that hold a hash pass it: the overlay load (its base hash) and the replay (the recording's `TerrainHash`) | ONE resolution rule; the callers already carry the hash — no format change, no wire change | the residency searching every version for a matching hash: a scan per load, and a name collision silently picks one |
| **C** | the batch re-compile | **after B, a CONVENIENCE, not a blocker**: an un-recompiled scenario still loads (on its old version, by B); *Re-compile scenarios on this terrain* (the orchestrator's one compile path, the import wizard's last page) moves them to the new version and reports the ones that fail | without B every scenario breaks on the re-import's commit; with B nothing breaks and the upgrade is chosen | re-compile inside the importer: the importer would need the editor's compile delegates (unique buildings, the bridge net, the net re-elevator) |
| **D** | the disk cost (a scale terrain's heights ≈ 127 MB per version) | **keep every version; a later *prune* step** deletes the versions no scenario overlay and no recording names | a recording that cannot replay is a loss; disk is cheaper | delete on re-import: breaks the replays D-4 promises |

**Build order once approved:** B (with its rail: a re-imported terrain, a scenario saved on the old one still loads, a recording replays) → A in the importer → C as the
wizard's step → D later. Blast radius: `TerrainCatalog`, `TerrainResidency`'s overlay check, the Replay Browser's terrain load, `TerrainImporter` — backend's and ui's
code (said in the build).

## 3. What would change the recommendation

A requirement that an old scenario MUST follow the new ground at once (no old version), or a disk budget that cannot keep a version per re-import.

## 4. ✅ AS BUILT `2026-11-16` (`terrain` lane, B → A → C; D, the prune, NOT built)

Nothing deviated from §2; the three decisions the build had to make are R-5060…R-5062 in [`STATUS_Terrain_Breaching_And_Versions.md`](batches/STATUS_Terrain_Breaching_And_Versions.md) §2.

| piece | as built | rail (in the touched class's own suite) |
|---|---|---|
| **B** the one rule | `TerrainCatalog.ResolveDefinition(name, expectedHash)` (`FDP/Toolkits/Fdp.Toolkits/Terrain/TerrainCatalog.cs`, beside the name-only one): the current definition when `TerrainContentHash.OfTerrain` matches (or no hash is asked for), else `<terrain folder>/versions/<hash>/terrain.json`, else null; a hash holding `/`, `\` or `.` throws. `Roots`-wide `List()` and the kept-ground scan skip anything inside a `versions/` folder (`IsInsideAVersion`) | `TerrainKeptGroundTests.ResolveByHash_FindsTheCurrentThenTheKeptVersion_ElseNull` |
| **B** overlay load | `TerrainResidency.Prepare` (`Hrot/Engine/Hrot.Core/Services/TerrainResidency.cs`, before the C-5 check): reads the overlay header's base hash and resolves with it; no version ⇒ `InvalidOperationException` naming the missing hash, the resident hash and the `versions/<hash>` that is not there. C-5's own comparison stays as the guard on the version that WAS found | `TerrainLoadStepTests.AnOverlaySavedOnAnOldBase_StillLoads_AfterTheTerrainIsReImported_AQ112B` (and the older `AnOverlayForAnotherBase_IsRefused_NamingBothHashes_O4`, unchanged) |
| **B** replay | no replay-side code: the Replay Browser opens a recording's overlay through `Prepare(name, overlayFolder)` (`ReplayBrowserSubsystem.ApplyRecordingTerrain`) and a recording's `TerrainHash` IS its overlay's header base hash (`RecorderTickSystem.cs:250` stamps it only with an overlay) — so the replay resolves the old version through the same line | `ReplayBrowserSubsystemTests.AQ112B_ARecordingMadeBeforeAReImport_ReplaysOnTheOldVersion` |
| **A** | `TerrainImporter.Swap` (`Hrot/Engine/Hrot.Terrain.Import/TerrainImporter.cs`): hash the old folder, move it to `.old-…`, move the new one in, then file the old one as `<target>/versions/<oldHash>/` (an older `versions/` is moved to the new folder first and never nested; the same content again keeps nothing; a failure while filing leaves the old folder in `.old-…` and adds a report warning — the new terrain is already in place). `ImportReport.KeptVersionHash` names it. `TerrainContentHash.IsExcluded` skips `versions/`; `.gitignore` has `assets/terrain/**/versions/` | `ImporterTests.Replace_KeepsTheOldFolderUnderVersionsByHash_AndNeverNestsThem_AQ112A` · `TerrainContentHashTests.OfTerrain_IgnoresTheKeptVersions` |
| **C** the function | `StorageProcessManager.RecompileScenariosOnTerrain(terrain)` → `RecompileReport` (moved / failed with reason / already current) (`Hrot/Subsystems/Hrot.Orchestrator/StorageProcessManager.cs`). It walks the NAS scenarios whose `terrain-overlay/overlay.json` names the terrain on a hash other than the current one and compiles each through `CompileOverlayCore` — the body of the save's `CompileTerrainOverlay`, extracted so the save and the re-compile share ONE compile (the save's wrapper keeps its delete-the-stale-overlay catch; a re-compile never deletes: a failing scenario keeps its old overlay, which still loads by B) | `StorageProcessManagerTests.AQ112C_RecompileScenariosOnTerrain_MovesTheOldOnes_ReportsTheFailing_AndLeavesTheRest` |
| **C** the entry point | ⛔ NOT wired to the import wizard's last page or any menu / HTTP route: the wizard (`Hrot.Presentation`, the editor process) imports straight into the terrains folder and has no path to the orchestrator's `StorageProcessManager`, which owns the NAS and the compile delegates. The missing piece is a cluster-op / storage-op message (a new `StorageOpType` row + an intent the master hands to `_storage`) and the wizard page that sends it and shows the report — a UI-lane slice, filed as the follow-up row in the status file |
