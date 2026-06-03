# BCP-BATCH-02-FIX3: unify wire-drop picker + auto-connect + duplicate-variable-name guard
Small, precisely-diagnosed fixes from user re-test.

## Onboarding
`.dev/.guides/DEV-GUIDE_claude.md`; `.dev/blueprint-canvas-parity/DESIGN.md` (projection-only binds). codebase-memory MCP, not search_code. GizmoMap.Contracts 0.2.2; no Hrot.IG/DDS. Headless tests gate ImGui.

## Task 1 (P1, unified root) — wire-drop picker shows only 3 kinds AND new node doesn't auto-connect
**Root cause (confirmed):** `Hrot.Blueprints.Editor/Host/BlueprintNodeCatalog.cs` `DescriptorToEntry` (lines ~159-198) builds `NodeCatalogEntry.Inputs`/`Outputs` (the `PinSignature` lists) from `defaultNode.Pins` (lines ~177-181). The 24 palette kinds added in FIX2 create nodes with **empty `Pins`** (pins are projected by `NodePinSchema` at render time), so their entries have no pin signatures. Consequences:
- `QueryForPinContext` (lines ~110-127) filters by `entry.Inputs`/`Outputs` → only the 3 hand-authored When/EQS kinds match → wire-drop picker shows just 3 (TAB's `nodes.all` doesn't filter, so it shows all 27).
- NodeEdit's wire-drop auto-connect (`CanvasInput.cs:1163-1209`) searches `entry.Inputs`/`Outputs` for a pin compatible with the dragged pin; with empty signatures it finds none → forms no link → "doesn't connect".

**Fix:** in `DescriptorToEntry`, when `defaultNode.Pins` is empty, derive the pin list via `NodePinSchema.GetCanonicalPins(defaultNode, _kindRegistry)` (the catalog already has the registry; asset may be null — exec/data kind + name is enough for signatures) and build `Inputs`/`Outputs` `PinSignature`s from THAT. Keep using `defaultNode.Pins` when non-empty (When/EQS hand-authored). This single change makes `QueryForPinContext` return all compatible kinds (wire-drop picker == TAB picker, filtered by pin compatibility) AND gives the wire-drop flow a compatible pin so it forms the link.

**Why auto-connect then works (no further change needed):** the wire-drop link targets a fresh `compatiblePinId`; the new node is created pinless; on rebuild the two-pass slow-path in `BlueprintGraphModel` binds the new node's first compatible-direction canonical pin to the link's pin GUID → the link resolves and the wire is drawn connected. Verify this end-to-end in a test.

**Tests (`Hrot.Blueprints.Tests`):** `QueryForPinContext` for an exec-output source returns the flow/event/channel kinds (not just 3) — assert count > 3 and that e.g. Branch/Sequence/ChannelCommand are present with a compatible exec-input; for a data-output of type X returns kinds with a compatible data-input. Integration-style: simulate add-node-with-link (AddNode pinless + AddLink to a fresh pin id on the new node) → after `Rebuild`, the link resolves (`FindPin(from)!=null && FindPin(to)!=null`) and connects to the new node.

## Task 2 (P2) — variable creation silently appends a numeric suffix on duplicate names
**Current:** `BlueprintDocumentFactory.CreateVariable` / the `+` modal accept a duplicate name and auto-rename (e.g. "Speed" → "Speed1"). Unacceptable — the user must be warned up front.
**Fix:** in `VariableCreateModal`, validate the entered name against existing `BlueprintAsset.Variables` (case-insensitive); when it collides, show an inline warning ("A variable named 'X' already exists") and **disable the Confirm button** (do not auto-rename). `CreateVariable(name, type)` should **reject** a duplicate (return false / throw a clear result) rather than silently suffixing. Also reject empty/whitespace names.
**Tests:** `CreateVariable` with an existing name → rejected, no new `VariableDecl` added, original unchanged; with a unique name → added. (Modal UI gated; test the validation/create logic headlessly.)

## Success Criteria
- [ ] Wire-drop picker offers the full compatible node set (matches TAB, pin-filtered); dropping an exec/data wire on canvas and picking a node **auto-connects** the wire to the new node.
- [ ] Duplicate variable name is warned up front and rejected (no silent numeric suffix); empty name rejected.
- [ ] Byte-stability + compiler golden unchanged. Build 0 errors / 0 warnings (baseline is 0; any warnings are yours). GizmoMap.Contracts 0.2.2.
- [ ] Green: `Hrot.Blueprints.Tests` (no new failures beyond the 10 DEBT-006; flaky sub-80ns perf re-run isolated), `Hrot.Editor.AiShared.Tests`, `Hrot.BTree.Editor.Tests`, `Hrot.Hsm.Editor.Tests`, `EditorSubsystemBoot`.
- [ ] Report at `.dev/blueprint-canvas-parity/reports/BCP-BATCH-02-FIX3-REPORT.md`.

## Execution rules
- Task 1 first. Run suites yourself; assert real values (pin-context kind count + specific kinds + compatible pin; link resolves after add+connect; duplicate rejected). Never fake a pass.
- Reuse `NodePinSchema`, the existing `PinToSignature` helper, `BlueprintGraphModel` two-pass binding, `VariableCreateModal`/`CreateVariable`. Verify signatures first. Projection-only stays mandatory (no `Pin` schema field, no `.bp.json`/`BlueprintJsonServices` change).

## Report
Document: the `DescriptorToEntry` signature-derivation change + how it unifies the picker and enables auto-connect (incl. the slow-path binding chain); the duplicate-name validation; actual test counts; build 0/0; byte-stability + golden status; suggested commit message. No comprehension questions.
