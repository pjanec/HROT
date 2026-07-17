# P1 — `FlowForEach` loop + `GetUnitRoster` source (design)

> Architect Q#5-C: **inline C# `for` over `[0,Count)`**, body is a **synchronous latent-free sub-DAG**,
> validator **rejects latent nodes** in the body, iteration source is a **curated `GetUnitRoster`**
> (keep raw fixed-array access out of the graph). GAP-1, the biggest missing capability.

## Target (slice 4 oracle)
`Condition_AreAllAtBaseline` = *for each subordinate in the commander's `UnitRoster`, read its
`NavigationStatus.Result`, and Succeed only if all have `Arrived`.* Composes **P2** (component read,
incl. the foreign-entity Target pin) + **P1** (this) + the **GAP-12** comparator helper.

## Building blocks that already exist (reuse)
- Per-subordinate read → **P2 `GetComponent`** with its **Target pin** (the loop item Entity).
- Arrived check → **GAP-12** `HillAssault2NavOps.IsArrived` helper (until the native Compare node).
- Component read of the roster itself → **P2** machinery (`IrOp_GetComponentRO<UnitRoster>`).

## The two new pieces

### 1. `GetUnitRoster` source (curated)
`UnitRoster` = `int Count` + `fixed long SubordinateEntities[16]` + `fixed ushort TacticalDesignations[16]`.
The `fixed` buffer needs **unsafe** access → keep it in a **curated C# accessor**, not the graph:
```csharp
public static class UnitRosterOps            // new, part of the public blueprint API
{
    public static int  Count(in UnitRoster r) => r.Count;
    public static Entity Subordinate(in UnitRoster r, int i) { unsafe { return Entity.FromPacked(r.SubordinateEntities[i]); } }
}
```
`GetUnitRoster` node = P2-style read of `UnitRoster` on self, exposing two things to the loop: a
**Count** (int) and an indexed **Subordinate(i)** accessor. (Confirm `Entity.FromPacked`/equivalent exists;
else add a tiny ctor helper.) Cap-bounded by `Count` (≤16).

### 2. `FlowForEach` node + `IrOp_ForEach`
- **Node**: exec-in, a **"Body"** exec-out (loop body root), a **"Completed"** exec-out (after the loop),
  a **collection** data-in (from `GetUnitRoster`), and a **"CurrentItem"** data-out (Entity) the body reads.
- **New IR**: `IrOp_ForEach(IrValue Count, string ItemAccessorExpr, IrValue ItemVar, IReadOnlyList<IrStatement> Body)`
  — carries the body as a **nested** statement list (NOT BFS blocks).
- **Scheduler**: on `FlowForEachNode`, schedule the Body exec-chain **inline** into a fresh nested
  statement list (own value-numbering; `CurrentItem` bound to the loop var), then emit `IrOp_ForEach`,
  then continue the outer chain at "Completed". Do **not** enqueue the body on the BFS block queue.
- **Emit**: `for (int __i = 0; __i < {Count}; __i++) { var __item = {accessor}(__i); {body…} }`.
- **Validator** (Stage2): body sub-DAG must be **latent-free** — reject `WaitForChannel`/`WaitForEvent`/
  `Delay`/`When` reachable from the Body exec-out (BP-new). Architect-mandated.

## The hard part — body control flow
The scheduler models `Branch` as a **block split** (new BFS block). An inline-`for` body can't span blocks.
Two paths:
- **(A) Branch-free first slice.** Prove the loop mechanics with a body that has **no in-body Branch**:
  e.g. *for each subordinate, publish an event* (P4) or *accumulate a count*. Ships the loop + reduce-free.
- **(B) Inline-`if` body.** Extend the scheduler so a `Branch` inside a `FlowForEach` body emits as an
  **inline `if/else`** (nested statements) rather than a block split. Needed for `AreAllAtBaseline`'s
  conditional AND-reduce. Bigger; do it as P1b.

`AreAllAtBaseline` reduce (needs B): WorkingState `bool AllAtBaseline = true`; body: `if
(!IsArrived(GetComponent(item,NavigationStatus).Result)) AllAtBaseline = false;`; after loop: return
`AllAtBaseline ? Success : Failure`. (An early-out `break` on first false is a nice-to-have optimization.)

## Proposed sequencing
1. **P1a** — `GetUnitRoster` + `UnitRosterOps` + `FlowForEach` + `IrOp_ForEach`, **branch-free body**
   (path A). Proof: `HillAssault2_ForEachSubordinate_PublishClear` (per-subordinate `ClearBehaviorEvent`,
   reusing P4) — proves iteration + curated source + inline-`for` emit + latent-free validation.
2. **P1b** ✅ **DONE** (`50ff6e4`) — inline-`if` body (path B) in the scheduler. `IrOp_If` +
   `Stage5.ScheduleInlineBodyChain`/`FindInlineBranchJoin`; BP2050 relaxed to allow Branch. Proof:
   **slice 4 `AreAllAtBaseline`** end-to-end vs the C# oracle (foreach → P2 read → GAP-12 check →
   AND-reduce → post-loop Return), through the real generator.

Each is a separate reviewed+gated+committed step. P1a de-risks the loop substrate before P1b's
scheduler surgery.

**Resolved (P1a/P1b):** `IrOp_ForEach`'s nested-body model composes fine with `_pinValueCache`/CSE —
the loop body snapshots+removes body-added cache keys at the loop boundary, and P1b adds the same
snapshot/remove **per branch arm**, so arm-scoped values never leak to the sibling arm or the
post-join scope. Join detection (`FindInlineBranchJoin`) = the nearest common successor of the two
arms (null when either arm ends → each arm self-contained, e.g. slice 4's unwired-True arm); it also
handles the reconverging `if(x){A}else{B}C` shape (C emitted once after the `if`).
