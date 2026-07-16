# Architect question — three blueprint capabilities the Hill-attack migration needs

**Context.** We're rebuilding the Platoon Hill-attack behavior as visually-authored blueprints, slice
by slice, to find what's missing (`HillAssault_Blueprint_Migration.md`). Slices 0 (a trivial
always-Success action) works end to end. Scoping the real nodes surfaced three capability gaps that
gate everything else. All were confirmed against the code (`Stage5_Schedule.cs`, `Nodes.cs`, the
existing `.bp.json` demos) and are backed by a new reflection-driven node safety net.

A cross-cutting question first, because it shapes the other three:

**Q0 — Target end-state.** Blueprintizing ECS-heavy nodes keeps landing on "a `FunctionCall` to a
preserved C# helper," because reading components, looping, and publishing events are all C#-shaped.
Is the intended end-state (a) **blueprints as orchestration over C# leaf-helpers** (invest little in
new node vocabulary; helpers do the ECS work), or (b) **fully visual** (build out `GetComponent`,
loop, event nodes so authors never write C#)? This decides how much of the below is "add a thin
escape hatch" vs "build real node kinds."

---

## GAP-7 — ECS-read access in graphs  *(foundational; blocks ALL conditions)*

**Problem.** A graph can read only its own blackboard (Params / WorkingState / Variables) plus
implicit-`self` accessor nodes (`ChannelCommand`, `GetShared`). There is **no** way to read a
component on `self` (e.g. `TargetMemory`, `NavigationStatus`, `BehaviorState`), read a world singleton
(e.g. `NetworkEntityMap`), or hand `self`/`world` to a `FunctionCall` helper. So even the
`SquadRallyStateOps`-style escape hatch can't help — the helper can't receive `self`/`world`.
Consequence: `Condition_HasTarget/AreAllAtBaseline/IsWaveCompleted/IsAreaQueryResolved` are all
unexpressible today.

**Options.**
1. **Context-aware `FunctionCall`** — the callee implicitly receives ambient `self`/`world` (lowers to
   `Helper(args…, self, world)`). Smallest change; keeps ECS logic in reviewable C#; immediately
   revives the condition family + the escape hatch. *(our recommended first step)*
2. **Dedicated `GetComponent<T>(self)` / `GetSingleton<T>()` source nodes** (+ a foreign-entity form,
   which would also close GAP-2 cross-entity component read). More visually native; larger build
   (node kinds, pin typing, validation, editor).
3. **`Self` / `World` source nodes** feeding existing `FunctionCall` pins — minimal vocabulary, but
   leaks raw `EntityRepository` into graphs.

**Questions.** Which approach (or 1 now, 2 later)? If 1: how does a call opt in — a node flag, or a
convention that trailing `Entity self` / `EntityRepository world` params are auto-bound? Read-only
component access only, or allow RW (conditions need only read; mutations go through channels anyway)?
Any ECS-access / parallel-execution safety rules to enforce?

---

## GAP-1 — loop / iteration  *(blocks roster fan-out + the wave loop)*

**Problem.** No `ForEach` / `While` / counted `Repeater`. The commander's `Repeater(-1)` wave loop and
every `for (i in UnitRoster)` fan-out (`DispatchAllToBaseline`, `IsWaveCompleted`, slot scans) have no
blueprint construct.

**Questions.** Do you want a real loop node in blueprints, or should iteration stay inside C#
`FunctionCall` helpers (blueprints remain per-tick straight-line)? If a node: a **bounded `ForEach`**
over a fixed-capacity source (e.g. `UnitRoster`'s 16-slot array) covers the common fan-out; a general
`While` is riskier (termination, latent nodes inside a loop). How should a loop interact with latent
nodes (Wait/Delay) — forbid latent inside loop? *(our recommendation: bounded `ForEach` over an
array/roster source, pairs with GAP-7's component read; defer general `While`; unbounded/complex
iteration stays in helpers.)*

---

## GAP-3 — publish engine events  *(blocks order dispatch + ClearBehavior)*

**Problem.** `ChannelCommand` targets only the 3 CQRS channels. The `CallEventDispatcher` /
`BindEventDispatcher` nodes exist in the schema but have **no `Stage5_Schedule` lowering** (they hit
the `default:` branch → BP4004 warning → dropped). The commander publishes `AssignTacticalIntentEvent`
per subordinate; the tank publishes `ClearBehaviorEvent`.

**Questions.** Should blueprints publish arbitrary engine events, gated by a catalog (the
`[BlueprintExposedEvent]` attribute sketched as F1 in the Slice-2 candidates)? Implement the existing
`CallEventDispatcher` lowering, or add a distinct `PublishEvent` node keyed by a catalog entry?
Cross-entity publish (commander → subordinate order) must cross a frame boundary via the deferred
event bus per your earlier ruling — confirm the shape (a generic `BlueprintDeferredEvent`?).
*(our recommendation: a catalog-gated `PublishEvent` capability; cross-entity via the deferred bus.)*

---

## Suggested priority

**GAP-7 → GAP-3 → GAP-1**, matching the slice ladder: GAP-7 unblocks the condition family (and the
helper escape hatch) with the smallest change; GAP-3 unblocks retreat/dispatch actions; GAP-1 unblocks
the roster/wave core last. Answering **Q0** first tells us how far to push each.
