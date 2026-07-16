# Architect question #2 — generic primitives so non-programmers can author worst-case behaviors

**Thank you for the Q0/GAP-1/3/7 answers — we've adopted the ones that stand as-is** (context-aware
`FunctionCall` with trailing `self`/`view`; `[SharedAiAction]` event publish + deferred cross-entity
bus). This follow-up is a **reconciliation**, not a disagreement: we think a couple of the answers, if
taken literally, quietly defeat the one requirement blueprints exist to satisfy — and we think your
*own* design philosophy already points at the fix.

## The requirement that reframes everything

Blueprints exist so that **non-programmers author behaviors** — including complex ones. Our benchmark
is the full **Platoon Hill-attack** (roster fan-out, wave/slot allocation, per-subordinate polling,
EQS, cross-entity orders). If a designer can't build *that* in blueprints, blueprints haven't met
their purpose. "Leave the hard parts in C#" is only acceptable if the designer never has to touch
those parts to build or tweak a behavior.

## The tension with the strict reading

"Push complexity into C# leaf-helpers" is right — **but only if those helpers are generic, reusable,
engine-public-API, authored once.** The strict reading ("write `DispatchAllToBaseline(roster)` in
C#", "no loop node — put the loop in a helper") produces a **bespoke C# helper per behavior**. That
puts a programmer back in the loop for *every* behavior a designer wants — which is precisely the
outcome blueprints are meant to eliminate. A designer cannot write `DispatchAllToBaseline`.

## The reconciliation — and it's *your* pattern already

You already solved a hard construct exactly the right way: **EQS**. The semantic query stays in a
hand-written C# `[EqsTemplate]`, but the designer orchestrates it with **generic, reusable nodes**
(`SpawnEqsSensor` / `ReadEqsResult`) — authored once, usable in any behavior, no per-behavior C#.

We're asking to apply that *same* "generic node fronts C# complexity" treatment to the remaining hard
patterns, under a principle we'd call **curated-generic**: generic across behaviors, but constrained
in surface (attribute-gated, read-only where appropriate, no arbitrary ECS mutation, no unbounded or
latent loops). This is *not* the fully-general visual language you rightly reject — it's the EQS /
event-catalog / restricted-type-system philosophy, applied consistently.

## Three asks (where we'd extend past the strict answers)

### A1 — Curated `GetComponent<T>` (read-only) — **the crux**
You ruled out a *general* `GetComponent` and flagged direct ECS *mutation* as a validator error. We
fully agree on mutation (writes stay in `[SharedAiAction]` / the command buffer). But **reads are
different**, and a designer must be able to read ECS state (a condition like "do I have a target?"
needs `TargetMemory`; "are subordinates at the baseline?" needs each one's `NavigationStatus`).
Proposal: a **read-only, typed `GetComponent<T>` restricted to components tagged
`[BlueprintReadable]`** — a curated allow-list, exactly like the event catalog and the EQS surface.
Not general ECS access; a vetted read window.
*Question:* will you approve an attribute-gated, read-only component read (self and — see A4 — foreign
entity)? If not, how *should* a non-programmer read component state at all? (The context-aware
`FunctionCall` you approved still needs a programmer to write each read helper — same bespoke-per-
behavior problem.)

### A2 — Structured, bounded `ForEach`
Your loop objection was topological-sort breakage + the latent-in-loop state-machine nightmare. Both
are real **for unstructured, back-edge loops**. They do **not** apply to a **structured `ForEach`
node** whose body is a nested sub-DAG, lowered to a plain synchronous C# `for` around the body's
emitted statements, with a validator rule **"no latent nodes inside a `ForEach` body."** No back-edge
in the exec graph → topological sort intact. Synchronous single-tick body → no cross-frame iterator
persistence, no resumable state machine. Iteration over a bounded source (a roster's 16 slots, EQS
results) is the single most common thing a designer needs.
*Question:* will you sanction a structured bounded latent-free `ForEach` as the one iteration
primitive (general `While` / back-edges remain forbidden)?

### A3 — Implement the squad slot/role/phase primitives
The `PartitionElements` / `AssignRoles` / `AcquireSlot` / `AdvancePhase` nodes already exist in the
schema but have no Stage-5 lowering (they're no-ops today). They appear purpose-built for exactly the
Hill-attack's slot/wave/role coordination. Implemented generically, they let the messy bitmask/SoA
bookkeeping live *inside* an engine primitive (with a public state struct the blueprint declares as
WorkingState) instead of in designer-authored logic.
*Question:* approve implementing these as the generic slot/wave/role mechanism? Any intended semantics
we should preserve?

### A4 — Foreign-entity read safety
A2/A1 combine for "for each subordinate, read its `BehaviorState`/`NavigationStatus`." That's a
**read-only** cross-entity component access within a tick.
*Question:* is read-only foreign-component access safe within the Simulation tick (reads don't touch
the parallel-write model), or must even reads be routed/deferred?

## Why this matters concretely

We mapped all 12 Hill-attack nodes to a primitive family (`ForEach`, curated `GetComponent`/
`GetSingleton`, `PublishEvent`, squad slot/role/phase, math library, context-aware `FunctionCall`).
**The family covers 100% of the worst case — but A1, A2, A3 are load-bearing.** Without them, roughly
half of Hill-attack can only be expressed as bespoke per-behavior C#, which fails the non-programmer
goal. With them, a designer composes the whole thing from generic, vetted, reusable nodes — complexity
still in C#, but authored *once* by the engine team, exactly as EQS already is.

## What we need decided

A1 (curated read — most important), A2 (structured `ForEach`), A3 (squad primitives), A4 (foreign
read). If you approve the curated-generic principle, we'll build in the order P7/P4 → `ForEach` →
`GetComponent` → `GetSingleton` → squad, validating each against the specific Hill-attack node it
unlocks.
