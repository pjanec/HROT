# Architect question #14 — designer-authored custom events + pub/sub (reflection-discovered, no manual registry)

**Status: 🟡 DRAFT — pending architect.**

## The need (two cases)

1. **System-predefined events** — engine-defined event structs (the current `BuiltInEngineEventCatalog`:
   `HitEvent`, montage/nav/behavior events, `AssignTacticalIntentEvent`, …). Publish + subscribe already
   work (`PublishEvent` → `world.Bus`; `WhenNode`/`EventFired` to receive).
2. **User-authored custom events** — designer-defined event structs for **entity↔entity or intra-entity
   communication: one behavior sends, any number subscribe (named events).**

**Hard requirements from the user:**
- Custom event **structs authored in C#** (NOT an in-editor struct designer) — that's accepted.
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
- **Addressing (D2):** cross-entity — "event aimed at entity X" vs broadcast. Is per-event Target/correlation
  filtering needed on the entry, or is subscribe-by-type + read-the-`Target`-field-in-graph sufficient?
  **Lean:** subscribe-by-type; the entry reads the event's Target field to self-filter; defer richer
  correlation unless required.

## What we're asking you to bless
1. **Q14-A:** `[BlueprintEvent]` reflection-discovery (mirror Q#12), baked field strings — and whether system
   events migrate too (Q14-A2).
2. **Q14-B:** generalize `PublishEvent` to reflection-sourced fields + make it editor-addable; confirm the
   no-ordering-hazard reasoning.
3. **Q14-C:** subscription = a named-event `EventEntryNode` (NOT WhenNode); payload = its reflected data-out
   pins; event type = identity.
4. **Q14-D:** the bus→Event-graph dispatch/routing (the net-new runtime piece) — confirm it doesn't already
   exist and bless the registration+pump sketch; addressing = subscribe-by-type + read the Target field (D2).
