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
| Subscribe | `WhenNode(EventFired)`: `EventTypeId` + `TargetFilter{None,Self}` + one `PayloadCondition` filter | fires `OnFired` but does **not expose the event's fields as output pins** — the subscriber can't *read* the payload |
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

### Q14-C — Subscribe substrate: world.Bus + WhenNode vs EventDispatcher
- **Lean:** custom events ride the **world.Bus** like system events; subscribe via **`WhenNode(EventFired)`**.
  Rationale: decoupled, cross-entity, any number of subscribers, no reference to the sender — the "one sends,
  many subscribe" shape. The event **TYPE is the identity** ("named" = the struct type, type-safe +
  discoverable) — do **not** add a parallel string-name registry (that reintroduces a manual registry).
  `EventDispatcher` stays for the explicit owner-scoped-callback case.

### Q14-D — Subscriber must READ the payload (build item) + addressing
- Today `WhenNode(EventFired)` only *filters* (one `PayloadCondition`); it exposes no field outputs.
- **Lean:** add **reflection-discovered output data pins** to `WhenNode(EventFired)` (one per event field,
  same attribute/baking) so the `OnFired` chain can consume the payload — the load-bearing missing piece for
  real communication.
- **Q14-D2 addressing:** `EventTargetFilter` is `{None, Self}` today (Self = "events aimed at me", None =
  broadcast). Is that sufficient for entity↔entity comms, or is an explicit **by-entity / by-correlation**
  filter needed now? **Lean:** Self + None initially; defer correlation unless required.

## What we're asking you to bless
1. **Q14-A:** `[BlueprintEvent]` reflection-discovery (mirror Q#12), baked field strings — and whether system
   events migrate too (Q14-A2).
2. **Q14-B:** generalize `PublishEvent` to reflection-sourced fields + make it editor-addable; confirm the
   no-ordering-hazard reasoning.
3. **Q14-C:** custom events on the world.Bus, subscribed via `WhenNode(EventFired)`, type-as-identity.
4. **Q14-D:** add reflected field-OUTPUT pins to `WhenNode(EventFired)`; addressing = Self/None for now (D2).
