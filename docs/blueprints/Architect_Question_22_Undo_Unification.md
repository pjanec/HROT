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

## Answers — **APPROVED 2026-08-04**

Full recommended package accepted: **A1 + B1 + C2 + D2 + E1**. ✅ **BP-11 is unblocked.**

| Sub-question | Decision | Architect's reasoning |
|---|---|---|
| Q22-A — which stack | **A1** | Collapse onto `UndoStack`, retire `CommandHistory`. Explicitly answers the open sub-question: `UndoStack` *was* conceived as graph-centric, but a unified chronological Ctrl+Z is paramount; non-graph commands on it are acceptable **provided C2's transport delegates keep the layering clean**. A3's coordinator rejected as unnecessary complexity. |
| Q22-B — interface shape | **B1** | Promote `RecordPropertyEdit` + `NotifyStructureChanged` onto `IEditService`; kills the `as EditService` downcasts. B2's minimality "is exactly the ambiguity that allowed Tier 3 to silently skip undo." Test doubles implement as no-ops. |
| Q22-C — GraphView access | **C2** | Injected transport delegate. Direct `GraphView` coupling (C1) would break the deliberate precedent already set by `OnStructureChanged`. |
| Q22-D — migration order | **D2** | Adapter first — `CommandHistory.Execute` delegates to `UndoStack`. Delete `CommandHistory` as later cleanup. |
| Q22-E — granularity | **E1** | Coalesce by (node, property) while the widget is active; one entry on deactivation. E2's ring churn rejected — "a single drag would instantly blow past our 64-entry buffer." |

Verification strategy approved as proposed: a headless test asserting a drawer edit followed by
`view.UndoLast()` restores the prior value.

---

## ⚠ Implementation addendum — three gaps in the approved package

Checked against code after the approval. **None blocks the decisions above**; all three are
implementation-level and the recommended resolutions stay inside the approved architecture.

### 1. `UndoStack` cannot carry a `PropertyEditCommand` — a transport must be chosen (**the real one**)

`UndoStack.ApplyAndRecord(GraphCommand forward, GraphCommand inverse, string label)` takes
**`GraphCommand`**, applying it via `_sink.Apply(forward)`. `GraphCommand` is an abstract record with
~30 *data* variants describing graph mutations — **zero `Action`/`Func<` anywhere in the file**. A
`PropertyEditCommand(string, Action apply, Action undo)` is therefore **not expressible on the stack as-is**.

> **C2 does not dodge this.** Whatever the delegate's signature, the value that ultimately lands on
> `UndoStack` must be a `GraphCommand` pair. This is the one genuine hole in the package.

| Option | Assessment |
|---|---|
| **R1 — route through the existing `SetNodeProperty(NodeId, string Key, object? Value)`** ✅ **recommended** | Already in the vocabulary, already handled by `BlueprintCommandSink:129` → `ApplySetNodeProperty`, and the sink ctor doc already names it *"Property-edit service for `GraphCommand.SetNodeProperty`"*. Zero changes to NodeEdit. |
| R2 — add a delegate-carrying variant to `GraphCommand` | Requires editing **`FDP/ExtDeps/NodeEdit`** — a vendored external tree. Needs its own layering nod. Avoid unless R1 provably can't express an edit. |

**Open (non-blocking) question:** `SetNodeProperty` is keyed by `NodeId` + string key. Any drawer edit
that is *not* node-scoped — asset-level variables, multi-field bakes — cannot be expressed by it. If such
a case appears during implementation, R2 (or an asset-scoped sibling) needs a quick confirm. Everything
node-scoped proceeds on R1 now.

### 2. D2 fixes Tier 2 only — **Tier 3 still needs its call sites converted**

The approval states D2 "immediately routes Tiers 2 and 3 into the unified undo stack." **It routes Tier 2
only.** Tier 3 drawers never call `RecordPropertyEdit` — they call `MarkDirty` and nothing else (verified:
`RecordPropertyEdit`'s only non-test callers are the 6 in `BlueprintCommandSink`). Re-pointing
`CommandHistory` cannot capture an edit that was never recorded.

So the ~9 `MarkDirty`-only sites across 5 drawer files (`ComponentNodeDrawers` 2 ·
`PlayMontageChainNodeDrawer` 3 · `SharedNodeDrawers` 2 · `FunctionCallNodeDrawer` 1 ·
`LiteralNodeDrawer` 1) must each be converted to record an apply/undo pair. **This is unavoidable under
any of A1/D1/D2** and is the bulk of the work. D2's low-risk claim holds for the 6 sink sites; it does not
extend to the drawers.

### 3. A new `GraphCommand` variant would fail **silently** if the sink isn't updated

`BlueprintCommandSink.Apply`'s `default:` case returns `new GraphCommandResult(true, null)` —
*"Unknown commands are silently accepted (forward-compat)"*. It reports **success**. So under R2, adding a
variant without a matching sink case yields an undo that no-ops *and claims to have worked* — precisely
the failure class BP-11 exists to remove. R1 sidesteps this entirely (`SetNodeProperty` already has a case).

### Minor — E1 mechanics

`IsItemDeactivatedAfterEdit()` fires *after* the value has already changed, so it cannot "capture the
pre-drag baseline" as described. Snapshot the baseline on **`IsItemActivated()`**, pair it with the final
value on deactivation. Same design, correct hook.

### Not applicable

The closing offer regarding *"ALC safety limits during teardown"* concerns `AssemblyLoadContext`
lifetime and has no bearing on undo unification. Disregarded.

---

## Net effect on the estimate

**BP-11 reclassified `RW-L` → `RW-M`.** Not because the design changed — the approved package is sound —
but because the verified work is a transport decision (gap 1) plus ~9 call-site conversions (gap 2) plus
activation-time coalescing (E1), which is past "contained new logic, no design decisions."
