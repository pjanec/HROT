# DEC-06 — Pill undo (add/remove) + nested-decorator safety (validation + prevention)

**Workstream:** DEC ([../DEC-PLAN.md](../DEC-PLAN.md)). **Layer:** NodeEditor.UI + Hrot.BTree.Editor host. **Depends:** DEC-02/03/03b/04. **Size: large (4 cohesive parts).**

Two user reports: (1) Ctrl+Z doesn't undo pill add/remove; (2) a nested-Repeater tree (two Repeater pills) crashed codegen at boot.

## Background / what already exists (do NOT redo)
- **L1 boot resilience ALREADY EXISTS:** `Hrot/Subsystems/Hrot.Editor/AiHotReloadCoordinator.cs:259-276` wraps each registrar `Invoke` in try/catch (PU-402), logging + skipping a failing tree. The boot "crash" is a debugger first-chance break on the caught `BehaviorTreeBuildException`. **Confirm** this catch covers the BTree build-exception path (it's a generic `catch (Exception)`, so it does); optionally add/extend a test asserting a registrar that throws is skipped and the others still register. No new resilience code unless you find a genuinely uncaught path.
- **Undo infra:** `view.Execute(fwd, inv, label)` → `Undo.ApplyAndRecord` records to the undo stack. `view.Commands.Apply(cmd)` and `_sink.Apply(cmd)` and direct asset mutation do NOT record. The fix is to route structural pill ops through `view.Execute`.
- **Inspector FIELD edits are OUT OF SCOPE.** `BTreeFacetMapper.ApplyFacet` mutates the asset directly for BOTH nodes and pills (a property of the shared inspector framework). Making field edits undoable is a separate, framework-wide change — do NOT attempt it here. This batch covers structural add/remove undo only.

## Part 1 — Undo for right-click "Add Decorator"
`BTreeNodeContextMenuProvider.MakeItem` currently calls `_sink.Apply(new GraphCommand.AddAttachment(...))` → not undoable. Route it through `view.Execute(addAttachment, new GraphCommand.RemoveAttachments(new[]{ newId }), "Add Decorator")` instead.
- The provider is constructed in `BTreeDocumentFactory` at line ~136 (`SetNodeContextMenuProvider(commandSink, graphModel)`) **before** the `GraphView` is created (line ~139). Wire a recorder AFTER the view exists: give the provider a settable `Action<GraphCommand,GraphCommand,string>` (or `Func<...,GraphCommandResult>`) recorder = `(f,i,l) => view.Execute(f,i,l)`, set via a new `hostServices.SetNodeContextMenuRecorder(view)` call placed right after `var view = new GraphView(...)`. If the recorder is null (defensive), fall back to the existing `_sink.Apply`.
- Keep generating the same `AttachmentId` for both forward and inverse.

## Part 2 — Undo for Delete (pills, and confirm nodes)
The active Delete handler is `CanvasInput.DeleteSelected` (`CanvasInput.cs:1237`), invoked from `HandleIdle` on Delete/Backspace; it builds a `Batch` and calls `view.Commands.Apply(batch)` → **NOT undoable** (this shadows the fully-undoable `EditCommands.DeleteSelected` at `EditCommands.cs:58`, which already builds inverses incl. attachments from DEC-04).
- Fix: make the Del-key path undoable. Preferred: **consolidate** — have `CanvasInput`'s Delete invoke the same undoable routine as `EditCommands.DeleteSelected` (which already handles links/nodes/comments/reroutes/attachments with correct inverses and `view.Execute`). Extract that routine to a shared internal static method (e.g. `EditCommands.DeleteSelected` made `internal static`, or a new shared helper) and call it from both places; delete the now-redundant non-undoable `CanvasInput.DeleteSelected` body.
- **Verify after:** deleting a NODE and a pill both undo correctly (this is a shared-path change — confirm no double-delete and that existing node-delete tests pass).

## Part 3 — L2 validation: detect nested Repeater / nested Parallel
The kernel (`TreeValidator.DetectNestedRepeater` / `DetectNestedParallel`) treats a Repeater inside another Repeater's subtree (transitive), or Parallel inside Parallel, as a hard error (`TreeCompiler.cs:99` throws). Mirror this in the editor's `BTreeValidator.Validate(BehaviorTreeAsset)` so it surfaces BEFORE save:
- Add `BTreeDiagnosticCode.NestedRepeater` and `BTreeDiagnosticCode.NestedParallel` (`Validation/BTreeDiagnostic.cs`).
- Walk the asset tree from the root, tracking `insideRepeater` / `insideParallel`. At each node, the node's **decorator pills are wrappers above it** (a Repeater pill makes the node-and-its-subtree "inside Repeater"; two Repeater pills on one node nest by themselves). A `Parallel` *node* sets `insideParallel`. Emit an Error diagnostic (severity Error) pointing at the offending node/pill when a Repeater is found while already inside a Repeater (count multiple Repeater pills on one host as nesting), and likewise for Parallel.
- Add a test: an asset with two Repeater pills on one node → one `NestedRepeater` error; a Repeater pill on a node under a Repeater-pilled ancestor → error; a single Repeater → no error; Parallel-in-Parallel → `NestedParallel`.

## Part 4 — L3 prevention (common case): block stacking a 2nd Repeater on one node
In `BTreeNodeContextMenuProvider.GetItemsFor`, when building the "Add Decorator" children, set the **Repeater** child's `Enabled = false` if the host node already has a Repeater pill (`_model.GetAttachmentsForNode(node)` → check, or query the asset's pills for that host with `DecoratorType == Repeater`). (Same-node stacking is the case the user hit; cross-node nesting is covered by L2 + L1.) Defense-in-depth: in `BTreeCommandSink.ApplyAddPill`, refuse (no-op) to add a Repeater pill when the host already has one. Keep other decorators unrestricted.

## Constraints
- Parts 1/2 are NodeEditor.UI + host; Part 2 is a shared-delete consolidation — be careful it doesn't change behaviour for nodes/links/comments/reroutes (reuse the existing `EditCommands` logic verbatim). Additive elsewhere.
- No `.btree.json`/codegen changes. If a Parallel `CS7036` appears when building, run `dotnet build-server shutdown` then rebuild (stale analyzer cache — NOT your bug).

## Verification (run + paste RAW output)
1. `dotnet build` NodeEditor.Core, NodeEditor.UI, `Hrot.BTree.Editor`, `Hrot.Hsm.Editor`, `Hrot.Blueprints.Editor` → 0 errors.
2. `dotnet test` NodeEditor.Core.Tests, NodeEditor.UI.Tests, `Hrot.BTree.Editor.Tests` → counts; no new failures vs baseline (Core 195/0, UI 78/0, BTree.Editor 548/0).
3. New tests: BTreeValidator nested-Repeater/Parallel; ApplyAddPill rejects 2nd Repeater; (if feasible) the AiHotReloadCoordinator skip-on-throw test.

## Report back
Per part: what changed + how undo now records (and confirmation node-delete still undoes); how nested detection walks pills; raw build + test output; explicit confirmation that inspector field-edit undo was intentionally left out of scope. **Do NOT commit** — lead reviews & commits.
