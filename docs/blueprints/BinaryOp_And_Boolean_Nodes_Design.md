# Arithmetic `BinaryOp` + boolean logic nodes (mini-design)

> The natural general round-out of the GAP-12 `Compare` node — same pure-data + infix-emit machinery.
> `Compare` already ships the **full** comparison set (all six `ComparisonOperator`s). This doc covers the
> two obvious companions. **Status:** arithmetic `BinaryOp` = building now (near-demand: `AimAndFire`
> round-count math, `CalculateSegments`); boolean `And`/`Or`/`Not` = **pending** (architect Q6-A explicitly
> said keep boolean composition as `Branch`/helper — needs a user/architect nod before building).

## Arithmetic `BinaryOp` (building now)

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

## Boolean `And`/`Or`/`Not` (PENDING — do not build without a nod)

Same machinery: `And`/`Or` = binary (`&&`/`||`) → bool; `Not` = unary (`!`) → bool. **Architect Q6-A
explicitly said keep boolean composition as `Branch` nodes / C# helpers, not speculative native nodes** —
so these are held pending a user/architect go-ahead (the user's "build wider" steer greenlights it, but it
contradicts a fresh explicit ruling, so we flag rather than silently build). If approved: mirror the
`BinaryOp` recipe with a `BooleanOperator` enum + a unary `Not` node (single operand pin).
