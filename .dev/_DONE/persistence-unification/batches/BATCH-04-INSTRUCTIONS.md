# BATCH-04: [BlueprintRegistrar] self-registration bridge + Hrot.AI.Behaviors csproj wiring
**Tasks:** PU-203, PU-204  **Phase:** 2 (build-time generation)  **Est:** ~12h
**Dependencies:** BATCH-03 (the generators + `EmitTopologyCore`). This batch makes JSON-owned BTree/HSM assets **register + run** at runtime, and wires the `.json`→generator into the `Hrot.AI.Behaviors` build.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your contract.
2. `.dev/_DONE/persistence-unification/BTree_HSM_JSON_Persistence_Detailed_Design.md` — **§3 D14** (the `[BlueprintRegistrar]` masquerade — read in full), **§6.3** (parallel-generator constraint + the bridge: emit an isolated `[BlueprintRegistrar]` class with `Register(BehaviorRegistry beh, BlueprintRegistryStaging …)`; register BTree thunks via `BehaviorRegistry.RegisterAction/RegisterCondition(…BlueprintBTree{Action,Condition}Delegate)`; HSM via static `HsmActionDispatcher.RegisterAction/RegisterGuard`; also register the JSON-owned **definition/blob** since `FbtTreeCatalog` can't see it), **§14** (RESOLVED verifications + the verify-at-implementation items), **§9/§2.7** (csproj wiring). Cite it.
3. `.dev/_DONE/persistence-unification/TASK-DETAIL.md` — PU-203, PU-204 success conditions.
4. `reviews/BATCH-03-REVIEW.md`.
5. Codebase Memory MCP first; never `search_code`.

## Verify-first (REQUIRED — record findings in the report, per §14 verify-at-implementation)
Before coding the bridge, confirm via code (cite file:line):
- `AiHotReloadCoordinator.ScanForRegistrars` is `[BlueprintRegistrar]`-only and its `ResolveRegistrarArgument`/injection contract: it injects `BehaviorRegistry` (a *staging* instance, BPF-042) + `BlueprintRegistryStaging`, and THROWS on `BlueprintRegistry`/`HsmActionDispatcher` params (`FDP/Toolkits/Fdp.Toolkits/Behavior/AiHotReloadCoordinator.cs` ~:330-345). So the bridge signature is `Register(BehaviorRegistry, BlueprintRegistryStaging)` and must call `HsmActionDispatcher` **statically**, not via injection.
- BTree thunk registration delegates: `BehaviorRegistry.RegisterAction(int,string,BlueprintBTreeActionDelegate)` / `RegisterCondition(…BlueprintBTreeConditionDelegate)` (`FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorRegistry.cs` ~:24,34,226,233). Confirm exact signatures + the delegate types.
- HSM static registration: `HsmActionDispatcher.RegisterAction/RegisterGuard` exact static signatures.
- **How a JSON-owned tree/HSM DEFINITION (blob) is registered today** so the bridge can do it (the generated `CreateBuilder().Build()` yields the blob; find the registration entry point the bridge must call — the design says `FbtTreeCatalog` can't see JSON-owned defs, so the bridge self-registers). Confirm editor-owned trees (`SampleScout`/`SampleGuard`) are otherwise unwired at runtime today (§14 item 2) — so the bridge is net-new wiring, not a replacement.

## Tasks (sequence; don't start the next until the current's tests pass.)

### Task 1 — PU-203: emit the `[BlueprintRegistrar]` self-registration bridge — file: `Hrot.AiEditor.Generators` (UPDATE both generators) + emit-core helper if cleaner (UPDATE)
The generator emits, **per editor-owned asset**, an isolated class decorated **`[BlueprintRegistrar]`** (NOT `[FbtRegistrar]`/`[HsmActionRegistrar]`) with `public static void Register(BehaviorRegistry beh, BlueprintRegistryStaging staging)` (match the coordinator's injectable signature exactly). Inside:
- build the blob from the generated `CreateBuilder()` and **register the JSON-owned tree/HSM definition** (the path you verified above);
- **BTree:** register each action/condition thunk via `beh.RegisterAction/RegisterCondition(...)` with the `BlueprintBTree*Delegate`;
- **HSM:** statically call `HsmActionDispatcher.RegisterAction/RegisterGuard`.
The bridge is **additive** — it does not change `CreateBuilder()`+thunk (the BATCH-03 generated topology core). Emit it as a separate class/part so the PU-205 topology-core equivalence (which excludes the bridge, §14 item 3) stays green.
**Tests required (integration — the core of this batch):** compile a JSON-owned tree's generated output (topology core + bridge) — e.g. via the existing in-memory Roslyn test harness or a GeneratorDriver+emit-and-compile — load into a collectible ALC, run `AiHotReloadCoordinator.ScanForRegistrars` over it, and assert: the bridge class is discovered (it carries `[BlueprintRegistrar]`, requests only injectable params), invoking `Register` into a staging `BehaviorRegistry` registers the tree's action/condition thunks AND the definition, and the result is **tickable** (the registered tree executes). Do the analogous HSM test via `HsmActionDispatcher`. (Mirror how existing coordinator/registrar tests drive `ScanForRegistrars` + registration.) **Negative:** assert the bridge does NOT carry `[FbtRegistrar]`/`[HsmActionRegistrar]` and does NOT request `BlueprintRegistry`/`HsmActionDispatcher` as params (which the coordinator would reject).

### Task 2 — PU-204: Hrot.AI.Behaviors csproj wiring — file: `Hrot/Subsystems/Hrot.AI.Behaviors/Hrot.AI.Behaviors.csproj` (UPDATE)
Add `<AdditionalFiles Include="Trees/**/*.btree.json" />` and `<AdditionalFiles Include="Machines/**/*.hsm.json" />`; reference the `Hrot.AiEditor.Generators` analyzer (as the Blueprint generator is referenced — `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` or the equivalent the Blueprint generator uses — verify the exact wiring in `Hrot.AI.Behaviors.csproj` for `Hrot.Blueprints.Generators`); ensure generated `.cs` lands in `obj/GeneratedFiles`. **Do NOT decommit `Trees/SampleScout.cs`/`Machines/SampleGuard.cs` yet** (that's PU-402, after migration PU-401 produces the `.json`); there are no `.btree.json`/`.hsm.json` under `Hrot.AI.Behaviors` yet, so the glob currently matches zero files — wiring is dormant-but-correct. Keep hand-written `.cs` (Brains/*) compiling.
**Tests required:** `dotnet build` of `Hrot.AI.Behaviors` succeeds with the generator referenced and the AdditionalFiles globs present (zero `.json` matched today → no generated output, no break). If you can drop a temporary `.btree.json` fixture under a test path to prove the glob feeds the generator end-to-end in the build, do so and remove it; otherwise rely on the BATCH-03 GeneratorDriver tests for generation proof and assert here only that the build is unbroken. Document the approach.

## Success Criteria
- [ ] PU-203: generator emits a per-asset `[BlueprintRegistrar]`-only bridge with the exact coordinator-injectable signature; registers the JSON-owned definition + BTree thunks (`BehaviorRegistry`) + HSM thunks (`HsmActionDispatcher` static). Integration test: discovered by `ScanForRegistrars`, registered, **tickable** (both BTree + HSM). Negative test on attributes/params.
- [ ] PU-203: PU-205 topology-core equivalence (BATCH-03) still green (bridge is additive/separate, excluded from the core compare).
- [ ] PU-204: `Hrot.AI.Behaviors.csproj` has the AdditionalFiles globs + the generator analyzer ref; build unbroken; no `.cs` decommit yet.
- [ ] Global gate: `dotnet build IOS-IG-SimHost.sln` 0 errors / 0 new warnings (touched); new integration tests green; generators 26+ green; persistence gate 88 green; `EditorSubsystemBoot` 10/10; `Hrot.Editor.AiShared.Tests` green; `Hrot.Blueprints.Tests` only pre-existing (0 new). **Report exact counts/classification.**
- [ ] Report → `.dev/_DONE/persistence-unification/reports/BATCH-04-REPORT.md`.

## Report Requirements
The verify-first findings (exact `BehaviorRegistry`/`HsmActionDispatcher`/`ScanForRegistrars` signatures + how the definition/blob is registered + confirmation editor-owned trees are unwired today); the bridge's emitted shape (paste a snippet); how the integration test compiles+scans+ticks; the csproj wiring (the exact analyzer-ref form, matching Blueprint); whether the dormant glob is correct; weak points; suggested commit message. No comprehension questions.

## Constraints
Branch `blueprint-integ-1`. GizmoMap.Contracts 0.2.2. No `Hrot.IG`/DDS/`Stride/`. No `editor_stride`. Keep edits in `Hrot.AiEditor.Generators` + `Hrot.AI.Behaviors.csproj` + tests (+ emit-core helper if needed). **No `.cs` decommit; no editor load-path change (PU-301).** Don't touch the Blueprint path or its generator. Do NOT commit (the lead commits).
