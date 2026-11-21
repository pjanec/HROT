<!--STATUS
state: LIVE
updated: 2026-09-30
current-answer: all three sections are built; "Bit / shift operators (CE-471)" is the newest
stale-below: the header's "Status" line (boolean pending) is history — both sections below are DONE
related-designs:
  - docs/blueprints/Architect_Question_78_Hill_Attack_The_Blueprint_Node_Way.md — owns WHY the bit operators were wanted (§6 row 9, §7 row 6)
-->
# Arithmetic `BinaryOp` + boolean logic nodes (mini-design)

> The natural general round-out of the GAP-12 `Compare` node — same pure-data + infix-emit machinery.
> `Compare` already ships the **full** comparison set (all six `ComparisonOperator`s). This doc covers the
> two obvious companions. **Status:** arithmetic `BinaryOp` = building now (near-demand: `AimAndFire`
> round-count math, `CalculateSegments`); boolean `And`/`Or`/`Not` = **pending** (architect Q6-A explicitly
> said keep boolean composition as `Branch`/helper — needs a user/architect nod before building).

## Arithmetic `BinaryOp` (✅ DONE — `5481e7d`)

Pure-data node, byte-for-byte the `Compare` shape except the result type = the operand type (not bool)
and the operator enum is arithmetic.

- **`Nodes.cs`:** `[JsonDerivedType(typeof(BinaryOpNode),"BinaryOp")]` + `BinaryOpNode : Node { ArithmeticOperator Operator }`, and a new `enum ArithmeticOperator { Add, Subtract, Multiply, Divide, Modulo }`.
- **Pins (asset-authored, mirrors `CompareNode`):** `A` in, `B` in (same numeric type), `Result` out (= operand type).
- **`BuiltInNodeRegistry`:** `BinaryOpNode => Array.Empty<PinSchema>()`.
- **`Stage0.NodeRequiresExecFallback`:** `false`.
- **`Stage5.ResolveNodeOutput`:** `case BinaryOpNode` — resolve `A`/`B` via `ResolveDataPin`, emit `IrOp_BinaryOp(a, b, op)` into a fresh value **typed = A's type** (result of `+`/`-`/… on `T` is `T`), cache `Result`. (Mirror the `CompareNode` case exactly; only the result IrTypeRef differs — reuse `aVal.Type`.)
- **`IrOperation.cs`:** `IrOp_BinaryOp(IrValue Left, IrValue Right, ArithmeticOperator Op)`.
- **`StatementEmitter`:** `case IrOp_BinaryOp` → `var __t{idx} = __t{Left} {infix} __t{Right};`, infix switch `Add "+", Subtract "-", Multiply "*", Divide "/", Modulo "%"` (same shape as `ComparisonOperatorInfix`; the `op_<Op>_<Type>` map at 936-949 already carries `+ - * /`).
- **Type resolve:** none — result type flows from operand A (reflection-free).
- **Coverage:** add a `BinaryOp` `Build*MinimalAsset` to `NodeCoverageTests` (e.g. `2 + 3`, full Roslyn pipeline).

Gates: real build 0 err; full Blueprints.Tests + generator suites green.

## Boolean `And`/`Or`/`Not` (✅ DONE — `7c84b01`)

**Decision record:** architect Q6-A leaned "keep boolean composition as `Branch`/helper". After a full
control-flow-vs-data-flow tradeoff walkthrough (see chat), the **user explicitly chose to add all three**
for authoring ergonomics (flat compound conditions, reusable/nameable bools, explicit negation). **Known
caveat, accepted:** these are DATA-flow nodes, so **no short-circuit** — an `And`/`Or` node resolves BOTH
operands as values before combining (unlike nested `Branch`es, which short-circuit). Harmless here because
condition inputs are pure, side-effect-free reads (`Compare`/`GetComponent`/`HasComponent`) — at worst a
wasted read, never a wrong result or an unwanted side effect. `Branch` remains the execution-routing node;
these compose the *condition value* that feeds it.

Two nodes, same pure-data + infix machinery as `Compare`:

**`BooleanOpNode` (And/Or — binary):**
- `enum BooleanOperator { And, Or }`; `[JsonDerivedType(typeof(BooleanOpNode),"BooleanOp")]`; `BooleanOpNode : Node { BooleanOperator Operator }`.
- Pins (asset-authored, mirror `CompareNode`): `A` in (bool), `B` in (bool), `Result` out (bool).
- Registry `Array.Empty`; Stage0 `NodeRequiresExecFallback => false`.
- Stage5 `case BooleanOpNode` — mirror `CompareNode` exactly (result typed `BoolType`), emit `IrOp_BooleanOp(a, b, op)`.
- `IrOp_BooleanOp(IrValue Left, IrValue Right, BooleanOperator Op)`; emit `var __t{idx} = __t{L} {infix} __t{R};` with `And => "&&", Or => "||"`.

**`NotNode` (unary):**
- `[JsonDerivedType(typeof(NotNode),"Not")]`; `NotNode : Node` (no operator prop).
- Pins: `A` in (bool), `Result` out (bool) — single operand.
- Registry `Array.Empty`; Stage0 `NodeRequiresExecFallback => false`.
- Stage5 `case NotNode` — resolve the single `A` in-pin, emit `IrOp_Not(a)` into a `BoolType` value, cache `Result`.
- `IrOp_Not(IrValue Operand)`; emit `var __t{idx} = !__t{Operand.Index};`.

Coverage: `BuildBooleanOpMinimalAsset` (`true && false`) + `BuildNotMinimalAsset` (`!true`), full Roslyn
pipeline. Gates: real build 0 err; full Blueprints.Tests + generator suites green.

## Bit / shift operators — `CE-471` (✅ DONE `2026-09-30`)

Five more `ArithmeticOperator` values on the same `BinaryOpNode`, **appended** (the enum is persisted by value):
`BitAnd &` · `BitOr |` · `BitXor ^` · `ShiftLeft <<` · `ShiftRight >>`. No new node, IR op or emitter case.

| as built | where |
|---|---|
| infix map + a shift count cast to `(int)` (C# requires an `int` count; a `long` pin still compiles) | `StatementEmitter.ArithmeticOperatorInfix`, `case IrOp_BinaryOp` |
| **`BP1678`** — a bit/shift operator on a type the compiler KNOWS is not an integer (float, double, decimal, string, vectors, entity; bool for shifts). Deny-list, reflection-free: an unknown struct or a `[Flags]` enum passes (`& | ^` are defined on enums) | `Stage5_Schedule.IsKnownNonIntegerOperand` |
| palette entries `BinaryOp.BitAnd` … `BinaryOp.ShiftRight`, titles `Math &` … | `BlueprintNodePaletteEntries`, `BlueprintNodeModel.OperatorSymbol` |
| rails — real Roslyn compile per operator + the `BP1678` refusal | `CE471_BitOperatorTests` |

⚠ **Deviation from the plan row:** no unary `BitNot` — `BinaryOp` is A/B-shaped; `A ^ AllOnes` (XOR with `-1`, or
`~0` of the unsigned type as a literal) inverts. ⚠ **Expected, shared with `+`:** operands narrower than `int` (byte/short)
are promoted by C#, so `var __t = a & b` is `int` while the result pin says `byte`; wire through an `int` until the
emitter casts back (not new in `CE-471`; ⛔ expected from C# promotion rules, not measured through this pipeline).
