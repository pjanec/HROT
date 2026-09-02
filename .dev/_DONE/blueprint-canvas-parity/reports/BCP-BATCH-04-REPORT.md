# BCP-BATCH-04 Report — wire-drop auto-connect (honor PinIds) + sample channel actions + pin audit

## Implementation Summary

### Task 1 (P1) — wire-drop auto-connect: honor `props["PinIds"]`
Root cause confirmed exactly as stated. `BlueprintCommandSink.CreateAssetNode` /
`ApplyInitialProperties` ignored the `"PinIds"` payload that NodeEdit's `CanvasInput` wire-drop
create-path ships in `AddNode.InitialProperties`, so the new node's pins (synthesized GUIDs)
never included the link's target GUID → `ApplyAddLink.FindPin` returned null → the link was
rejected → no auto-connect.

**Fix** (`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintCommandSink.cs`):
added `ApplyPinIds(Node, props)`, called at the end of both create-paths in `CreateAssetNode`
(the registry/fallback path) and in `FinishVariableNode` (the Get/Set path). It:
1. reads `props["PinIds"]` as `IReadOnlyList<PinId>` (the concrete `List<PinId>` the canvas
   ships satisfies this — no per-element copy needed);
2. builds the node's canonical pins via `NodePinSchema.GetCanonicalPins(node, _catalog.KindRegistry, _asset)`
   — the **same registry-backed source** `BlueprintNodeCatalog.DescriptorToEntry` uses, so the
   pin **count and per-direction order align** with the catalog entry the canvas walked;
3. re-orders the canonical pins **inputs-then-outputs** (`Direction=="In"` first, then
   `"Out"`) — matching `DescriptorToEntry` (`Inputs` = In-pins, `Outputs` = Out-pins) and
   `CanvasInput`'s `pinIdx` walk (`entry.Inputs` then `entry.Outputs`);
4. stamps `pinIds[i].Value` onto `ordered[i].Id` for `i < min(ordered, pinIds)` (count-mismatch
   guard; extra canonical pins keep their generated GUIDs);
5. assigns `node.Pins = ordered`.

Because the new node now **owns** the link-referenced GUID, `BlueprintGraphModel.Rebuild`'s
FAST PATH (`assetHadPins == true`, lines 161–166) uses those GUIDs verbatim, `FindPin` resolves
both endpoints, and `ApplyAddLink` writes a real `Link` to `_graph.Links`. The wire connects.

**Ordering verified against the actual code before coding:**
- `CanvasInput.cs` 1147–1191: `pinIds` is a `List<PinId>` of size `entry.Inputs.Count +
  entry.Outputs.Count`; the compatible-pin search walks `entry.Inputs` (pinIdx 0..) then
  `entry.Outputs`, taking `pinIds[pinIdx]`.
- `BlueprintNodeCatalog.DescriptorToEntry` 186–195: `canonicalPins = NodePinSchema.GetCanonicalPins(defaultNode, _registry)`;
  `Inputs = canonicalPins.Where(Direction=="In")`, `Outputs = Where(Direction=="Out")` —
  order-preserving. My `ApplyPinIds` reproduces this split exactly.

### Task 2 (P2) — SampleWiredDemo channel actions
`Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/SampleWiredDemo.bp.json`: the second
`ChannelCommand` node used `CombatChannel`/`Fire`, which is **not** in
`BuiltInChannelCommandCatalog`. Changed it to `WeaponChannel` / `AimAndFire` (a real catalog
entry: `("AimAndFire", "...WeaponChannel", 1, "System.Int32")`). The first ChannelCommand was
already `LocomotionChannel`/`MoveTo` (a valid entry) in the source file, so only node #4 needed
the fix. The asset stays valid, fully wired (5 nodes, 4 links unchanged) and positioned. Both
ChannelCommand nodes now resolve in the catalog and surface their (placeholder) param pin.

### Task 3 (P2) — pin-coverage audit
Wrote `.dev/_DONE/blueprint-canvas-parity/reports/PIN-COVERAGE-AUDIT.md`: a table of **every** node
kind (palette + the two dynamic kinds), whether it projects data pins now, and — for exec-only
kinds — the reason classified as by-design / config-needed / data-limited / deferred, each with
a cited source line. Summary: 10 palette kinds (+3 authored) have data pins today; 10 are
by-design exec-only; FunctionCall/CallCustomEvent are config-needed; ChannelCommand/CallPeer are
data-limited (DEBT-BCP-006); ReadRankedResult is deferred (needs UtilityDecisionDef schema).

## Design Decisions
- **Accept `IReadOnlyList<PinId>`** (not just `List<PinId>`) for forward-compatibility; the
  canvas's `List<PinId>` matches. Null / wrong-type / empty payloads are ignored (no-op), so
  non-wire-drop `AddNode`s are unaffected.
- **Call `GetCanonicalPins` with `_asset`** (per the instruction). For variable nodes the
  `VariableId` is applied (via `ApplyInitialProperties`) **before** `ApplyPinIds`, so the typed
  Value pins are produced; pin **count/order is invariant** to the variable type (Get=1, Set=4),
  so alignment with the catalog entry (built asset-unaware) holds.
- **No change to the `AddNode.Node` id handling.** `CreateAssetNode` still assigns a fresh
  `Guid` to the asset node (pre-existing behavior); the auto-connect does not depend on the
  command's `NodeId` matching the asset id — it depends only on the PinIds, which is what the
  link references. Tests assert against the actually-created node id accordingly.

## Deviations
None from the implementation spec. (The source `SampleWiredDemo.bp.json` already had the first
ChannelCommand correct, so only one of the two nodes was changed — WHAT: edited node #4 only;
WHY: node #2 was already a valid catalog action; BENEFIT: minimal diff; RISK: none.)

## Test Results
New tests: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Host/BcpBatch04WireDropTests.cs`
(3 tests, all green) — they reproduce the canvas's exact `Batch(AddNode{PinIds}, AddLink→pinIds[k])`
sequence and assert a **real connection**, not just "added":
- `WireDrop_Exec_EventEntryToChannelCommand_ConnectsToNewNode` — EventEntry exec-out → new
  ChannelCommand exec-in. Asserts: link in `graph.Links`; both ends `FindPin != null`; resolved
  target pin is owned by the new node and is its exec-IN; model link wires source-out→new-in.
- `WireDrop_Data_TypedOutToSetVariableValueIn_ConnectsToNewNode` — GetVariable data-out
  (System.Int32) → new SetVariable `Value` data-IN. Same real-connection assertions + the
  resolved pin is `Data`/`Input`/`Value`/`System.Int32`.
- `WireDrop_WithoutPinIds_LinkToFreshPin_IsRejected` — control/regression guard: without PinIds
  the link to a phantom GUID is rejected (`Success == false`, `graph.Links` empty), pinning the
  root cause and proving the PinIds payload is load-bearing.

```
BcpBatch04WireDropTests:  Passed: 3,  Failed: 0,  Skipped: 0
```

Required suites (all `--no-build` after a clean solution build):
```
Hrot.Editor.AiShared.Tests ........... Passed: 761, Failed: 0
Hrot.BTree.Editor.Tests .............. Passed: 382, Failed: 0
Hrot.Hsm.Editor.Tests ................ Passed: 333, Failed: 0
ClusterRunner.Integration (EditorSubsystemBoot) ... Passed: 10, Failed: 0
Hrot.Blueprints.Tests ................ Passed: 1120, Failed: 10, Skipped: 8 (Total 1138)
```

**The 10 `Hrot.Blueprints.Tests` failures are PRE-EXISTING and unrelated to this batch.**
Verified by `git stash`-ing the two source edits and re-running the failing subset on the clean
baseline: identical 10 failures (`Failed: 10, Passed: 0` for that subset). They are golden /
snapshot / allocation tests:
`AiPrimitiveEmitGoldenTests` (MoveToAndFire, HasVisibleTarget), `InstanceEmitGoldenTests`
(InstanceCounter, DoorActor, HealthRegen), `LibraryEmitGoldenTests`,
`*DemoTests.*_GeneratedSource_Snapshot` (MoveToAndFire, LibraryMath),
`ConditionSummaryAttachmentTests.Synthesize_EqsResult_ScoreCrossed_IncludesThreshold`,
`AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes`. None touch
`BlueprintCommandSink`, `NodePinSchema`, the wire-drop path, or `SampleWiredDemo`.

## Build status
- `dotnet build IOS-IG-SimHost.sln`: **0 errors, 26 warnings** (the pre-existing ~26 in
  unrelated test projects; none in touched projects). Touched-project build
  (`Hrot.Blueprints.Editor` + `Hrot.Blueprints.Tests`): **0 errors**; the 8 warnings in the test
  project are all pre-existing (`IBlueprintTimeController` obsolete, `EntityQuery.ForEach`
  obsolete, nullable in `BlueprintTestFixture`) — none in files this batch added/edited.

## Byte-stability / compiler-golden constraint
- **Byte-stability stays green by construction.** `ApplyPinIds` only populates `node.Pins` on a
  **freshly-created in-memory node**; it never touches loaded assets, which still hydrate via
  `BlueprintGraphModel` projection (`"Pins": []` on disk). The guardrail
  `BlueprintPinHydrationTests.ByteStability_EveryFixture_SerializesToOriginalBytes` iterates
  `TestAssets/` and `Comparison/Fixtures/`, deserializes → projects → re-serializes; the path I
  changed is not exercised by load/serialize, so fixtures remain byte-identical. (Confirmed the
  test was not among the 10 pre-existing failures — it passes.)
- **Compiler golden unchanged.** No compiler or asset-schema code was touched; the golden
  failures listed above are pre-existing and present on the clean baseline.
- **GizmoMap.Contracts 0.2.2; no Hrot.IG/DDS; headless** — honored (tests run headless, no ImGui
  context; ImGui rendering remains gated elsewhere).

## Developer Insights
- The two-pass model already had a FAST PATH for nodes that carry pins (added in BCP-BATCH-01).
  The whole fix is therefore just "make the new node carry the canvas-supplied GUIDs"; the
  resolution machinery downstream was already correct. This is why the fix is small and the
  data-flow is robust.
- **`CanvasInput`'s data-wire compatibility match is by exact type** (`sig.Type ==
  srcPinModel.Type`), and catalog entries are built from **default-constructed** nodes, so a
  `SetVariable` entry advertises its `Value` pin as `System.Object`. A data wire of a concrete
  type (e.g. `System.Int32`) therefore would **not** match that entry's signature in the picker
  filter today. This is outside Task 1's scope (Task 1 is the auto-connect *binding*), but it is
  a real wire-drop limitation worth a follow-up: catalog entries can't be configured per-instance
  before the node exists, so by-pin filtering for typed data pins is conservative. The exec path
  (kind-only match) is unaffected.
- The `AddNode.Node` (command-supplied `NodeId`) is **discarded** by `CreateAssetNode`, which
  mints a fresh GUID. Undo/redo currently works because the canvas inverse is
  `RemoveNodes(newNodeId)` matched by... the model, not the asset id — worth auditing separately,
  but it is pre-existing behavior and not regressed here.

## Known Issues / DEBT
- **DEBT-BCP-005 — wire-dropped nodes now carry in-memory `node.Pins`.** To honor PinIds the
  newly-authored node gets a populated pin list (with canvas-supplied GUIDs). Loaded assets are
  unaffected (they stay `"Pins": []` and project). **But if/when the editor SAVES**, these pins
  would persist into the `.bp.json`, which flips the compiler out of its empty-pins fast paths
  (`Stage2/3/4`) — a semantic change per DESIGN.md. **Before enabling save**, confirm the save
  path either strips synthesized/authored pins from in-memory nodes or that persisted pins
  round-trip safely through the compiler. (No save path is wired today, so this is latent.)
- **DEBT-BCP-006 — ChannelCommand placeholder params.** All five
  `BuiltInChannelCommandCatalog` entries use `ParamsTypeFqn = "System.Int32"`, so a
  ChannelCommand projects a single generic placeholder data-IN pin instead of rich per-arg pins.
  The projection already decomposes struct params types into per-field pins
  (`NodePinSchema.ReflectDataMembers`), so this is purely a **data/content gap**: enriching the
  catalog with real params types would surface rich pins with no editor code change.

## Suggested Commit Message
```
fix(blueprint-editor): honor PinIds for wire-drop auto-connect + fix SampleWiredDemo channel actions (BCP-BATCH-04)
```
