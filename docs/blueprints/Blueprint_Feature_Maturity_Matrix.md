# Blueprint Feature-Maturity Matrix (audit, 2026-07-16)

> ⚠ **Dated snapshot (2026-07-16) — partly superseded.** Several items below have since shipped and
> should now read ✅: `FlowForEach`, `PublishEvent` (+ custom-event pub/sub via `EventEntry`), the
> `Compare` / `BinaryOp` / `BooleanOp` / `Not` value ops, and the `MakeStruct` / `BreakStruct` /
> `SetMembers` struct-value nodes (not in this table at all). The node count is now ~42, not 30. For the
> current capability list see [Blueprints_Overview.md §3](Blueprints_Overview.md). This audit is kept for
> its per-axis (compiler / authoring / tests) breakdown and the still-valid "avoid" call-outs
> (`Cast`, `WaitForEvent`, dispatchers, `ArrayMake`/`ArrayGet`, squad quartet).
>
> Full-pipeline readiness of all 30 node kinds, to build the Hill-attack migration with eyes open.
> Two read-only audits (compiler-side; editor+runtime+tests) merged here. `file:line` evidence lives
> in the audit transcripts; this is the decision surface.
> **Axes:** **Compiler** = Validate+Lower(Stage5)+Emit (does it produce correct runtime code?).
> **Authoring** = can a *designer* configure it (real drawer/inspector form + create-time baking)?
> **Tests** = exercised end-to-end (run-assert) vs compile/round-trip-only vs none.
> Legend: ✅ real · ⚠ partial/stub/compile-only · ❌ missing/broken · `n/a` = no config to author.

## The headline

Two independent maturity axes, **both incomplete, and they don't line up**:
- **Compiler:** ~20/30 wired; **8 unlowered** (silent no-op/wrong-value); **2 broken** (`Cast`, `WaitForEvent`).
- **Authoring is the weaker axis:** only ~10 kinds have a real editing surface. **~16 kinds can't be
  meaningfully configured in the editor** — *including several that are fully runtime-wired and
  run-proven* (`When`, `ScoreDecision`, `ReadRankedResult`, `CallPeerBlueprint`, `CallCustomEvent`,
  `WaitForChannel`). So the designer-facing layer — the whole reason blueprints exist — is the real
  critical path, more so than the runtime.

## Matrix

| Node | Compiler | Authoring | Tests | Note |
|------|:--:|:--:|:--:|------|
| FunctionCall | ✅ | ✅ | ✅ | fully mature |
| Branch | ✅ | n/a | ✅ | no config |
| Sequence | ✅ | n/a | ✅ | no config |
| GetVariable | ✅ | ✅ | ✅ | baked at create |
| SetVariable | ✅ | ✅ | ✅ | baked at create |
| Literal | ✅ | ✅ | ✅ | per-type widget |
| EventEntry | ✅ | ✅ | ✅ | baked at create |
| Return | ✅ | n/a | ✅ | Status default |
| **Cast** | 🐛 | ❌ | ⚠ | emits `global::Cast.System.Int32(…)` → invalid C#; no validator; no drawer |
| **ArrayMake** | ❌ | ⚠ | ⚠ | unlowered; data pin → silent `default` (no diagnostic) |
| **ArrayGet** | ❌ | ⚠ | ⚠ | unlowered; same silent-wrong-value |
| Delay | ✅ | n/a | ✅ | duration via pin |
| **CallDispatcher** | ❌ | ❌ | ⚠ | unlowered (BP4004); `DispatcherId` uneditable |
| **BindDispatcher** | ❌ | ❌ | ⚠ | unlowered; uneditable |
| CallCustomEvent | ✅ | ❌ | ⚠ | wired but `EventId` uneditable; weak validation |
| CallPeerBlueprint | ✅ | ❌ | ⚠ | wired but target uneditable; compile-only tests |
| ChannelCommand | ✅ | ✅ | ✅ | action baked via per-action palette |
| WaitForChannel | ✅ | ❌ | ✅ | wired+run-proven but **no drawer** (`ChannelType` uneditable) |
| **WaitForEvent** | 🐛 | ❌ | ⚠ | short-name validates but Roslyn CS0400; lowered as a channel (wrong); Skip'd test |
| When | ✅ | ⚠ | ✅ | wired+run-proven, but **all 4 mode forms are stubs** (can't set condition) |
| ReadEqsResult | ✅ | ✅ | ✅ | sensor combo |
| SpawnEqsSensor | ✅ | ✅ | ✅ | template combo |
| ScoreDecision | ✅ | ❌ | ✅ | wired+run-proven but `AssetId` uneditable; no validator |
| ReadRankedResult | ✅ | ❌ | ✅ | wired+run-proven but `Rank` uneditable |
| **PartitionElements** | ❌ | ❌ | ⚠ | unlowered; exec-only pins → `ElementCount` unreachable |
| **AssignRoles** | ❌ | ❌ | ⚠ | unlowered; `ManeuverKind` unreachable |
| **AdvancePhase** | ❌ | ❌ | ⚠ | unlowered; config unreachable |
| **AcquireSlot** | ❌ | ❌ | ⚠ | unlowered; `TotalSlots` unreachable |
| GetShared | ✅ | ✅ | ✅ | filtered type picker |
| SetShared | ✅ | ✅ | ✅ | same |

## Ranked surprises (by impact)

1. **Squad quartet** (Partition/AssignRoles/AdvancePhase/AcquireSlot) — **triple-missing**: unlowered + exec-only pins (config unreachable) + no drawer. The FDP primitives exist; the graph layer is a façade. *(We already chose the lean `SlotRotation`+`MemberSlotList` path instead — so we avoid this.)*
2. **`Cast` broken** — emits a call into a nonexistent `Cast` type → invalid C#. A basic op, unguarded. Fix or avoid.
3. **`ArrayMake`/`ArrayGet` silent-wrong-value** — unlowered, and the data path yields `default` with **no diagnostic**. The most dangerous failure mode (compiles clean, wrong result). *(MemberSlotList avoids exposing raw arrays.)*
4. **`WaitForEvent` structurally broken** — no `EventTypeId` satisfies both validation and Roslyn; lowered as a channel. *(We use named `EventEntry` handlers for respond-to-event instead.)*
5. **Dispatchers unlowered** (`CallDispatcher`/`BindDispatcher`). *(We use a new `PublishEvent` node instead.)*
6. **Authoring gaps on working nodes** — `When` (forms stubbed), `ScoreDecision`/`ReadRankedResult`/`CallPeerBlueprint`/`CallCustomEvent`/`WaitForChannel` (no editing surface). These *run* but a designer can't configure them.
7. **Missing validators** — `ScoreDecision`/`ReadRankedResult`/`CallCustomEvent`/`Cast` accept bad references silently.
8. **`When` FallingEdge** deferred (TODO) — falling-edge behaviors silently never fire.

## Hill-attack build readiness (what the migration actually touches)

| Need | Nodes | Status |
|------|-------|--------|
| **Ready now** (compiler+authoring+tests ✅) | EventEntry, Return, FunctionCall, Branch, Sequence, Get/SetVariable, Literal, ChannelCommand, GetShared, SetShared | ✅ build on these |
| **Runtime-ready, authoring gap** | WaitForChannel (no drawer) | needs a drawer (P-editor) |
| **To build (new capability)** | P7 context-`FunctionCall`; **P1 `FlowForEach`** (⚠ NOT among the 30 compiler node kinds — editor scaffolding only, so P1 = *add the compiler node + lowering*, not wire an existing one); P2 `Read<Component>` catalog; P3 `GetSingleton`; P4 `PublishEvent`; P5 `SlotRotation`+`MemberSlotList` nodes | green-lit, unbuilt |
| **Deliberately avoid** (broken/unlowered — replaced by the above) | squad quartet, dispatchers, ArrayMake/ArrayGet, Cast, WaitForEvent | do not use |

## Build implications

- **P1 scope correction:** `FlowForEach` is *not* a compiler node yet (not in the 30 discriminators) — the "scaffolding exists" is editor-side. P1 = add the `Node` subtype + discriminator + Stage5 lowering (→ synchronous `foreach`) + validator (no latent in body) + a drawer. Bigger than "wire the existing scaffold."
- **Authoring is the true critical path.** Every new node (P2/P3/P4/P5) needs a real drawer, and several existing wired nodes (`WaitForChannel`, and `When` if used) need drawers built/finished. All editor-UI is Windows-verifiable-only — plan an explicit editor phase, mostly *wiring existing UIs* (catalog pickers, the predicate builder).
- **The safety net already pins the compiler gaps** (`NodeCoverageTests` asserts the BP4004 no-ops), so implementing any of them flips its characterization test — a built-in "you finished it" signal.
