# BCP-BATCH-01-FIX: node-drop jump-back (all 3 perspectives) + wires-on-load (Blueprint)
Two confirmed bugs from user testing of BCP-BATCH-01.

## Onboarding
1. `.dev/.guides/DEV-GUIDE_claude.md`; `.dev/_DONE/blueprint-canvas-parity/DESIGN.md` (projection-only rule still applies).
Use codebase-memory MCP; not search_code. GizmoMap.Contracts stays 0.2.2; don't touch Hrot.IG/DDS. Headless tests must not call ImGui.

## Confirmed root cause — JUMP-BACK (BTree, HSM, AND Blueprint)
The canvas commits every node drop as **`GraphCommand.ChangeParentMultiple`** (`FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/CanvasInput.cs:761` — "Always use ChangeParentMultiple for uniformity (BPF-029)"), **never** `MoveNodes`. `ChangeParentMove(NodeId Node, NodeId? NewParent, int? NewRegion, Vector2 NewLocalPosition)` (`NodeEditor.Core/Commands/GraphCommand.cs:206`). **None** of the three sinks handle `ChangeParentMultiple`:
- `BTreeCommandSink.Apply` → falls to `default` → returns `(false,"Unsupported")`. Drop discarded.
- `HsmCommandSink.Apply` handles `ChangeParent` (singular) but `ApplyChangeParent` is `/* TODO */` (`HsmCommandSink.cs:112`); does NOT handle `ChangeParentMultiple`.
- `BlueprintCommandSink.Apply` → silent `default`. Drop discarded.
So the asset position is never updated → node renders at its stored (pre-drag) position → "jumps back".

### Fix (all three sinks): handle `GraphCommand.ChangeParentMultiple`
For each `ChangeParentMove`: set the asset node's position to `NewLocalPosition`, mark dirty, and notify the model **without a wholesale rebuild** (mirror the existing move path). Specifically:
- **BTreeCommandSink:** add `case GraphCommand.ChangeParentMultiple cpm: ApplyNodeMoves(cpm.Moves.Select(m => new NodeMove(m.Node, m.NewLocalPosition)).ToList()); break;` (reuse existing `ApplyNodeMoves`). (BTree node model reads `Position` live from the asset, so this is sufficient.)
- **HsmCommandSink:** add a `ChangeParentMultiple` case. For each move: set the state's position to `NewLocalPosition`; if `NewParent`/`NewRegion` differ from current, **implement the reparent/region move** (replace the `ApplyChangeParent` TODO with real hierarchy mutation using the HSM asset's existing region/child APIs — move the state into the target region; if full reparent is genuinely out of reach, at minimum persist position for every move and clearly TODO-note the region change, but DO make position stick). Mark dirty + notify.
- **BlueprintCommandSink:** add a `ChangeParentMultiple` case that updates `assetNode.EditorMetadata.X/Y = NewLocalPosition` for each move, marks dirty, and calls `_model.NotifyMoved(...)` (no rebuild). **Also make `BlueprintNodeModel.Position` read live** from `node.EditorMetadata` (delete the cached `_position`/`SetPosition` snapshot approach; mirror `BTreeNodeModel.Position => _node.Position`) so position can never go stale. Keep `MoveNodes` handling too (harmless).

**Tests (per perspective, headless):** apply a `ChangeParentMultiple` with a new position; assert `model.FindNode(id)!.Position == newPos` and the asset node's stored position updated; assert no wholesale rebuild for a pure move. Add to `BlueprintCommandSinkTests`, `BTreeCommandSinkTests`/equivalent, `HsmCommandSinkTests`/equivalent.

## Confirmed root cause — WIRES NOT SHOWN ON LOAD (Blueprint)
`BlueprintGraphModel.Rebuild` slow-path binds pin GUIDs **positionally** (`outPins[i] ↔ outLinks[i]`). When a node has more same-direction pins than incident links (e.g. a Branch/Sequence with only some outputs connected), trailing links' GUIDs are never assigned to any pin → `FindPin` fails → `WireRenderer` skips the wire (`CanvasRenderer` continues when an endpoint isn't in `pinPositions`). Test assets are mostly 1-link-per-pin so it passed.

### Fix: link-GUID-driven binding (guarantee every link resolves)
Rewrite the slow path so it is **driven by the incident links, not pin index**:
- Collect the node's **distinct** outgoing `FromPinId`s (fan-out shares one) and distinct incoming `ToPinId`s.
- Assign each distinct outgoing link GUID to an output pin (in pin declaration order); each distinct incoming link GUID to an input pin. Prefer assigning a link GUID to a pin whose **exec/data kind** is consistent if inferable (exec links connect exec pins) — but the overriding invariant is: **every link endpoint GUID must land on some pin of the correct direction** so the wire resolves.
- Any pin with no assigned link GUID gets `IdGenerator.Deterministic($"pin:{nodeId:N}:{name}:{dir}")`.
- Keep the fast path (asset `Pins` non-empty → authoritative GUIDs) unchanged.

**Tests:** add a fixture asset (or build in-memory) with a Branch (or Sequence) node where only SOME outputs are connected; assert **every** `graph.Links` entry resolves (`FindPin(FromPinId)!=null && FindPin(ToPinId)!=null`) after projection. Keep the MoveToAndFire tests green. Keep the byte-stability test green (still projection-only — no asset writes).

## Success Criteria
- [ ] Nodes stay where dropped in BTree, HSM, and Blueprint (drop persists via ChangeParentMultiple).
- [ ] Loaded Blueprint assets render their wires (all links resolve), including partial-connection multi-output nodes.
- [ ] Byte-stability test still green; compiler golden suite unchanged.
- [ ] `dotnet build IOS-IG-SimHost.sln` 0/0; GizmoMap.Contracts 0.2.2.
- [ ] Green: `Hrot.Blueprints.Tests` (no new failures beyond the 10 DEBT-006; the sub-80ns `WhenNodePerfTests` is flaky under load — re-run isolated if needed), `Hrot.Editor.AiShared.Tests`, `Hrot.BTree.Editor.Tests`, `Hrot.Hsm.Editor.Tests`, `EditorSubsystemBoot` filter.
- [ ] Report at `.dev/_DONE/blueprint-canvas-parity/reports/BCP-BATCH-01-FIX-REPORT.md`.

## Execution rules
- Run all suites yourself; assert real values (post-drop position equals NewLocalPosition; every loaded link resolves); never fake a pass.
- Projection-only stays mandatory: no `Pin` schema field, no writes to `.bp.json`, no `BlueprintJsonServices` change.
- Reuse existing move/rebuild helpers; don't duplicate.

## Report
Document: the ChangeParentMultiple handlers per sink (and how HSM reparent/region was handled or deferred), the live-Position change, the new link-GUID-driven binding algorithm, actual test counts, build 0/0, byte-stability + compiler-golden status, suggested commit message. No comprehension questions.
