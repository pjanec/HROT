# BATCH-04 Review
**Status:** ✅ APPROVED (functional) — but a **design↔codebase contradiction** surfaced; see "Escalation". **Date:** 2026-06-05

## Summary
PU-203 (`[BlueprintRegistrar]` self-registration bridge — JSON-owned BTree/HSM defs+thunks register into the
staging `BehaviorRegistry`/`HsmActionDispatcher` and are discovered by `AiHotReloadCoordinator.ScanForRegistrars`)
+ PU-204 (Hrot.AI.Behaviors csproj wiring). 11 new integration tests prove a JSON-owned tree/HSM compiles →
ALC → scanned → registered → **tickable**. Build green; gates green.

## Verified (read source + assertions, ran suites)
- Verify-first findings accurate (cited): coordinator `[BlueprintRegistrar]`-only + injects staging
  `BehaviorRegistry`+`BlueprintRegistryStaging`, throws on `BlueprintRegistry`/`HsmActionDispatcher`; BTree
  thunks via `BehaviorRegistry.Register*`; HSM via static `HsmActionDispatcher.Register*`; bridge self-registers
  the JSON-owned def (FbtTreeCatalog can't see it). Editor-owned trees unwired today — bridge is net-new wiring.
- Integration test compiles topology-core + bridge into an ALC, runs `ScanForRegistrars`, registers, **ticks**
  (BTree → `NodeStatus.Running`; HSM analogous). Negative test: bridge carries only `[BlueprintRegistrar]`,
  requests only injectable params. Real assertions. ✅
- csproj analyzer-ref matches Blueprint (`OutputItemType="Analyzer" ReferenceOutputAssembly="false"`) + dormant
  `AdditionalFiles` globs (zero `.json` today). ✅
- 3 latent emit bugs fixed (BTree `[BTreeDefinition]` had an invalid `AssetId` arg — committed SampleScout.cs has
  none; HSM interleaved order — committed SampleGuard.cs is already states-first; rootless-blob fallback). These
  bring the emit core TOWARD the valid committed structure. ✅
- **Ran myself:** build 0 errors/0 warnings; generators+bridge 37/37; persistence gate 88/88; boot 10/10;
  Blueprints 1357 pass / 7 pre-existing / 0 new.

## Issues / Debt
- **PU-D06 (P1 → escalated):** the 3 latent emit bugs prove **BATCH-02's "byte-identical gate" was tautological**
  (it compared the editor adapter to the emit core — same code path — and only `.Contain()`-checked the committed
  `.cs`). So the emit core was NEVER proven byte-identical to the committed `SampleScout.cs`/`SampleGuard.cs`, and
  in fact diverged (invalid AssetId; interleaved HSM). The committed `.cs` are hand-structured, so exact text
  reproduction may be unachievable anyway. **This contradicts the design's locked migration-equivalence premise**
  (D1/§6.4/§11: "regenerated `.cs` byte-identical to committed `.cs` proves behavior unchanged"). **Recommended
  resolution:** change the PU-401 migration-equivalence criterion from `.cs`-text-identity to **blob/behavioral
  equivalence** (compile committed `.cs` AND regenerated `.cs`; compare the resulting `BehaviorTreeBlob`/
  `HsmDefinitionBlob` — semantic equivalence). More robust; subsumes PU-D04/PU-D05. **Needs user/architect
  sign-off (locked decision).** See Escalation note to the user.
- **PU-D07 (P3):** `InternalsVisibleTo` added to `Fdp.Toolkits.csproj` for `Hrot.AiEditor.Generators.Tests`
  (to drive registration internals in the integration test). Acceptable; flagged for awareness.
- **PU-D08 (P3):** `BTreeAssetContributor` drops BB/context type names (`ToDtoWithTypeNames` test workaround);
  root-cause fix at PU-301.

## Verdict
APPROVED for PU-203/PU-204 (functionally correct, well-tested, build green). The migration-equivalence
*criterion* (PU-D06) is escalated to the user before PU-401.

## Commit Message
```
feat(persistence): [BlueprintRegistrar] self-registration bridge + AI.Behaviors csproj wiring (BATCH-04)

Completes PU-203, PU-204. The BTree/HSM generators now emit a per-asset isolated [BlueprintRegistrar]
class (Register(BehaviorRegistry, BlueprintRegistryStaging)) that self-registers the JSON-owned
definition + BTree thunks (BehaviorRegistry.Register*) + HSM thunks (static HsmActionDispatcher.Register*);
discovered by AiHotReloadCoordinator.ScanForRegistrars on rebuild + quick reload (D14 masquerade, no
HR-001 change). Hrot.AI.Behaviors.csproj references the generator as an analyzer + AdditionalFiles globs
for Trees/**/*.btree.json + Machines/**/*.hsm.json (dormant: zero .json today; no .cs decommit yet).
Fixed 3 latent emit bugs surfaced by first-time compilation of generated output: invalid AssetId on
[BTreeDefinition] (committed SampleScout.cs has none), interleaved HSM state/transition order (GoTo
forward-ref throw), rootless-blob fallback — all bring emit toward the valid committed structure.
Tests: 11 integration (JSON-owned BTree/HSM compiled->ALC->ScanForRegistrars->registered->tickable +
attribute/param negatives); generators+bridge 37/37; persistence gate 88/88; boot 10/10; Blueprints
7 pre-existing/0 new.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
```
