# Architect question #22 — Unifying the two undo stacks (property edits vs. structural edits)

> **Scope.** Blueprint editor undo/redo only. Tracker item **BP-11** (🔴, `RW-L`) —
> *"No inspector or drawer edit is undoable."* Detail: `Blueprint_Issues_Detail.md#BP-11`.
> This is the one item in the cheap/headless batch that is **not** a mirror-an-existing-pattern fix,
> so per the engine-rules gate it gets an architect pass before any code.

**Symptom.** A designer edits a value in the Details panel / node drawer, presses **Ctrl+Z**, and nothing
happens. The edit is not reversible by any means.

---

## Ground truth (verified against code 2026-08-04)

There are **three** tiers, not two. This corrects the earlier audit note, which said the two
`SharedNodeDrawers` downcasts were "written to be undoable" — they are not; they call
`NotifyStructureChanged`, which has nothing to do with undo.

| # | Path | Records? | Reachable by Ctrl+Z? |
|---|---|---|---|
| 1 | Canvas structural edits → `view.Execute(fwd, inv, label)` → `UndoStack.ApplyAndRecord` | ✅ yes | ✅ **yes** — `GraphView.UndoLast()` → `Undo.Undo()` |
| 2 | `BlueprintCommandSink` property edits → `EditService.RecordPropertyEdit` → `CommandHistory.Execute` | ✅ yes | ❌ **no** — `CommandHistory.Undo` has **zero non-test callers** |
| 3 | Drawer / inspector edits → `IEditService.MarkDirty` only | ❌ **no** | ❌ no |

**Supporting facts.**

| Fact | Evidence |
|---|---|
| `IEditService` exposes exactly one method, `MarkDirty` | `NodeDrawers/IEditService.cs` — docstring: *"Stub for M5; full implementation deferred."* |
| `RecordPropertyEdit` + `NotifyStructureChanged` live only on the **concrete** `EditService` | `NodeDrawers/EditService.cs:45,64` |
| `BlueprintCommandSink` holds the concrete type, so it calls them directly | `Host/BlueprintCommandSink.cs:39` — `private readonly EditService _editService;` — 6 call sites (728, 828, 854, 953, 982, 1017) |
| Drawers hold the **interface**, so they downcast — but only for structure-notify | `SharedNodeDrawers.cs:248,414` — `(_editService as EditService)?.NotifyStructureChanged(...)`. **No drawer ever calls `RecordPropertyEdit`.** |
| ~9 `MarkDirty`-only edit sites across 5 drawer files | `ComponentNodeDrawers` 2 · `PlayMontageChainNodeDrawer` 3 · `SharedNodeDrawers` 2 · `FunctionCallNodeDrawer` 1 · `LiteralNodeDrawer` 1 |
| `EditServiceContext` carries `History`, `MarkDirty`, `OnStructureChanged` — **no `GraphView`** | `EditService.cs:92-118` |
| `CommandHistory` is a bounded 64-entry ring; no leak | `GraphEditor/CommandHistory.cs` |
| ⚠ `CommandHistory.Execute()` **performs the mutation** (`cmd.Execute()` → `apply()`) | `CommandHistory.cs:24`, `PropertyEditCommand.Execute` — **load-bearing; cannot simply be deleted** |

> **The trap.** Tier 2 *looks* correct in isolation and has passing unit tests
> (`EditServiceTests`, `CommandHistoryTests`, `GraphCommandsUndoTests` all drive `history.Undo()`
> directly). The tests prove the stack works; nothing proves it is *connected*. Same failure mode as
> BP-29, where tests passed the registry explicitly and production never did.

---

## Q22-A — Which stack survives?

- **A1 — collapse onto NodeEdit's `UndoStack`; retire `CommandHistory`.** `RecordPropertyEdit` re-points at
  `view.Execute(fwd, inv, label)`. One stack, so undo ordering across a mixed edit sequence
  (move a node, change a field, delete a link) is automatically correct.
  *Reuse:* `UndoStack.ApplyAndRecord` already has the forward/inverse contract `PropertyEditCommand` wants;
  Ctrl+Z already reaches it. *Build:* thread a `GraphView` to `EditService` (see Q22-C).
- **A2 — keep `CommandHistory` as the blueprint-level stack; route Ctrl+Z to it.** Wrong direction, listed for
  completeness: it would orphan every existing structural edit, which is the half that currently works.
- **A3 — two stacks + a coordinator that interleaves them.** Correct ordering needs a global sequence number
  across both, i.e. a third structure. Cost of a merge-undo layer for zero capability gain.

**Claude's lean: A1.** A single stack is the only option where mixed-edit undo ordering is right by
construction. A3's coordinator is strictly more machinery than A1's deletion.

**Open sub-question for the architect:** is `UndoStack` intended to stay a *NodeEdit-core* (graph-shaped)
concept? A blueprint property edit is not a graph mutation, and A1 puts non-graph commands on it. If that
violates a NodeEdit layering rule, A3 becomes the fallback and this question changes shape.

## Q22-B — What does `IEditService` expose after the fix?

- **B1 — promote `RecordPropertyEdit` + `NotifyStructureChanged` onto `IEditService`.** Deletes both
  downcasts. *Cost:* every test double must implement them (they can be no-ops).
- **B2 — keep `IEditService` minimal; formalise the downcast as a capability interface**
  (`IUndoableEditService`, `as`-tested). Preserves "drawers work headless with a stub", but keeps the
  silent-no-op-on-stub failure mode that hid this bug.
- **B3 — retire `IEditService` for a richer `INodeEditContext`.** Larger blast radius; only worth it if
  drawers are due other context (selection, focus) anyway.

**Claude's lean: B1.** The interface's own docstring calls it a deferred stub — this is the deferral coming
due. B2 preserves exactly the ambiguity that let tier-3 edits silently skip undo.

## Q22-C — How does `EditService` reach the `GraphView`?

- **C1 — add `GraphView` to `EditServiceContext`.** Smallest diff; context is already swapped per active
  document, which is the right lifetime. *Cost:* `Hrot.Blueprints.Editor` takes a direct NodeEdit `GraphView`
  dependency at the service layer.
- **C2 — inject a transport delegate**, `Action<Action, Action, string> recordSink`, wired by the composition
  root. Keeps `EditService` ignorant of NodeEdit — mirrors how `OnStructureChanged` already works
  (`EditServiceContext` deliberately holds a delegate, not a canvas reference).
- **C3 — document-level mediator** owning both stacks. Most indirection; justified only if other subsystems
  will need the same seam.

**Claude's lean: C2.** There is already a precedent in the same class: `OnStructureChanged` is a delegate
*specifically* so the service never references the canvas, and the docstrings say so twice. C1 contradicts
a decision this file already made deliberately.

## Q22-D — Migration order, and what happens to the 6 sink call sites?

- **D1 — big-bang:** promote the interface, re-point the implementation, convert all ~9 drawer sites, delete
  `CommandHistory`, one commit.
- **D2 — adapter first:** make `CommandHistory.Execute` delegate to `UndoStack`, so tiers 2 and 3 become
  undoable without touching call sites; delete `CommandHistory` later. Lowest risk, two steps.
- **D3 — drawers only:** convert tier 3, leave tier 2 on `CommandHistory`. Leaves the split in place.

**Claude's lean: D2.** The 6 sink sites already call `RecordPropertyEdit` correctly — their *only* defect is
which stack it lands on. Fixing the destination fixes all 6 for free, with no edit to the call sites, and
keeps the `Execute()`-performs-the-mutation invariant intact throughout.

## Q22-E — Undo granularity for continuous edits

Not addressed anywhere in the current code. A drag on a float slider or typing in a text box fires an edit
per frame / per keystroke — one entry each would blow the 64-entry ring in a single drag.

- **E1 — coalesce by (node, property) while the widget stays active**, commit one entry on
  deactivate. ImGui gives `IsItemDeactivatedAfterEdit()` for exactly this.
- **E2 — one entry per change**, accept ring churn.
- **E3 — defer; treat as follow-up.**

**Claude's lean: E1**, and it is cheap *if decided now* — the apply/undo pair is captured at commit time
rather than per-change. Retrofitting coalescing after the fact means revisiting every converted site, so
this is the one sub-question where deferring costs materially more than deciding.

---

## Recommended package (if the architect concurs)

**A1 + B1 + C2 + D2 + E1.** Net effect: one live stack; `IEditService` honest about its capability;
`EditService` still canvas-agnostic via a delegate; no call-site churn in the first step; slider drags
produce one undo entry.

**Verification is fully headless** — assert that a drawer edit followed by `view.UndoLast()` restores the
prior value, which is exactly the assertion no existing test makes.

## Answers

*(to be filled in from the architect pass)*

| Sub-question | Decision | Notes |
|---|---|---|
| Q22-A — which stack | | |
| Q22-B — interface shape | | |
| Q22-C — GraphView access | | |
| Q22-D — migration order | | |
| Q22-E — granularity | | |
