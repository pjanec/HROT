# BATCH-06 — Showcase `.btree.json` + Starter recipe (BTree)

**Task:** TASK-BT-06 (`.dev/ai-hsm-btree-vis-edit-2/TASK-DETAIL.md#task-bt-06--showcase-btree--starter-recipe`)
**Phase:** A · **One objective** (two coupled deliverables: a showcase asset + a Starter recipe).
**Decision D-03** (`.dev/ai-hsm-btree-vis-edit-2/DECISIONS.md`): the Starter recipe is an **in-code** entry in `AvailableRecipes()` (no on-disk recipe discovery).

## 🔒 Working agreement (MANDATORY)
Same as prior batches: one task; **NO cheating** (no excluding files / suppressing diagnostics / weakening tests); **finish without asking** until build clean + `Failed: 0`; tests assert real values; litter-free; report = diffs.

## 📋 Onboarding
- Design: `docs/blueprints/BTree_HSM_Editor_State_And_Forward_Plan.md` §4/§5 (EB-A); host `docs/blueprints/BTree_Editor_NodeEditor_Host_Design.md` §2 (the OrcGuard-style picture).
- **Schema template (copy its exact shape):** `Hrot/Subsystems/Hrot.AI.Behaviors/Trees/SampleScout.btree.json`. Use the same `$meta`, field names, `EditorMetadata` (X/Y/Comment/Collapsed/Color), `Canvas`, `Pills`, `SubtreeSyncBindings`, `Suppressions`, `Blackboard` blocks.
- Serializer: `Hrot.AiEditor.Persistence.BTree.BTreeJsonServices` (`Serialize`/`Deserialize`). Mapper: `BehaviorTreeAssetMapper` (`FromDto`/`ToDto`).
- Report → `.dev/ai-hsm-btree-vis-edit-2/reports/BATCH-06-REPORT.md`.

## 🎯 Deliverable A — Showcase asset
Author a NEW `<BTree assets root>/CombatShowcase.btree.json` that, when opened, exercises **every** built BTree feature so the canvas is no longer minimalistic. Use `BlackboardTypeName = "Fdp.Toolkit.Behavior.Components.BrainBlackboard"` and `ContextTypeName = "Fdp.Toolkit.Behavior.BTreeContext"` (same as SampleScout). Put it at the path the BTree assets root resolves to (use the `AssetRoots`/path the SampleScout sample lives under — i.e. alongside `SampleScout.btree.json`; do NOT invent a new folder).

Required topology (a valid tree; assign fresh, distinct `VisualId` guids; lay out with sane X/Y so it reads top-down):
- **Root** → **ObserverSelector** (so the eye glyph + OBSERVES badge show).
- Under the ObserverSelector:
  - a **Condition** leaf as a guard child — `MethodFqn` = a REAL `[BTreeCondition]` method (e.g. `Condition_TargetAliveAndVisible` in `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/CgfNodes.cs`, or `Condition_HasTarget` in `EqsCombatNodes.cs`). Read the file and build the FQN exactly as `ActionSchemaExporter` does (`{DeclaringType.FullName}.{MethodName}`).
  - a **Sequence** containing:
    - an **Action** leaf with **two stacked decorator pills** on it — a **Repeater** (`IntParam` e.g. 3) and a **Cooldown** (`FloatParam` e.g. 2.0). `MethodFqn` = a REAL `[BTreeAction]` (e.g. `Action_FireAtTarget` in `CgfNodes.cs` or `Action_HoldPosition` in `EqsCombatNodes.cs`).
    - a **Wait** leaf (`Duration` e.g. 1.5).
    - a **Subtree** leaf referencing the existing **`SampleScout`** tree (so it resolves to a real asset, not a red box).
- Pills go in the `Pills` array (VisualId, HostNodeVisualId = the Action's VisualId, DecoratorType, Int/FloatParam, StackIndex 0 and 1).

Add a couple of `Comment`s in `EditorMetadata` for realism. The file MUST round-trip byte-stable through `BTreeJsonServices`.

## 🎯 Deliverable B — Starter recipe (D-03)
In `Hrot/Subsystems/AI/Hrot.BTree.Editor/BTreeNewAssetService.cs`, add an in-code **"Starter"** recipe to `AvailableRecipes()` (alongside the existing "Empty"): a minimal **valid** tree = a **Root** node with one **empty Sequence** child (so a new-from-Starter tree opens with a root + a composite to build under, not a blank canvas). Mint its DTO in code (mirror `MakeEmptyDto()` → `MakeStarterDto()`), wrap in a `BTreeEditableAssetAdapter`. `CreateNew(starterRecipe, name, relPath)` must clone it (the existing recipe-clone path already does serialize→deserialize→new AssetId).

## 🧪 Tests (new file `Persistence/BTreeShowcaseAndStarterTests.cs` in `Hrot.BTree.Editor.Tests`)
Load the **shipped** showcase file (resolve its real committed path — e.g. via `AssetRoots` or by walking up from the test assembly to the repo; do NOT inline a copy of the JSON in the test) and assert:
- `Showcase_Deserializes`: `BTreeJsonServices.Deserialize(text)` is non-null.
- `Showcase_RoundTripByteStable`: `Serialize(Deserialize(text))` equals `Serialize(Deserialize(that))` (serialize→deserialize→serialize is idempotent). (Compare the normalized serialized strings.)
- `Showcase_Projects_HasAllFeatures`: `BehaviorTreeAssetMapper.FromDto(...)` (or the contributor) yields a model containing: an ObserverSelector node, a Condition leaf with non-empty `MethodFqn`, an Action leaf with non-empty `MethodFqn` carrying **2 pills** (Repeater + Cooldown), a Wait leaf, and a Subtree leaf whose reference is `SampleScout`.
- `Starter_InAvailableRecipes`: `new BTreeNewAssetService(<tempDir>).AvailableRecipes()` contains an entry named "Starter".
- `Starter_CreateNew_YieldsRootPlusSequence`: `CreateNew(starter, "MyNew", "")` into a temp dir → the written `.btree.json` deserializes to a tree with a Root and one (empty) Sequence child.

(Use the `BTreeNewAssetService(string assetRootPath)` ctor with a temp directory for the recipe tests so they don't touch the real assets root.)

## ✅ Success criteria
- [ ] `dotnet build IOS-IG-SimHost.sln` — 0 errors, 0 new warnings in touched projects.
- [ ] `dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests` — **Failed: 0** (incl. the new tests).
- [ ] `CombatShowcase.btree.json` exists under the BTree assets root, round-trips byte-stable, projects with all features, and references `SampleScout` for its subtree.
- [ ] "Starter" recipe present and produces a Root+Sequence tree.
- [ ] Action/Condition `MethodFqn`s are REAL methods from the named source files (so the editor can bind them).
- [ ] Report written. Note in the report: runtime FQN/registration resolution is confirmed at REVIEW-BT (editor load), not in these structural tests.

## Notes / pitfalls
- Round-trip byte-stability is the key gate — match SampleScout's exact serializer output conventions (the saves are pretty-printed via `JsonAestheticFormatter`; you only need serializer round-trip stability through `BTreeJsonServices`, not the pretty formatter, for the test).
- Do NOT modify `SampleScout.btree.json`.
- If a feature can't be represented because a DTO/field differs from the SampleScout schema, follow the real schema and note the deviation — do not invent fields.
