# FlowForEach loop-introspection outs — `CurrentIndex` + `Count`

**Type:** demand-driven round-out of an existing node's interface (not new vocabulary).
**Trigger:** `DispatchAllToBaseline` needs the per-tank lerp `t = count>1 ? i/(count-1) : 0.5f`
— i.e. the body must see the 0-based iteration index **and** the element count. `FlowForEach`
previously exposed only `Body`/`Completed`/`CurrentItem`.

## What shipped

Two **optional** data-out pins on `FlowForEachNode` (unwired = zero cost, nothing emitted):

| Pin            | Type          | Scope        | Emit |
|----------------|---------------|--------------|------|
| `CurrentIndex` | `System.Int32`| body-local   | `var __t{idx} = __fe{item};` at top of loop body |
| `Count`        | `System.Int32`| outer-local  | `var __t{cnt} = global::{CountAccessorFqn}({roster});` **before** the `for`, reused as the loop bound |

```
var __tC = global::…UnitRosterOps.Count(__tR);   // Count out  (loop-invariant, hoisted once)
for (int __fe0 = 0; __fe0 < __tC; __fe0++)
{
    var __tItem = global::…UnitRosterOps.Subordinate(__tR, __fe0);
    var __tI = __fe0;                             // CurrentIndex out (body-scoped)
    …body uses __tI / __tC…
}
```

## Design points

- **Count is loop-invariant → hoisted to the outer scope**, and reused as the loop bound. This is
  valid both inside the body and in the `Completed` chain. It also stops re-evaluating the accessor
  each pass. Only done when the `Count` pin is wired; unwired keeps the original inline
  `< global::…Count(...)` bound so **existing goldens stay byte-identical**.
- **CurrentIndex is body-scoped** (depends on the changing loop var), so it is bound *after* the
  body-cache snapshot and cleaned up with the other body-scoped values (same lifecycle as
  `CurrentItem`) — it never leaks to the outer scope.
- **Zero ABI change.** `IrOp_ForEach` gained two nullable `IrValue?` fields (`CountVar`, `IndexVar`),
  both defaulted `null`; the emitter branches on them. No Stage6/lowering pass consumes `IrOp_ForEach`.

## Why no architect round

This completes the obvious interface of a loop node that already exists (every general foreach
exposes an index/count) and is pulled by a concrete slice — it is not a speculative new vocabulary,
and touches no engine-semantics decision. Per the architect-questioning discipline this qualifies as
a demand-driven round-out proceeding on this in-repo note. The **slice-level** design questions for
the consumer (`DispatchAllToBaseline`) — JSON-params builder, managed `PublishEvent` — were already
put to the architect and approved in `Architect_Question_6_Access_Shapes_And_Vocabulary.md` (C).

## Proof (headless)

- `NodeCoverageTests.FlowForEach_IndexAndCount_EmitsHoistedCountAndBodyIndexCopy` — compiles a
  FlowForEach whose body wires `CurrentIndex`/`Count` into an arithmetic `BinaryOp` (`index - count`)
  feeding a `SetShared`, then asserts the emitted C# hoists the count local, copies the loop var into
  a body local, and consumes both. Locks the emission contract without game assemblies.
- `Inline/FlowForEachIndexCount` coverage fixture (Stage1-7) — same asset through the real
  multi-stage compiler, zero error diagnostics.
- Full Roslyn build proof lands with the `DispatchAllToBaseline` slice (its real consumer).
