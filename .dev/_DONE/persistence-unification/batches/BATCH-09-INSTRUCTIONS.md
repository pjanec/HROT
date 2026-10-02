# BATCH-09: Atomic decommit + flushAction→JSON switch (PU-402, Phase 4 part 2 — COMPLETES Phase 4)
**Tasks:** PU-402 (decommit) + resolve PU-D11 (flushAction→JSON). **Phase:** 4  **Est:** ~10h
**Dependencies:** BATCH-08 (PU-401: blob-equivalence PROVEN; validated migration JSON staged in `.dev/_DONE/persistence-unification/migration-artifacts/`). **PU-D06 RESOLVED.**

## What this batch does (lead has fully researched the blast radius — follow exactly)
Swap the two committed hand-authored editor assets to JSON (the generator takes over runtime emission via the PU-203 `[BlueprintRegistrar]` bridge), fix the tests whose premise changes, and flip the debounced edit-to-live flush to write JSON (now safe — see below). This COMPLETES the migration critical path.

### Lead-verified facts (re-verify, cite) — these de-risk the batch:
- **`SampleScout`/`SampleGuard` are the ONLY editor-owned BTree/HSM assets** (Trees/ and Machines/ contain only those two `.cs`; default Compile glob, no explicit `<Compile Include>`). They are **NOT registered into the live runtime registry today** (absent from `AiBehaviorFactory`/`CgfBehaviorSetup`) — they're editor/test fixtures. So decommit causes **no runtime-registry regression**.
- **`FbtTreeCatalog.GetSampleScout()` will vanish** from the Fdp-generated catalog (one generator can't see another's output) — **but it has ZERO callers** (confirmed). Non-event. (`AiBehaviorFactory` registers MoveToLocation/FollowRoute/… NOT SampleScout.)
- **The PU-203 bridge IS wired into real editor startup:** `EditorSubsystem.Initialize` → `_aiCoordinator.TriggerInitialLoad()` → `LoadAndScan` → `ScanForRegistrars` discovers `SampleScoutRegistrar`/`SampleGuardRegistrar` (from the generated `*.Registrar.g.cs`) and invokes `Register(BehaviorRegistry, BlueprintRegistryStaging)`. (EditorSubsystem.cs ~563-574.)
- **JSON contributors already wired** in `EditorSubsystem.Initialize` (~596-602) pointing at `Trees/` & `Machines/`; on AssetId collision the JSON contributor **wins** over the assembly-reflection contributor (`AiAssetCatalogBuilder` adds JSON contributors last), so the editor shows the **JSON layout**. Double-discovery is benign.
- **Migration JSON correctness (HIGH risk — verified):** the staged artifacts carry `BlackboardTypeName="Fdp.Toolkit.Behavior.Components.BrainBlackboard"`, `ContextTypeName="Fdp.Toolkit.Behavior.BTreeContext"`, HSM `AssetId="979df4a4-..."`, and per-node/state layout (X/Y). The PU-401 bridge-tickable test proved the generated code compiles + ticks with these. Re-confirm before trusting.
- **flushAction safety (resolves PU-D11):** today `flushAction` routes BTree/HSM → `emitService.Emit(asset)` which writes **C# to `asset.SourceFilePath`** (`EditorSubsystem.cs:2293`). After migration the migrated assets are JSON-owned (`SourceFilePath = *.btree.json`), so the UNCHANGED flushAction would **overwrite the `.json` with C# (clobber)**. PU-D11 was deferred only because un-migrated `.cs` assets existed — **after migrating the only two, that reason is gone**, so flipping the flush to write JSON is now correct + safe.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your contract.
2. `BTree_HSM_JSON_Persistence_Detailed_Design.md` — **§11 (migration/decommit), §6.4, §8 (flush), §3 (D1, D11, D14)**. Cite.
3. `reviews/BATCH-08-REVIEW.md` (equivalence proven), `reviews/BATCH-04-REVIEW.md` (bridge), `reviews/BATCH-06-REVIEW.md` (Save-All JSON serialization to reuse for the flush).
4. `TASK-DETAIL.md` PU-402.
5. Codebase Memory MCP first; never `search_code`.

## Tasks (sequence; build+test after EACH task)

### Task 1 — PU-402 atomic decommit (the swap)
- `git mv .dev/_DONE/persistence-unification/migration-artifacts/SampleScout.btree.json Hrot/Subsystems/Hrot.AI.Behaviors/Trees/SampleScout.btree.json`
- `git mv .dev/_DONE/persistence-unification/migration-artifacts/Machines/SampleGuard.hsm.json Hrot/Subsystems/Hrot.AI.Behaviors/Machines/SampleGuard.hsm.json`
- `git rm Hrot/Subsystems/Hrot.AI.Behaviors/Trees/SampleScout.cs Hrot/Subsystems/Hrot.AI.Behaviors/Machines/SampleGuard.cs`
- (the now-empty `.dev/.../migration-artifacts/` dir + its `Machines/` subdir can be removed)
- `dotnet build IOS-IG-SimHost.sln` MUST succeed: the generator emits `SampleScout.g.cs`+`SampleScout.Registrar.g.cs` and `SampleGuard.g.cs`+`SampleGuard.Registrar.g.cs` into `obj/GeneratedFiles`; `Hrot.AI.Behaviors.dll` still exposes `Hrot.AI.Behaviors.Trees.SampleScout.Build()` and `...Machines.SampleGuard.Compile()` (generated). Confirm `FbtTreeCatalog.g.cs` no longer has `GetSampleScout` and that this does NOT break the build (zero callers).

### Task 2 — Fix the tests whose premise changed (6 known; handle any others that surface)
After Task 1, the assembly no longer carries `[BTreeLayout]`/`[HsmLayout]` or `.Layout()`. Disposition (cite each, verify by building the test projects):
- **DELETE (obsolete — `.Layout()` gone, build errors):**
  - `Hrot.BTree.Editor.Tests/SampleScoutDiscoveryTests.SampleScout_Layout_ReturnsNonNullWithExpectedNodes`
  - `Hrot.Hsm.Editor.Tests/SampleGuardDiscoveryTests.SampleGuard_Layout_ReturnsNonNullWithExpectedStates`
- **MIGRATE to the JSON layout source (preserve coverage — layout now lives in JSON):**
  - `SampleScoutDiscoveryTests.BTreeAssetContributor_LoadFrom_SampleScout_LayoutIsApplied` → assert layout via **`BTreeJsonAssetContributor`** discovering the now-live `Trees/SampleScout.btree.json` (non-zero node X/Y). (The assembly contributor now yields null layout by design — that's expected, not a regression.)
  - `SampleGuardDiscoveryTests.HsmAssetContributor_LoadFrom_SampleGuard_LayoutIsApplied` → same via **`HsmJsonAssetContributor`** + `Machines/SampleGuard.hsm.json`.
- **CONVERT to read the live committed JSON (no longer regenerable from the assembly's `[BTreeLayout]`):**
  - `Hrot.AiEditor.Generators.Tests/Equivalence/MigrationEquivalenceTests.BTree_SampleScout_MigrationJson_RoundTrips_And_CarriesLayout` → read `Trees/SampleScout.btree.json` from disk, assert round-trips byte-stable + carries layout + type names. (Stop regenerating from `LoadBTree`, which now has no layout.)
  - `..._Hsm_SampleGuard_MigrationJson_RoundTrips_And_CarriesLayout` → read `Machines/SampleGuard.hsm.json`.
- **KEEP (survive — verify):** the discovery + AssetId + `Build()`/`Compile()` tests; the blob-equivalence + byte-identical + divergence tests (they call the generated `Build()`/`Compile()` and don't assert layout; note the equivalence is now JSON-idempotence rather than committed-cs-vs-json — add a one-line comment).
- If ANY other test breaks because it depended on the committed `.cs` source / `[*Layout]`, fix it the same way (migrate to JSON or delete-if-obsolete) and list it in the report.

### Task 3 — Resolve PU-D11: flip the debounced flushAction to write JSON (BTree/HSM)
In `EditorSubsystem.cs` (the `RegenerationScheduler` `flushAction`, ~2284-2295): replace the BTree/HSM `emitService.Emit(asset)` call with a **JSON write** to `asset.SourceFilePath`, reusing the EXACT serialization the BATCH-06 `SaveAllAiDocumentsCommand` uses (BTree: `BehaviorTreeAssetMapper.ToDto → BTreeJsonServices.Serialize → AtomicFileWriter.Write`; HSM: `HsmAssetMapper.ToDto → HsmJsonServices.Serialize → AtomicFileWriter.Write`). Wire it via the same injected-delegate pattern already used for Save-All (avoid circular refs — reuse the existing `saveBTreeDelegate`/`saveHsmDelegate` wiring if present, or mirror it). Run the BATCH-07 `AssetBaseNameCollisionGuard.CheckCollisionOnDisk` before writing (post-migration there's no `.cs` sibling so it won't fire — but keep the guard). Blueprint branch UNCHANGED (`_blueprintQuickReloadTrigger`). No-path/empty `SourceFilePath` → skip (don't write). Never throw out of the flush.
- **Scope note (be explicit in the report):** this makes the flush PERSIST correctly (writes valid JSON, no clobber). The end-to-end edit→MSBuild-regen→hot-reload **latency/reload loop** is NOT headlessly verifiable and is the subject of Phase 9 (≤100 ms quick reload) + the user's manual editor smoke (deferred). Prove headlessly: flushing a dirty JSON-owned BTree/HSM doc writes round-trippable JSON to its `.json` `SourceFilePath` (NOT C#).

## Success Criteria
- [ ] PU-402: `SampleScout.cs`/`SampleGuard.cs` DELETED; `Trees/SampleScout.btree.json` + `Machines/SampleGuard.hsm.json` committed; full solution builds 0 errors / 0 new warnings; generated topology-core + bridge present; `FbtTreeCatalog` loss is benign (no callers).
- [ ] The PU-203 bridge integration tests (`BlueprintRegistrarBridgeIntegrationTests`, incl. `BTree_SampleScout_Bridge_Register_TreeIsTickable`) still GREEN — the regression anchor proving JSON-owned assets register→tick.
- [ ] All 6 affected tests handled (2 deleted, 2 migrated to JSON contributor, 2 converted to read live JSON); any other casualties handled + listed.
- [ ] PU-D11 resolved: flushAction writes JSON (not C#) for BTree/HSM to `SourceFilePath`; collision guard honored; Blueprint flush + Ctrl+S unchanged; never throws. + headless test (flush a dirty JSON-owned doc → JSON written + round-trips; NOT C#).
- [ ] Global gate: `dotnet build IOS-IG-SimHost.sln` 0/0; `Hrot.AiEditor.Generators.Tests` green; `Hrot.BTree.Editor.Tests` + `Hrot.Hsm.Editor.Tests` green (with migrated tests); `Hrot.Editor.AiShared.Tests` green; `EditorSubsystemBoot` 10/10; `Hrot.Blueprints.Tests` only pre-existing (0 new). Report EXACT counts.
- [ ] Report → `.dev/_DONE/persistence-unification/reports/BATCH-09-REPORT.md`.

## Report Requirements
Confirm the swap (files added/deleted) + build green + generated files present; FbtTreeCatalog loss confirmed benign (no callers); the bridge-tickable anchor green; per-test disposition table (deleted/migrated/converted/kept + why) with file:line; the flushAction change (what it writes now, the guard, no-throw, Blueprint untouched) + the EXPLICIT manual-verify note for the edit-to-live reload loop; whether `AiAssetEmitService.Emit` (the old .cs writer) is now unused (note it, don't remove); weak points; suggested commit message. No comprehension questions.

## Constraints
Branch `blueprint-integ-1`. GizmoMap.Contracts 0.2.2. No `Hrot.IG`/DDS/`Stride/`. Do NOT touch `BlueprintJsonServices`/`BlueprintAsset`/the Blueprint `Save`/`_blueprintQuickReloadTrigger` path. Do NOT remove `AiAssetEmitService` (may be used elsewhere/tests). Do NOT commit (the lead commits).
