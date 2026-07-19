# Architect question #14 — designer-authored custom events + pub/sub (reflection-discovered, no manual registry)

**Status: ✅ APPROVED (architect) — Option 2b + full infrastructure.** Recipient-filtering (D2) refined with
user sign-off to a declarative **Self/Any filter on the entry node** (spec in Q14-D).

- **Q14-E APPROVED — Option 2b** (editor-authored, generic blackboard-style payload, **NO codegen**). Keep 2a
  (C# hand-authored typed struct) as a demand-driven fallback for advanced users needing a C#-consumable event.
- **Q14-A APPROVED** — `[BlueprintEvent]` reflection-discovery mirroring Q#12 (editor reflects fields; compiler
  bakes `EventTypeFqn` + field strings); retires the hand-maintained `PayloadFields` registry. **Q14-A2
  APPROVED:** migrate the system-predefined events to the same reflection mechanism (one source of truth).
- **Q14-B APPROVED** — generalize `PublishEvent` to reflection-sourced pins + make it editor-addable; the
  single-struct-initializer emit confirms **no write-ordering hazard**.
- **Q14-C CONFIRMED** — subscription primitive = named-event `EventEntryNode` (NOT `WhenNode`); payload
  projects onto its reflected data-out pins.
- **Q14-D CONFIRMED (net-new)** — build the bus→Event-graph dispatch: registration-by-event-type at load +
  a per-tick bus pump routing each event to matching entry graphs. **D2 addressing — refined:** the recipient
  check is a **declarative `Self`/`Any` filter on the entry node** (reusing `WhenNode`'s `EventTargetFilter{
  None, Self}`), enforced by the pump — **not** an in-graph branch. Only genuinely complex/multi-field
  correlation stays deferred (a single-field self-check is not "complex correlation").

Cleared to implement (pending the dispatch/runtime trace). Original proposal follows.

## The need (two cases)

1. **System-predefined events** — engine-defined event structs (the current `BuiltInEngineEventCatalog`:
   `HitEvent`, montage/nav/behavior events, `AssignTacticalIntentEvent`, …). Publish + subscribe already
   work (`PublishEvent` → `world.Bus`; `WhenNode`/`EventFired` to receive).
2. **User-authored custom events** — designer-defined event structs for **entity↔entity or intra-entity
   communication: one behavior sends, any number subscribe (named events).**

**Hard requirements from the user:**
- **Designers must be able to author custom events** (the primary need); C# hand-authoring is a nice-to-have
  for advanced users, NOT a hard requirement. The user is open to a single approach — see **Q14-E** for the
  three options + the recommendation (**2b: editor-authored, generic payload, no codegen**). The originally-
  floated "editor defines → generate a C# struct" (codegen) is the high-risk leg and the recommendation drops
  it.
- The **editor must discover the struct's fields by reflection — NOT a hand-maintained registry.**
- Ideally a **multi-pin setter** (one pin per field) for publishing. The user flagged the multi-pin
  **write-ordering** worry (the deferred `SetShared`-writes concern, #13).

## What exists today

| Piece | Mechanism | Gap for case 2 |
|---|---|---|
| Publish | `PublishEvent` node → `IrOp_PublishBusEvent` → `world.Bus.Publish(new T{…})` | fields come from a **hand-baked** `EngineEventCatalogEntry.PayloadFields` (the manual registry the user rejects); also no palette entry / drawer (asset-authored only) |
| Subscribe | **`EventEntryNode`** — a named-event graph entry keyed by `EventTypeId`; its data-out pins expose the payload (today matched against `Graph.Inputs`). This is the subscription primitive (per the user), NOT `WhenNode` (which is a reactive in-graph condition). | the bus→Event-graph **dispatch** that fires a subscriber's entry graph on a published event is not evident in code (likely net-new, Q14-D); and the entry's field pins come from manual `Graph.Inputs`, not the event struct's reflected fields (Q14-A) |
| Delegate pub/sub | `EventDispatcher` (`Bind`/`Call`), `CustomEvent` (`CallCustomEvent`) | owner-scoped / intra-blueprint — not decoupled cross-entity broadcast |
| Field reflection | editor `NodePinSchema` reflects loaded game assemblies (net8 host); `[BlueprintCallable]` (Q#12) already discovers CLR members this way | — |

## Key clarification (resolves the user's ordering worry)

**A multi-pin publish setter has NO write-ordering hazard** — unlike multi-pin `SetShared` writes (#13). Publish
emits a **single struct initializer** `new EventT { f1 = <pin>, f2 = <pin>, … }` — one pure construction
expression, evaluated once; field assignment order is irrelevant and there is no interleaving with impure
state. (Today's `PublishEvent` is already exactly this shape.) So the multi-pin publish node is safe to build;
the ordering concern that gates `SetShared` does not apply.

## Reuse map — custom-event editing ≈ shared-state editing (already shipped)

The user's instinct is correct: editing a custom event in the editor is nearly identical to editing
`GetShared`/`SetShared`, and the machinery is directly reusable — **including JSON storage**.

| Concern | Shared-state (exists) | Custom-event (reuse) |
|---|---|---|
| Type discovery | `ISharedStructTypeProvider` / `ReflectionSharedStructTypeProvider` — scans loaded assemblies for value types marked `[BlackboardDtoStructAttribute]` (attribute + reflection, **no manual registry**) | same provider pattern over `[BlueprintEvent]` structs (or a shared marker) |
| Field editing | `Get/SetSharedNodeDrawer` reflects the struct's fields → per-field pins; a filtered "Type FQN" picker | same drawer/picker, injected with the event-type provider |
| JSON storage | the node bakes `SharedTypeId` (FQN) + `VariableId` + wired field pins; the **struct stays in C#** (reflected), only the FQN reference + field wiring live in the `.bp.json`. Round-trip proven by `SharedNodeCommandSinkAndPersistenceTests` | identical: bake `EventTypeFqn` + wired field pins; no struct definition in JSON |

So the discovery-provider + drawer + filtered-picker + baked-FQN JSON model is **proven in the codebase**
(shared state) and should be lifted, not reinvented. This also reinforces Q14-A: reflection-via-attribute
discovery already exists in **two** places — `[BlackboardDtoStructAttribute]` (shared structs) and
`[BlueprintCallable]` (Q#12 CLR helpers).

## Sub-questions

### Q14-A — Discovery: reflection-via-attribute (kill the manual registry)
- **Lean:** mirror the **approved Q#12 `[BlueprintCallable]`** pattern with a `[BlueprintEvent(Category)]`
  attribute on the C# event struct. The **editor reflects its public fields** (existing `NodePinSchema`
  reflection) to build the picker + publish/subscribe pins; the **compiler bakes** the discovered
  `EventTypeFqn` + per-field `(Name, TypeId)` strings into the `.bp.json` node — exactly how FunctionCall /
  ChannelCommand / BlueprintCallable already bake, because the netstandard2.0 generator cannot reflect game
  assemblies. This **eliminates the hand-maintained `PayloadFields`** the user objects to.
- **Q14-A2:** do system-predefined events **also migrate** to reflection (retire the hand-baked catalog), or
  keep the catalog for engine events + reflection for custom? (Unifying is cleaner but touches shipped events.)
- **Reuse:** Q#12 discovery + `PublishEvent` field-pin lowering + `NodePinSchema`. **Build:** the attribute +
  an event-discovery pass + baking event-field strings.

### Q14-B — Send: multi-pin publish node
- **Lean:** generalize the existing `PublishEvent` node — source its data-in pins from reflection (Q14-A)
  rather than the baked catalog; add the palette-entry-per-event + drawer (the known authoring gap) so it is
  editor-addable, not JSON-only. No new lowering — the single-struct-initializer emit already exists.
- **Ordering:** none (see clarification above).

### Q14-C — Subscribe = a NAMED-EVENT ENTRY node (not WhenNode) [user-directed]
- **Correction (user):** the subscription is a **named-event entry node** in the graph — an event-handler
  entry (`EventEntryNode`, keyed by `EventTypeId`; the existing Event-graph primitive), **NOT `WhenNode`**.
  When the named event fires, execution **enters at that node**. Any number of blueprints/graphs may declare an
  entry for the same event type ⇒ "one sends, many subscribe." The event **TYPE is the name** (discoverable,
  type-safe — no parallel string-name registry).
- **Payload = the entry node's data-out pins**, reflection-discovered from the event struct's fields (Q14-A).
  This reuses `EventEntryNode`'s existing "one data-out per `Graph.Inputs`" pin projection — retargeted at the
  event struct's reflected fields instead of manually-declared `Graph.Inputs`. So reading the payload IS the
  entry node's outputs — **no `WhenNode` field-output work needed** (this supersedes the earlier Q14-D lean).
- `WhenNode` stays for reactive in-graph conditions (value-changed / condition-met); `EventDispatcher` for
  explicit owner-scoped callbacks. Neither is the custom-event subscription primitive.

### Q14-D — Dispatch/routing: how a published event reaches subscribers' entry graphs [the real runtime work]
- The substantive open question: when `PublishEvent` puts event T on the `world.Bus`, how does the runtime
  **invoke the named-event entry graph of every subscribed blueprint** (cross-entity and intra-entity)?
  Sketch: registration-by-event-type at load (each blueprint advertises which event types its entry graphs
  handle) + a per-tick bus pump routing each event to the matching entry graphs.
- **Verify:** whether a bus-event → Event-graph dispatch path **already exists**. `EventEntryNode` +
  `GraphKind.Event` exist as primitives, but a search did not surface the routing that actually *fires* an
  Event graph when a bus event arrives — so this is likely the load-bearing **net-new runtime piece**. (The
  editor / JSON / discovery all reuse — see the reuse map.)
- **Addressing (D2) — RESOLVED: declarative `Self`/`Any` filter on the entry node.** There are two orthogonal
  filter axes; the entry node handles both declaratively so the graph never branches:
  - **Name/type** — inherent: the entry node is keyed to one event type; it only fires for that event. No
    in-graph name filtering.
  - **Recipient** — an optional `Self`/`Any` toggle (reuses `WhenNode`'s `EventTargetFilter{None, Self}`):
    - `Self` (default) → fires **only when** `event.<TargetField> == this entity`.
    - `Any` (= `None`) → broadcast; fires regardless of target.

  Node UX:
  ```
  ┌─ ◈ On TargetSpotted ───────────┐   ← entry node; event TYPE = the name (auto name-filter)
  │  Deliver to:  ◉ Self   ○ Any    │   ← shown only if the event declares a Target field
  │                           exec ▶ │   ← execution enters here on fire
  │                       SpotterId ● │   ← payload → data-out pins (reflection/def-discovered)
  │                        Position ● │
  │                          Target ● │   ← Target field still readable if the graph wants it
  └─────────────────────────────────┘
  ```
  - **Target field** is inferred from the event definition (the field tagged as recipient — the `TargetFieldName`
    / an `[EventTarget]` marker); the designer only flips `Self`/`Any`, never picks the field.
  - **JSON:** `{ "kind": "EventEntry", "EventTypeId": "TargetSpotted", "TargetFilter": "Self" }`
    (`"None"` = Any/broadcast — existing enum value).
  - **Enforced at dispatch:** the per-tick pump delivers to this entry graph iff `TargetFilter == None` **or**
    `event.<TargetField> == thisEntity` — no `Branch`/`Compare` in the graph.
  - **Edge case:** event with no target field → toggle hidden, always delivered (broadcast-only event).
  - **Deferred:** complex/multi-field correlation (arbitrary field matching) → in-graph branch, only if a
    future case needs it.

### Q14-E — Authoring approach [the key decision — recommendation below]
Precedent correction: **C# reflection-discovery is proven** (`[BlueprintCallable]` Q#12 + shared-state); the
**only unprecedented/high-risk piece is editor→C# code generation.** "Editor-authored saved in JSON" is proven
*specifically* via the **blackboard model** (designer-defined fields + a generic byte-addressed payload, **no
codegen**). Three options for designer-authored custom events:

| Option | Serves designers? | Precedent / reuse | Risk |
|---|---|---|---|
| **2a — C# hand-authored (typed struct, reflection)** | ✗ needs a programmer per event | ✓ `[BlueprintCallable]` / shared-state | low |
| **2b — Editor-authored, generic payload (blackboard-style), NO codegen** | ✓ | ✓ blackboard authoring UI + byte-payload + JSON | medium |
| **2c — Editor-authored + codegen (typed struct)** | ✓ | ✗ no editor→source-gen precedent | high |

**Claude's recommendation: 2b (drop codegen; keep 2a as a cheap demand-driven later add).** Rationale: 2b is
the only option that BOTH serves designers (the point) AND reuses proven machinery — blackboard already does
designer-authored `(Name, FieldType)` fields (`BlackboardAuthoringWindow` / `BlueprintVariablesWindow`) + a
generic byte-addressed payload + JSON storage, with no generator and no build-step coupling. 2c's codegen is
the only scary part and it is avoidable. 2a alone can't serve designers; keep it available (it's nearly free
via the Q14-A reflection path) for advanced users who later need a **C#-consumable typed** custom event.

- **Honest tradeoff of 2b:** an editor-authored event is a **generic bus payload, not a real C# struct**, so
  arbitrary C# systems can't consume it as a typed event. That is fine for the stated use case
  (behavior↔behavior comms). System-predefined events (case 1) stay typed as today.
- **New runtime work under 2b:** a **generic event carrier on the bus** — `PublishEvent` packs the wired
  field pins into the carrier; the named-event entry node unpacks them into its data-out pins — plus the
  dispatch/routing (Q14-D). Both reuse the blackboard byte-addressing model. No codegen.
- **E-storage:** editor-authored event definitions live in an asset/JSON (mirroring how blackboard/variables
  persist); the picker lists them alongside any reflected C# (2a) events.
- **E-guardrails:** renaming/removing a field must surface as a break in dependent blueprints (mirror Q#12's
  deleted-member validation).

**Architect: please confirm 2b (or state a preference for 2a-only / 2c), so we don't build the codegen leg
speculatively.**

## What we're asking you to bless
0. **Q14-E (the key decision):** authoring approach — **recommendation is 2b** (editor-authored, generic
   blackboard-style payload, **no codegen**); confirm, or choose 2a-only / 2c. This gates whether the
   high-risk codegen leg gets built at all.
1. **Q14-A:** `[BlueprintEvent]` reflection-discovery (mirror Q#12), baked field strings — the convergence
   point for BOTH authoring paths; and whether system events migrate too (Q14-A2).
2. **Q14-B:** generalize `PublishEvent` to reflection-sourced fields + make it editor-addable; confirm the
   no-ordering-hazard reasoning.
3. **Q14-C:** subscription = a named-event `EventEntryNode` (NOT WhenNode); payload = its reflected data-out
   pins; event type = identity.
4. **Q14-D:** the bus→Event-graph dispatch/routing (the net-new runtime piece) — confirm it doesn't already
   exist and bless the registration+pump sketch; addressing = subscribe-by-type + read the Target field (D2).
