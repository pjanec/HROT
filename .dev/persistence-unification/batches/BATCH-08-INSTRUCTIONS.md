# BATCH-08: Migration-equivalence harness + migration JSON generation (PU-401, part 1 of Phase 4)
**Tasks:** PU-401 (equivalence proof + JSON generation). **PU-402 (decommit) is a SEPARATE later batch — do NOT decommit anything here.**  **Phase:** 4  **Est:** ~9h
**Dependencies:** BATCH-01..05 (DTOs, mappers, JSON services, generators, bridge, dual-load). **PU-D06 RESOLVED 2026-06-05** — equivalence = **blob/behavioral** (see below).

## PU-D06 decision (authoritative — the criterion you implement)
Migration equivalence is **blob/behavioral equivalence**, NOT byte-identical `.cs`:
> Produce the runtime blob from the COMMITTED `.cs` AND from the JSON-REGENERATED `.cs`, then compare the two blobs structurally. Equal blobs ⇒ runtime behavior unchanged ⇒ migration is safe.
This supersedes the D1/§6.4/§11 "byte-identical `.cs`" wording and subsumes debt PU-D04 + PU-D05.

## CRITICAL scope (regression safety) — read carefully
**This batch is PURELY ADDITIVE. Touch NOTHING in the live build:**
- Do **NOT** create any `*.btree.json`/`*.hsm.json` under `Hrot/Subsystems/Hrot.AI.Behaviors/Trees|Machines/` (the globs are ACTIVE — a real file there would make the generator emit a registrar while `SampleScout.cs`/`SampleGuard.cs` still define one → duplicate registration, AND the BATCH-07 collision guard would fire). The migration JSON you produce is a **test artifact / staging file**, written under the TEST's temp dir or `.dev/persistence-unification/migration-artifacts/` — NOT the live asset folders.
- Do **NOT** delete or edit `Trees/SampleScout.cs` or `Machines/SampleGuard.cs`.
- Do **NOT** change `flushAction`, the csproj, or any registration path.
Decommit + live-tree placement is **PU-402 (next batch)**.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your contract.
2. `.dev/persistence-unification/BTree_HSM_JSON_Persistence_Detailed_Design.md` — **§11 (migration)**, §6.4/§6.5, §3 (D1, D14). Cite. (Note: the byte-identical wording is SUPERSEDED by PU-D06 → blob/behavioral; cite that.)
3. `.dev/persistence-unification/TASK-DETAIL.md` — PU-401 success conditions.
4. `reviews/BATCH-03-REVIEW.md` + `reviews/BATCH-04-REVIEW.md` (generator + bridge).
5. Codebase Memory MCP first; never `search_code`.

## Verified seams (lead-confirmed via research — re-verify, cite)
- **Committed→blob (reference side):** the test assembly already contains `SampleScout.Build()` → `BehaviorTreeBlob` (`Trees/SampleScout.cs:44`) and `SampleGuard.Compile()` → `HsmDefinitionBlob` (`Machines/SampleGuard.cs:56`). Call them directly.
- **Committed→JSON chain:** `BTreeAssetContributor.LoadFrom(asm)` (reflects `[BTreeDefinition]` + `[BTreeLayout]`) → pick the `SampleScout` `BehaviorTreeAsset` → **`ToDtoWithTypeNames(asset, "SampleScout")`** (recovers `BrainBlackboard`/`BTreeContext` via reflection on `CreateBuilder`'s return generic — the plain `ToDto` leaves type names empty → emitter would emit `BTreeBuilder<,>` which won't compile) → `BTreeJsonServices.Serialize(dto)`. HSM: `HsmAssetContributor.LoadFrom` → `HsmAssetMapper.ToDto` (no type-name trick needed) → `HsmJsonServices.Serialize`. The `ToDtoWithTypeNames` helper exists in `BlueprintRegistrarBridgeIntegrationTests` (`Hrot.AiEditor.Generators.Tests`) ~:81-110 — reuse/lift it.
- **JSON→regenerated `.cs`→blob (test side):** run the JSON through `CSharpGeneratorDriver` with `BTreeJsonGenerator`/`HsmJsonGenerator` (as `MigrationEquivalenceTests` already does) to get the topology-core `{Name}.g.cs` + `{Name}.Registrar.g.cs`. Compile the topology core in-process via the EXISTING helper `CompileMultiAndLoad(string[] sources, asmName)` in `BlueprintRegistrarBridgeIntegrationTests.cs` ~:169-220 (built on `InMemoryRoslynCompiler` + `MetadataReferenceResolver.ForRuntimeAssemblies(AppDomain.CurrentDomain.GetAssemblies())`). Then reflection-invoke the generated `Hrot.AI.Behaviors.Trees.SampleScout.Build()` / `...Machines.SampleGuard.Compile()` to get the regenerated blob.
- **Blob types/fields to diff** (no existing equality helper — write a structural compare):
  - `BehaviorTreeBlob` (`Fbt.Kernel/BehaviorTreeBlob.cs`): `TreeName, Version, StructureHash, ParamHash, Nodes[] (NodeDefinition 8B struct: Type,ChildCount,SubtreeOffset,RawPayloadIndex), MethodNames[], FloatParams[], IntParams[], SubtreeAssetIds[]`. Ignore `[NonSerialized]` `CompiledDelegate`/`DebugMetadata`.
  - `HsmDefinitionBlob` (`Fhsm.Kernel/Data/HsmDefinitionBlob.cs`): `Header` (StructureHash, ParameterHash, counts), `States` (StateDef[] 32B), `Transitions` (TransitionDef[] 16B), `Regions`, `GlobalTransitions`, `ActionTable`, `GuardTable` via span accessors. Compare element-by-element (the fixed-layout structs allow `MemoryMarshal.AsBytes` byte compare). Ignore the managed `Metadata` sidecar.
- **Vacuous tests to replace** (PU-D05): `MigrationEquivalenceTests.BTree_SampleScout_EquivalenceTest_FailsLoudly_WhenDiverged` (~:186-197) and `Hsm_SampleGuard_EquivalenceTest_FailsLoudly_WhenDiverged` (~:253-264) — both assert `reference + "// DIVERGED" != reference` (tautology). DELETE and replace with the real blob-divergence test below.
- **Layout persistence:** `BehaviorTreeAssetDto` persists per-node canvas layout (X/Y, `BehaviorTreeAssetDto.cs:48,84`). The committed→JSON chain reads `[BTreeLayout]` (`SampleScoutDiscoveryTests` proves layout-is-applied). So the migration JSON MUST contain non-empty layout → assert it (decommit safety: editor restores layout from JSON via PU-301).

## Tasks (sequence; don't start the next until the current's tests pass)

### Task 1 — Blob structural-compare helper — NEW test helper (in `Hrot.AiEditor.Generators.Tests`)
A test-only `BlobEquivalence` helper: `AssertEqual(BehaviorTreeBlob a, BehaviorTreeBlob b)` and `AssertEqual(HsmDefinitionBlob a, HsmDefinitionBlob b)` — field/array/element compares per the field lists above; clear failure messages naming the first differing field/index. (Keep it in the test project; it's a proof tool, not production.)

### Task 2 — PU-401 blob-equivalence tests (the heart) — `MigrationEquivalenceTests.cs` (UPDATE: replace the 2 vacuous tests)
For BOTH `SampleScout` (BTree) and `SampleGuard` (HSM):
- `*_BlobEquivalence_CommittedVsJsonRegenerated`:
  1. reference blob = call the committed `SampleScout.Build()` / `SampleGuard.Compile()` directly.
  2. migrated JSON = committed→JSON chain (with `ToDtoWithTypeNames` for BTree).
  3. regenerated blob = JSON → generator driver → `CompileMultiAndLoad(topologyCore)` → reflection-invoke generated `Build()`/`Compile()`.
  4. `BlobEquivalence.AssertEqual(reference, regenerated)` — **the PU-D06 criterion**.
- `*_BlobEquivalence_FailsLoudly_WhenJsonDiverges` (replaces the vacuous tautology — PU-D05): take the migrated JSON, MUTATE a behavior-affecting field (e.g. flip a FloatParam / change a node type / drop a transition), regenerate the blob, and assert `BlobEquivalence.AssertEqual` **throws** (a real divergence-detection test). Use `Assert.Throws`/`Should().Throw`.

### Task 3 — PU-401 migration-JSON generation + validation — `MigrationEquivalenceTests.cs` (ADD) + write artifacts
- `*_MigrationJson_RoundTrips_And_CarriesLayout`: generate the migration JSON for each asset; assert it `Deserialize`s back to an equal DTO (byte-stable re-serialize), AND that it contains non-empty per-node layout (X/Y) matching the committed `[BTreeLayout]` (decommit safety — PU-402 relies on this). For BTree assert `BlackboardTypeName`/`ContextTypeName` are populated (the `ToDtoWithTypeNames` recovery).
- Write the two validated JSON files to `.dev/persistence-unification/migration-artifacts/SampleScout.btree.json` and `Machines/SampleGuard.hsm.json` (create the folder). These are the EXACT files PU-402 will move into the live tree. (A test that writes them is fine; OR a small one-shot the lead can eyeball. Do NOT put them under `Trees/`/`Machines/` live folders.)

## Success Criteria
- [ ] PU-401: blob/behavioral equivalence PROVEN for SampleScout + SampleGuard (committed blob ≡ JSON-regenerated blob), per the PU-D06 criterion. + the real divergence test (replaces the PU-D05 tautologies; they are DELETED).
- [ ] Migration JSON generated + validated: round-trips byte-stable, carries layout (X/Y) + (BTree) populated BB/Ctx type names; written to `.dev/persistence-unification/migration-artifacts/` (NOT the live tree).
- [ ] **Nothing in the live build changed:** no `.json` under live `Trees|Machines/`; `SampleScout.cs`/`SampleGuard.cs` untouched; csproj/flushAction untouched. (PU-402 does decommit.)
- [ ] Global gate: `dotnet build IOS-IG-SimHost.sln` 0 errors / 0 new warnings (touched); new + existing `Hrot.AiEditor.Generators.Tests` green (incl. the real byte-identical gate tests already present); `EditorSubsystemBoot` 10/10; `Hrot.Blueprints.Tests` only pre-existing (0 new). Report exact counts.
- [ ] Report → `.dev/persistence-unification/reports/BATCH-08-REPORT.md`.

## Report Requirements
The blob-compare helper's field coverage (what's compared, what's ignored + why); how the regenerated blob is obtained (driver → CompileMultiAndLoad → reflection-invoke); the divergence test's mutation + that it throws; confirmation the migration JSON round-trips + carries layout + (BTree) type names, and WHERE the artifacts were written; explicit confirmation NOTHING in the live tree/csproj/flushAction changed; any blob field that legitimately differs and why it's excluded from the equivalence (justify); weak points; suggested commit message. No comprehension questions.

## Constraints
Branch `blueprint-integ-1`. GizmoMap.Contracts 0.2.2. No `Hrot.IG`/DDS/`Stride/`. **Do NOT decommit `.cs`; do NOT place `.json` in live `Trees|Machines/`; do NOT change the csproj or flushAction.** Do NOT commit (the lead commits).
