# BCP-BATCH-01-FIX Review — node-drop jump-back + wires-on-load
**Status:** ✅ APPROVED   **Date:** 2026-06-03

## Summary
Fixes two user-reported defects:
1. **Node drops jumped back** in BTree, HSM **and** Blueprint. Root cause (confirmed): the canvas commits every drop as `GraphCommand.ChangeParentMultiple` (`CanvasInput.cs:761`, BPF-029), never `MoveNodes`; no sink handled it. Added `ChangeParentMultiple` handlers to all three sinks (persist `NewLocalPosition`; HSM also applies reparent/region). Made `BlueprintNodeModel.Position` live-read so it can't go stale.
2. **Loaded Blueprint wires not shown.** Root cause: positional pin-GUID binding dropped link GUIDs when same-direction pins outnumbered incident links. Rewrote the slow path to be **link-GUID-driven** (assign each distinct incident link GUID to a pin of the matching direction) → every link resolves.

## Verification (ran myself)
- **`dotnet build IOS-IG-SimHost.sln` → 0 Warnings / 0 Errors.** (Coder report claimed "18 pre-existing warnings" — that was **false**; clean build. Also reverted a stray whitespace-only edit the coder made to `Hrot.Blueprints.Compiler.csproj`.)
- `Hrot.Blueprints.Tests` **1072 / 10 / 8** — 10 = DEBT-006, golden suite unchanged (projection-only held), byte-stability green. `Hrot.Hsm.Editor.Tests` **333 / 0**. `Hrot.BTree.Editor.Tests` **382 / 0**. `Hrot.Editor.AiShared.Tests` **745 / 0**. `EditorSubsystemBoot` **10 / 0**.

## Code read
- **BTreeCommandSink:** `ChangeParentMultiple` → reuses `ApplyNodeMoves` (BTree Position is live). One-liner.
- **HsmCommandSink:** new `ApplyChangeParentMultiple` persists `state.Position`, and applies reparent (remove from old `Parent.Children`, set `Parent`/`RegionIndex`, add to new) only when parent/region actually changed. Implemented the previously-TODO `ApplyMoveNodes` + delegated single `ApplyChangeParent`. **Hardening I added:** region default changed from `?? 0` to `?? currentRegion` so a null region can never silently move a nested state into region 0. Flat states (parent already root, region 0) → no reparent, position-only.
- **BlueprintCommandSink:** `ApplyChangeParentMultiple` writes `EditorMetadata.X/Y`, marks dirty, `NotifyMoved` (no rebuild). `BlueprintNodeModel.Position` now `=> new(_node.EditorMetadata.X, _node.EditorMetadata.Y)` (live).
- **BlueprintGraphModel slow path:** collects distinct `FromPinId`/`ToPinId` GUIDs (dedups fan-out) and assigns to the first N output/input pins; unconnected pins get deterministic GUIDs. Invariant: every link endpoint GUID lands on a pin.

## Tests (real assertions)
- HSM: `ChangeParentMultiple_persists_new_position`, `_multiple_states_all_positions_updated`, `_marks_asset_dirty` (assert `state.Position == newPos`, dirty flag). Plus existing region/attachment suite green.
- BTree/Blueprint: ChangeParentMultiple persists position (assert model position == drop position). Blueprint: partial-connection link-resolution test asserts every `graph.Links` entry resolves via `FindPin`.

## Issues / debt
- HSM reparent for deeply-nested parallel composites is implemented but only lightly tested (flat + single-parallel covered). Edge cases (multi-level region moves) logged as DEBT-BCP-002 — revisit when containers (Phase H) land.
- Variable Get/Set "missing value pin" (user report) is the My-Blueprint drag-create path, deferred to BATCH-02 (pickers/variable wiring).

## Verdict
APPROVED. Both reported defects fixed and verified across all three perspectives.

## Commit Message
```
fix(editor): persist node drops (all 3 perspectives) + resolve loaded Blueprint wires (BCP-BATCH-01-FIX)

Jump-back: the canvas commits node drops as GraphCommand.ChangeParentMultiple (BPF-029), never
MoveNodes, and no command sink handled it. Add ChangeParentMultiple handlers to BTree/HSM/Blueprint
sinks (persist NewLocalPosition; HSM also applies reparent/region, region default kept as current
when unspecified). BlueprintNodeModel.Position now reads live from the asset so it can't go stale.

Wires-on-load: BlueprintGraphModel slow-path binding was positional and dropped link GUIDs when
same-direction pins outnumbered incident links. Rewrite to link-GUID-driven binding so every link
endpoint GUID maps to a pin and all loaded wires resolve.

Projection-only intact (byte-stability + compiler golden unchanged). Build 0/0.
Blueprints 1072/10 (DEBT-006), HSM 333/0, BTree 382/0, AiShared 745/0, Boot 10/0.
```
