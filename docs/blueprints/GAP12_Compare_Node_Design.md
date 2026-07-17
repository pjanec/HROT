# GAP-12 — native `Compare` node (mini-design)

> **Scope of THIS doc:** the minimal, architect-gate-free build — a pure `Compare` node that turns two
> values + a `ComparisonOperator` into a `bool`, retiring the `HillAssault2NavOps.IsArrived` stopgap.
> The broader "do we also want an arithmetic `BinaryOp` / boolean `And`/`Or`/`Not` node family?" is a
> vocabulary-precedent question deferred to **Architect Question #6-A** — the minimal `Compare→bool`
> below is a safe subset regardless of that answer, so it ships now (HANDOFF §4 mirror-pattern recipe).

## Why it's a mirror-pattern build (no architect gate)

`ComparisonOperator` (Equal/NotEqual/LessThan/LessThanOrEqual/GreaterThan/GreaterThanOrEqual) already
exists (`Nodes.cs:266`) — today only inside `WhenNode`. The operator→C# infix map already exists too
(`StatementEmitter.cs:936-949`, the `op_Eq_Byte`→`==` path used by FunctionCall operator lowering). So
this is: **one new pure-data node + one new IR op reusing the existing infix map + one Stage5 case**,
exactly the shape P2 (`GetComponent`) / GAP-11 (`GetParameter`) followed.

## The node

`CompareNode` (`kind: "Compare"`), pure data (no exec pins). Props: `Operator: ComparisonOperator`.
Pins (asset-authored, mirrors `GetComponentNode` — registry returns `Array.Empty`, Stage0's
`Pins.Count > 0` guard leaves them alone; no enricher):

| Pin | Dir | Type | Notes |
|---|---|---|---|
| `A` | In (data) | operand type | left operand (wired from any pure-node/literal/blackboard out) |
| `B` | In (data) | operand type | right operand — same type as A (C# `==`/`<` require it) |
| `Result` | Out (data) | `System.Boolean` | the comparison result |

## Compiler wiring (the recipe)

1. **`Nodes.cs`** — `[JsonDerivedType(typeof(CompareNode), "Compare")]` + class with `Operator`.
2. **`BuiltInNodeRegistry.GetStaticPins`** — `CompareNode => Array.Empty<PinSchema>()` (pure; asset supplies pins). Mirrors `GetComponentNode`.
3. **`Stage0_Rehydrate.NodeRequiresExecFallback`** — `false` (pure data).
4. **`Stage5_Schedule.ResolveNodeOutput`** — `case CompareNode cn`: resolve `A` and `B` via their linked outputs (`ResolveDataPin`), emit `IrOp_Compare(a, b, cn.Operator)` into a fresh `BoolType` value, cache the `Result` pin → return it. Mirrors the `GetComponentNode`/`LiteralNode` pure-node cases.
5. **`IrOperation.cs`** — `IrOp_Compare(IrValue Left, IrValue Right, ComparisonOperator Op)`.
6. **`StatementEmitter.cs`** — `case IrOp_Compare op`: `var __t{idx} = __t{Left.Index} {infix} __t{Right.Index};`, where `{infix}` comes from a small `ComparisonOperator → "=="/"!="/"<"/"<="/">"/">="` switch (extract/share with the existing infix map at 936-949).
7. **Type resolve (Stage4):** `Result` pin = `System.Boolean` (authored). `A`/`B` types flow from their wired sources — no `StaticTypeRegistry` change (reflection-free; enum operands resolve via their `global::`-prefixed pin types exactly as P2's field read does).

## Retrofit (the GAP-12 payoff — helper-free conditions)

Once the node lands, rewrite the two conditions to drop the C# comparator helper:

- **`HillAssault2_IsSelfArrived.bp.json`** — replace `FunctionCall IsArrived(result)` with
  `Compare(A = GetComponent.Value, B = Literal(NavigationResult.Arrived), Op = Equal)` → `Branch.Condition`.
- **`HillAssault2_AreAllAtBaseline.bp.json`** — same swap inside the loop body (Branch condition becomes the `Compare` Result; True/False arms unchanged).
- **Enum literal:** the `B` operand is a `Literal` whose `ValueJson` is the FULLY-QUALIFIED member
  `global::Fdp.Toolkit.Navigation.NavigationResult.Arrived` (`IrOp_Const` emits `ValueJson` verbatim as
  the C# literal; enum `==` needs both sides the enum type).
- Then **delete `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAssault2NavOps.cs`** (only these two
  assets reference it) — the conditions become fully visually-authored (the true non-programmer endpoint).

## Proof / gates

Update both proof tests: assert the generated `TickCore` now contains the infix compare
(`== global::Fdp.Toolkit.Navigation.NavigationResult.Arrived`) and **no longer** contains
`HillAssault2NavOps.IsArrived(`. Behavioral assertions (Arrived→Success / not→Failure) stay identical.
Add a `Compare` node to `NodeCoverageTests` (`Build*MinimalAsset` + `CoverageAssets`) per HANDOFF §4.7.
Gates: real build 0 err; `HillAssault2_*` all green; full Blueprints.Tests + generator suites green.
