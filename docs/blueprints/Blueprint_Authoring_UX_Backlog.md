# Blueprint / BTree Authoring — UX Improvement Backlog

> **Purpose:** a running list of ideas to make the variable / scope / shared-state authoring system
> usable by non-programmers. **Ideas only — not commitments.** Each becomes a task when picked up.
> **Feed:** anything that trips up an author (incl. findings from the *Platoon Hill-attack →
> blueprints* migration, which will expose what's missing to replicate complex behavior internals).
> Keep entries terse.

## Vocabulary (agreed 2026-07-16)

The four intent-first "kinds" of node memory, and their underlying settings:

| # | Name | Role | Scope |
|---|------|------|-------|
| ① | **Parameter** | Input | — |
| ② | **Private scratch** | State | Node |
| ③ | **Behavior shared** | State | Behavior |
| ④ | **Squad shared** | State | Entity |

"Squad" = a commander + its subordinate roster (command hierarchy), not a generic entity list.

## Ideas

### UX-1 — Intent-first authoring picker **[high impact]**
Replace the raw Role + Scope combos with one question — *"What is this memory?"* — whose options are
the four names above. Picking one sets Role+Scope underneath. Raw combos move behind an "Advanced"
toggle. Collapses four implementation axes to one intent choice for the common case. *(Needs an
architect nod — it's a real change to the Blackboard Variables panel.)*

### UX-2 — Unify the "two doors" to shared state **[high impact]**
Today kind ④ has two unrelated UI surfaces for the same storage: a node's **Scope = Entity**, and
explicit **Get Shared / Set Shared** graph nodes. Decide whether to present them as one concept /
entry point, or sharply delineate when each is used. *(Needs an architect decision.)*

### UX-3 — Progressive disclosure
Hide Scope, Get/Set Shared, and byte-budget UI until the author opts into sharing. A private-node
action should show nothing about scope or sharing.

### UX-4 — In-context micro-explanations
One-line "what / when" next to each choice (build on the existing `StaticVsDynamicTooltip` pattern);
label the memory-budget bar in intent terms, not raw bytes.

### UX-5 — Graph-level visual cues
Color / badge a node by its sharing scope so "this touches shared state" is visible at a glance in
the graph, not just in a panel.

## Documentation (illustrated)

### DOC-1 — Designer quickstart ✅ shipped
`Variables_Designer_Quickstart.md` — intent-first decision tree (D4). Mermaid.

### DOC-2 — Higher-level architecture overview **[next]**
The missing "big picture." Illustrated (SVG): how **blueprints relate to BTrees**; what **actions vs
conditions** are; that a node has **Parameters** (set once) and **Working State** (per-tick). This is
the orientation a newcomer needs *before* the D4 tree.

### DOC-3 — Memory-layout schematic (D2)
To-scale SVG: where data physically lives — `BehaviorParameters` (Params) vs `BlueprintBlackboard`
partition slots (Working State), keyed by scope. The engineer's "aha."

### DOC-4 — Lifetime timeline (D3)
SVG: assign-behavior → tick → tick → switch-behavior — *when* Params sync in, how long each scope's
State survives. Answers "why did my value reset / not reset?"

> **Diagram medium:** SVG for DOC-2/3/4 (richer pictures; Mermaid clips labels and lays out
> awkwardly). Mermaid only for simple flowcharts like D4.

## Capability gaps (from the Hill-attack migration)

Discovered by rebuilding real behavior as blueprints. Full detail + slice mapping in
`HillAssault_Blueprint_Migration.md`. Candidates until a slice confirms each:

- **GAP-1 — no loop / iteration node** (ForEach / While / counted Repeater). Biggest gap.
- **GAP-2 — read a foreign entity's ECS component** (not just a shared slot).
- **GAP-3 — publish arbitrary engine events** (beyond the 3 CQRS channels).
- **GAP-4 — roster fan-out** (iterate `UnitRoster`, publish N per-subordinate orders) — squad
  primitives may cover; verify.
- **GAP-5 — bitmask / SoA-array working state** + array-set / bit-op vocabulary.
- **GAP-6 — in-place param mutation** → migrate to working-state var (refactor, not a true gap).
