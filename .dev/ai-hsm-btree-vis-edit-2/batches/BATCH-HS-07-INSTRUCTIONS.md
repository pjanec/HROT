# BATCH-HS-07 — Showcase `.hsm.json` + Starter recipe

**Task:** TASK-HS-07. **One objective only.** Author a rich showcase HSM machine (`.hsm.json`) + add an in-code "Starter" recipe (one Simple state flagged Initial) to `HsmNewAssetService`.

Design ref: TASK-DETAIL.md §TASK-HS-07; Forward-Plan §4/§5 (EH-04). Schema template = the existing `Hrot/Subsystems/Hrot.AI.Behaviors/Assets/HSMs/SampleGuard.hsm.json` (READ IT FIRST — copy its exact shape: `$meta`/AssetId/Name/TargetNamespace/BlackboardTypeName/States/Regions/Transitions/GlobalTransitions/Events/Canvas/Blackboard).

## Working agreement (MANDATORY — restated)
1. **One task per batch.** Touch only the files below. Do NOT modify the command sink, model, renderers, or `SampleGuard.hsm.json`.
2. **No cheating to pass.** Do NOT invent/register fake actions or guards to force a binding (that was rejected on BTree, D-05). Do NOT add an `[HsmGuard]` to production code. If a real binding doesn't exist, leave the field null per the decision below. If blocked, STOP + write the blocker.
3. **Finish without asking** — build + test until `Failed: 0`, then report.
4. **Headless only.** 5. **Tests assert behavior** (deserializes, round-trips, validates, recipe valid), not strings. 6. **Litter-free.** 7. **Report = truth.**

## Binding decision (VE-DEBT-004 — do NOT deviate)
- HSM **actions** exist: bind state `OnEntryAction` / `ActivityAction` (and a transition `ActionFunction` or two) to the real `Hrot.AI.Behaviors.CgfHsmNodes.StubIdle`.
- There is **NO real `[HsmGuard]`** in production. Therefore **every transition `GuardFunction` MUST be null** in the showcase. Do not bind, fake, or author a guard. (Same gap as BTree's conditions — VE-DEBT-004.)

## Files
- **NEW** `Hrot/Subsystems/Hrot.AI.Behaviors/Assets/HSMs/HsmShowcase.hsm.json` — the showcase (auto-discovered from the HSM asset root).
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/HsmNewAssetService.cs` — add the in-code "Starter" recipe (mirror `BTreeNewAssetService` `_starterRecipe` + `MakeStarterDto`).
- Read: `BTreeNewAssetService.cs` (recipe pattern), `HsmJsonServices.cs` (Serialize/Deserialize), the `HsmAssetDto` DTO shape, `HsmValidator` (so the showcase passes with no Errors).
- Tests: `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/` — new file (showcase + recipe).

## Showcase content (must deserialize, round-trip byte-stable, and validate with ZERO Errors)
Author a machine demonstrating the HSM features. Use fresh GUIDs for every StableId/VisualId; wire parent/child via the DTO's child-id arrays + ParentStableId exactly as `SampleGuard.hsm.json` does. Include:
- A top-level **composite** state with an **initial** child (so `CompositeWithoutInitialChild` does NOT fire).
- A **parallel** state with **≥2 regions**, each region with its `InitialChildStableId` set to one of its children.
- A **history** (or deep-history) pseudo-state **inside a composite** (not at root — avoid `HistoryOutsideComposite`).
- A **final** state with **no children and no outgoing transitions** (avoid `FinalStateWithChildren` / `FinalStateWithOutgoingTransition`).
- **≥2 events**; transitions carrying `EventName` (referencing a defined event — avoid `EventReferenceDangling`), `GuardFunction = null` (VE-DEBT-004), and at least one `ActionFunction = "Hrot.AI.Behaviors.CgfHsmNodes.StubIdle"`.
- At least one **global transition**.
- Bind at least one state's `OnEntryAction` (and/or `ActivityAction`) to `Hrot.AI.Behaviors.CgfHsmNodes.StubIdle`.
- Reasonable `Canvas` X/Y layout so it's readable (doesn't need to be pretty — visual polish is REVIEW-HS).

**Iterate against the real validator/serializer:** deserialize it, run `HsmValidator.Validate`, ensure **0 Errors** (Warnings are acceptable — note any), and confirm Serialize(Deserialize(json)) is byte-stable. Fix the JSON until all hold. Do NOT weaken the validator.

## Starter recipe (`HsmNewAssetService`)
Mirror BTree: add a `_starterRecipe` field, initialize it in the ctor from `MakeStarterDto()`, add it to `AvailableRecipes()` (`{ _emptyRecipe, _starterRecipe }`). `MakeStarterDto()` builds the minimal valid machine: a synthetic/root composite + ONE Simple state with `IsInitial = true` (and optionally `OnEntryAction = StubIdle`, but null is fine). It must deserialize to a valid one-initial-state machine that passes the validator with 0 Errors.

## Tests (`Hrot.Hsm.Editor.Tests`, new file)
1. **Showcase deserializes:** read the showcase JSON (embed the path via the same mechanism other asset tests use, or load relative to the assets root) → `HsmJsonServices.Deserialize` non-null.
2. **Showcase round-trips byte-stable:** `Serialize(Deserialize(json))` equals the canonical serialization (deserialize→serialize→deserialize→serialize stable).
3. **Showcase validates:** project to `HsmAsset` and `HsmValidator.Validate` returns **0 Error-severity** diagnostics (assert no `Severity == Error`).
4. **Showcase shape:** asserts it contains ≥1 parallel state with ≥2 regions, a history pseudo-state, a final state, ≥2 events, ≥1 global transition, and ≥1 state bound to `StubIdle` — and that **every transition GuardFunction is null** (VE-DEBT-004 guard).
5. **Starter recipe:** `AvailableRecipes()` includes a recipe that deserializes to a machine with exactly one initial Simple state and validates with 0 Errors.

> If embedding/reading the showcase file in the test is awkward, prefer round-tripping the JSON string (you can read it via the test project's content/copy-to-output, or include the canonical JSON as a test resource) — but the on-disk `HsmShowcase.hsm.json` under the assets root is the deliverable regardless.

## Verification (no regenerate env var)
```
dotnet build Hrot/Subsystems/AI/Hrot.Hsm.Editor/Hrot.Hsm.Editor.csproj
dotnet test  Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests
```
Must end `Failed: 0`, 0 build errors. Baseline before this batch: 432 passed. List pre-existing failures; confirm 0 new.

## Report → `.dev/ai-hsm-btree-vis-edit-2/reports/BATCH-HS-07-REPORT.md`
The showcase topology (states/regions/transitions/events/globals + what binds to StubIdle); confirmation all guards are null (VE-DEBT-004); the Starter recipe; how the showcase is loaded in tests; validator result (0 Errors, any Warnings); round-trip approach; before/after counts; anything not done. Do not commit.
