# BCP-BATCH-04: wire-drop auto-connect (honor PinIds) + sample channel actions + pin-coverage audit
From user re-test: wire-drop STILL doesn't auto-connect; ChannelCommand/FunctionCall often exec-only (largely data-limited).

## Onboarding
`.dev/.guides/DEV-GUIDE_claude.md`; `.dev/_DONE/blueprint-canvas-parity/DESIGN.md` (projection-only binds). codebase-memory MCP, not search_code. GizmoMap.Contracts 0.2.2; no Hrot.IG/DDS. Headless tests gate ImGui.

## Task 1 (P1) — wire-drop auto-connect: honor `props["PinIds"]`
**Root cause (confirmed):** NodeEdit `CanvasInput` (wire-drop) pre-generates pin GUIDs (`pinIds`), passes them as `AddNode` `InitialProperties["PinIds"]` (a `List<PinId>` for `entry.Inputs.Count + entry.Outputs.Count` pins, inputs-then-outputs order), and forms the auto-connect `AddLink` referencing `pinIds[pinIdx]`. But `BlueprintCommandSink.CreateAssetNode`/`ApplyInitialProperties` **ignore `PinIds`**, so the created node's pins (from `NodePinSchema`, different GUIDs) don't include `pinIds[pinIdx]` → `ApplyAddLink.FindPin` returns null → the link is **rejected** → no connection.
**Fix:** in `CreateAssetNode` (after the typed node is created, for the registry/fallback path AND ideally the Get/Set path), when `props` contains `"PinIds"` (a `List<PinId>` / `IReadOnlyList<PinId>`), populate `node.Pins` with the node's **canonical pins** (`NodePinSchema.GetCanonicalPins(node, _catalog.KindRegistry, _asset)` — use the SAME registry-based source `BlueprintNodeCatalog.DescriptorToEntry` uses so the count/order aligns: inputs (Direction=="In") in order, then outputs (Direction=="Out") in order) and assign the provided `PinIds` to those pins **in inputs-then-outputs order** (guard count mismatch: assign min(count); leave extra canonical pins with their generated GUIDs). This makes the node carry the link-referenced GUIDs so `ApplyAddLink` resolves and the wire connects. (Populating `node.Pins` for newly-created nodes is in-memory; note the save implication in the report — see DEBT below.)
**Verify ordering** against `CanvasInput` wire-drop (the pinIdx walks `entry.Inputs` then `entry.Outputs`) and `BlueprintNodeCatalog.DescriptorToEntry` (how it splits canonical pins into Inputs/Outputs). Align exactly.
**Tests (`Hrot.Blueprints.Tests`):** simulate the wire-drop command sequence — `AddNode(kind, pos, {PinIds:[...]})` then `AddLink(linkId, srcPin, pinIds[k])` as a Batch → assert the link is present in `_graph.Links` AND resolves (`FindPin(both ends)!=null`) AND connects the source pin to the new node (the new node owns the target pin). Test an exec wire-drop (e.g. EventEntry exec-out → new ChannelCommand exec-in) and a data wire-drop.

## Task 2 (P2) — fix SampleWiredDemo to use real channel actions
`SampleWiredDemo.bp.json` uses `CombatChannel`/`Fire`, which is NOT in `BuiltInChannelCommandCatalog` (it has `MoveTo`/`FollowRoute` on `…LocomotionChannel`, `AimAndFire` on `…WeaponChannel`). Change the two ChannelCommand nodes to real catalog actions (e.g. ChannelType `LocomotionChannel` ActionId `MoveTo`, and ChannelType `WeaponChannel` ActionId `AimAndFire`) so they resolve and show their (placeholder) param pin. Keep it valid + wired + positioned.

## Task 3 (P2) — pin-coverage audit doc
Produce `.dev/_DONE/blueprint-canvas-parity/reports/PIN-COVERAGE-AUDIT.md`: a table of EVERY node kind → does it project data pins now? If exec-only, classify WHY: **by-design** (Return, EventEntry, squad nodes — no node data pins per compiler), **config-needed** (FunctionCall needs TargetTypeId+MethodName; unconfigured = exec-only), **data-limited** (ChannelCommand params are a single placeholder type in `BuiltInChannelCommandCatalog` — richer params need catalog data enrichment, a separate runtime/content effort), or **deferred** (ReadRankedResult needs the UtilityDecisionDef result schema). Cite the source for each. This tells the user exactly where pins are and aren't, and what's a code gap vs a data gap.

## Success Criteria
- [ ] Dropping a wire on canvas + picking a node CONNECTS the wire to the new node (exec and data).
- [ ] SampleWiredDemo ChannelCommand nodes resolve to real catalog actions (show their param pin).
- [ ] Audit doc produced.
- [ ] Byte-stability of EXISTING `.bp.json` fixtures unchanged (the PinIds change only affects newly-created in-memory nodes; loaded assets still hydrate via projection). Compiler golden unchanged. Build 0 errors; touched projects no new warnings. GizmoMap.Contracts 0.2.2.
- [ ] Green: `Hrot.Blueprints.Tests` (no new failures beyond the 10 DEBT-006; flaky sub-80ns perf isolated), `Hrot.Editor.AiShared.Tests`, `Hrot.BTree.Editor.Tests`, `Hrot.Hsm.Editor.Tests`, `EditorSubsystemBoot`.
- [ ] Report at `.dev/_DONE/blueprint-canvas-parity/reports/BCP-BATCH-04-REPORT.md`.

## DEBT to log in the report
- **DEBT-BCP-005:** wire-dropped nodes now carry populated `node.Pins` in-memory (to honor PinIds). If/when the editor SAVES, these pins would persist (existing loaded assets are unaffected; only newly-authored nodes). Confirm the save path's behavior and whether persisted pins are acceptable / round-trip-safe before enabling save.
- **DEBT-BCP-006:** ChannelCommand params are a single placeholder type per action in `BuiltInChannelCommandCatalog`; rich per-arg pins need catalog data enrichment (runtime/content effort).

## Execution rules
- Verify the PinIds payload type + order against CanvasInput and DescriptorToEntry BEFORE coding. Run suites yourself; assert the link actually resolves + connects (not just "added"). Never fake a pass. Projection-only for LOADED assets stays mandatory (byte-stability test green).

## Report
Document: the PinIds honoring (payload type, order alignment); the sample fix; the audit; the two debts; actual test counts; build status; suggested commit message. No comprehension questions.
