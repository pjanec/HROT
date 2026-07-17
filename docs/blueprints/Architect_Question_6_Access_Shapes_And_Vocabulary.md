# Architect question #6 — remaining access shapes + node-vocabulary scope

**Context.** P1b (in-body inline `if/else`) and slice 4 (`AreAllAtBaseline`) are done; the loop +
conditional-reduce substrate is proven end-to-end through the real generator. A design-readiness sweep
of the *remaining* Hill-attack oracle nodes finds most are build-ready on already-shipped capabilities —
**except four decision points** the migration has never actually put to you. Bundling them (like Q#5's
A/B/C/D) so we get them right instead of guessing. Each is "how does the engine want us to reach X",
plus one node-vocabulary-precedent question. Our lean is given for each; we proceed on the leans unless
you redirect.

## Q-A — `Compare`/`BinaryOp` node-vocabulary scope (GAP-12)

We're shipping a minimal pure **`Compare`** node now (2 operands + `ComparisonOperator` → `bool`,
reusing the enum that today lives only in `WhenNode`; see `GAP12_Compare_Node_Design.md`). It's a safe
subset and retires the `HillAssault2NavOps.IsArrived` C# stopgap, making conditions fully visual.

The **precedent question**: do you want a fuller **arithmetic/boolean node family** to match engine
conventions — an arithmetic `BinaryOp` (`+ - * /`, needed by `AimAndFireSpecific`'s round-count math and
`CalculateSegments`), and boolean `And`/`Or`/`Not` (needed to compose multi-clause conditions without a
nest of `Branch`es)? Or should non-trivial arithmetic/logic stay in curated `FunctionCall` helpers
(reviewable C#), with `Compare→bool` the *only* native operator node?
- **Our lean:** ship `Compare→bool` now; add a native **arithmetic `BinaryOp`** when the first slice
  needs it (`AimAndFire`/`CalculateSegments`), reusing the same infix-emit path
  (`StatementEmitter.cs:936-949`); keep boolean composition as `Branch`/curated helper until a slice
  proves a need. I.e. grow the vocabulary demand-driven, not speculatively.
- **Reuse vs build:** `Compare` = new pure node + `IrOp_Compare` (reuses infix map). `BinaryOp` = the
  same shape with arithmetic operators + a result type = operand type. No ABI change either way.

## Q-B — `GetSingleton` shape: field-read vs curated method-call; and the ABI (GAP-10, P3)

`AimAndFireSpecific`'s target-resolve calls
`ctx.World.GetSingletonManaged<NetworkEntityMap>().TryGetEntity(p.TargetNetworkId, out targetEntity)`
(`HillAttackTankNodes.cs:339-350`) — a singleton **read followed by a method call with an `out`**, not a
plain field read. Meanwhile `ISimulationView` has **no singleton accessor at all** (GAP-10); the
AiPrimitive path reaches singletons only by downcasting `world` to `EntityRepository`.

Two coupled decisions:
1. **Node shape.** Is `GetSingleton` a plain **field-read** node (`GetSingleton<T>() → T`, then a
   `GetComponent`-style field/`Compare` chain), or does Hill-attack's real need (`TryGetEntity`) call for
   a **curated method-call form** — e.g. a `GetSingleton<T>`-plus-curated-accessor node, or just a
   context-aware `FunctionCall` (P7) that receives `world` and calls a curated
   `NetworkEntityMapOps.TryGetEntity(world, netId)` helper returning the `Entity` (or `Entity.Invalid`)?
2. **ABI.** Do we extend **`ISimulationView`** with a read-only singleton accessor (clean, works for both
   Instance + AiPrimitive), or keep keying off the **`EntityRepository`** downcast (AiPrimitive-only, but
   zero ABI change and matches how `GetShared`/`ChannelCommand` already reach `world`)?
- **Our lean:** for Hill-attack specifically, resolve the target via a **context-aware `FunctionCall`**
  to a curated `NetworkEntityMapOps.TryGetEntity(world, netId) → Entity` helper (P7 path already exists,
  reflection-free, keeps the `out`/lookup in reviewable C#) — i.e. **do NOT build a generic
  `GetSingleton` node yet**, since the one real consumer needs a method call, not a field read. Revisit a
  native `GetSingleton` field-read node only if a slice needs a *plain* singleton field. On the ABI:
  `EntityRepository` downcast for now (no ABI churn); extend `ISimulationView` only if/when Instance
  dispatch needs singletons.
- **Reuse vs build:** lean = one curated static helper + a `FunctionCall` (zero new node/IR). The
  alternative (generic `GetSingleton` + method-call variant + `ISimulationView` change) is a much larger,
  ABI-touching build for a single consumer.

## Q-C — building the `AssignTacticalIntentEvent.JsonParams` string payload from pins

`DispatchAllToBaseline` and the wave dispatch publish
`AssignTacticalIntentEvent{ IntentId="MoveToLocation", JsonParams=<serialized MoveToLocationParams> }`
— a **managed** engine event whose `JsonParams` is a raw serialized-**string** field
(`BuiltInEngineEventCatalog.cs:218-226`). P4 `PublishEvent`'s payload model is concrete scalar/vector/
entity pins (`Blueprint_Generic_Primitives_Design.md` §4a) — it has **no "serialize an object to a JSON
string" step**. How should a non-programmer produce that payload?
1. A curated **`FunctionCall` JSON-builder** helper (`MoveToLocationParams`-shaped inputs → JSON string),
   published via P4 as an opaque string field. *(smallest; our lean)*
2. A generic **managed-event payload node** that serializes a struct built from pins (bigger; new
   serialize-at-emit machinery — and the analyzer can't reflect the type at gen time).
3. Accept it as a **P7 escape hatch** (the whole publish is a curated `FunctionCall` that builds + posts
   the event), i.e. don't express this event visually at all.
- **Our lean:** (1) — a curated typed→JSON helper feeding P4's string field; keeps the serialization in
  reviewable C#, no new IR, and the `IntentId`/`JsonParams` contract stays honest. Confirm this is the
  sanctioned way to author string-payload managed events, or tell us the intended pattern.
- **Reuse vs build:** lean = one helper + existing P4. Option 2 is new emit machinery for a managed
  serialize; option 3 gives up visual authorability of the dispatch.

## Q-D — EQS request/poll: does `SpawnEqsSensor`/`ReadEqsResult` front `AreaQueryBatchHelper`?

The backlog pairs the EQS slice (`RequestAreaQuery`/`IsAreaQueryResolved`) with the existing
`SpawnEqsSensor`/`ReadEqsResult` nodes — but that mapping is **unverified**. `SpawnEqsSensorNode` is
documented (`Nodes.cs:307-314`) as resolving an `[EqsTemplate(AssetId=…)]` declaration, whereas the
oracle's area query calls `AreaQueryBatchHelper.RequestAreaQuery(world, self, targetAreaEntity,
ForceId.Hostile)`, polls `GetAreaQueryResult` (5 s timeout), and `FreeAreaQuerySlot`s explicitly
(`HillAttackCommanderNodes.cs:188-227`) — a polygon/force-filtered **batch** query.
- **Question:** does `SpawnEqsSensor`/`ReadEqsResult`'s existing template lowering actually drive
  `AreaQueryBatchHelper` under the hood (→ reuse-only, this slice is small), or is the batch area-query a
  **separate EQS surface** that needs a new `[EqsTemplate]` wrapper (or a curated
  `AreaQueryBatchOps.*`-style helper family) around `RequestAreaQuery`/`GetAreaQueryResult`/
  `FreeAreaQuerySlot`?
- **Our lean:** we suspect they are **different surfaces** and this slice needs a small curated
  batch-area-query node/helper pair, NOT the template-based `SpawnEqsSensor`. Confirm which, so we scope
  it right rather than forcing the oracle's batch query into the template path.
- **Reuse vs build:** if same surface → reuse-only (S). If different → a new curated
  request/poll/free helper trio or a batch-query node (M). We won't guess.

---

**Our lean defaults if you're happy with them:** A — ship `Compare→bool` now, add arithmetic `BinaryOp`
demand-driven, boolean composition stays helper/`Branch`. B — Hill-attack target-resolve via a curated
`NetworkEntityMapOps.TryGetEntity` `FunctionCall` (no generic `GetSingleton` node yet); `EntityRepository`
downcast, no `ISimulationView` ABI change. C — curated typed→JSON `FunctionCall` helper feeding P4's
string field. D — assume `AreaQueryBatch*` is a distinct surface needing a small curated helper trio, not
the template `SpawnEqsSensor`. We'll proceed on these unless you redirect.

---

## ARCHITECT ANSWERS (pending — paste here)

- **A —**
- **B —**
- **C —**
- **D —**
