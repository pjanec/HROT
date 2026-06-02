# BATCH-10 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-02

## Summary
Game-side layout-contracts assembly extracted + sample BTree/HSM assets added. **Full solution builds (0 errors)** — the user's core requirement. Editor suites green; samples discovered. The earlier build-blocking layering bug (emitter emits `[BTreeLayout]`/`BTreeEditorLayout` into the runtime project, but those types lived only in the heavy editor assembly) is resolved.

## Verification performed (ran myself)
- **`dotnet build IOS-IG-SimHost.sln` → Build succeeded, 0 errors.** (Authoritative; not a filtered run.)
- `Hrot.BTree.Editor.Tests` **377/377**, `Hrot.Hsm.Editor.Tests` **330/330**, `Hrot.Editor.AiShared.Tests` 702 (per report), `EditorSubsystemBoot` 10/10. Blueprints 10 pre-existing (DEBT-006), no new.
- **Fhsm.Tests: 2 failed** (`OrthogonalRegionTests.OutputLane_Conflict_Detected`, `FailSafeTests.InfiniteLoop_Detected_And_Stops`) — **verified pre-existing**: stash-tested at baseline 8e197569 (Batch-10 stashed), they fail identically. Kernel runtime tests, unrelated to the layout/attribute change → **DEBT-009**.
- **Deleting `Fhsm.Kernel.HsmLayoutAttribute` was safe**: nothing in FastHSM referenced it (the full build compiles `Fhsm.Tests`; the 2 failures are runtime region/loop, not attribute).
- Samples: real discovery tests — `BTreeAssetContributor.LoadFrom(BehaviorsAssembly)` → `assets.Should().Contain(a => a.Name == "SampleScout")`; same for `SampleGuard`. `SampleScout` = Root→Sequence→{Wait,Wait} + `[BTreeLayout]`; `SampleGuard` = Idle⇄Scanning + `[HsmLayout]`, fixed GUIDs.

## Implementation notes (good)
- New `Hrot.Editor.AiContracts` (net8.0, no ImGui/NodeEdit) holds 12 moved types with **namespaces unchanged** → no using-edits across the editor. Referenced by both `Hrot.AI.Behaviors` and `Hrot.Editor.AiShared`. `LayoutDiscovery` stayed editor-side. Added to the .sln.
- `BTreeFluentEmitter.LayoutNamespace` fixed `Hrot.AI.Behaviors.Trees.Layout` → `Hrot.Editor.AiShared.Layout` (matches actual + HSM emitter).
- Incidental fix: `HsmAssetContributor.LoadFrom` now passes `blob.Metadata` (was `null`) → correct state names/layout for `.Compile()`-built assets.

## Issues Found
None blocking. DEBT-009 (pre-existing Fhsm kernel failures) recorded.

## Verdict
APPROVED. The editor now has openable sample assets and authored/emitted BTree/HSM assets compile in the runtime project (save round-trip unblocked).

## Commit Message
```
feat(editor): game-side layout-contracts assembly + sample BTree/HSM assets (BATCH-10)

- New lightweight Hrot.Editor.AiContracts: moved BTree/HSM/Blueprint layout-contract types out
  of the heavy Hrot.Editor.AiShared (namespaces unchanged), referenced by both Hrot.AI.Behaviors
  (runtime) and the editor. Fixes the layering break that prevented emitted [BTreeLayout]/[HsmLayout]
  files from compiling in the runtime project.
- Removed duplicate Fhsm.Kernel.HsmLayoutAttribute (canonical now in AiContracts).
- Fixed BTreeFluentEmitter layout namespace to match where the types live.
- Fixed HsmAssetContributor.LoadFrom to pass blob.Metadata (was null).
- Added discoverable samples: SampleScout (BTree) + SampleGuard (HSM) with [Definition]+[Layout].

Full solution builds (0 errors). Tests: BTree 377, HSM 330, EditorSubsystemBoot 10/10.
Pre-existing Fhsm kernel failures (2) unrelated — DEBT-009.
```
