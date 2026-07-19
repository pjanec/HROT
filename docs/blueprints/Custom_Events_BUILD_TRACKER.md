# Custom Events — Build Tracker

Design: `Custom_Events_Design.md` (APPROVED). This tracks the overnight autonomous build.
Legend: ⬜ todo · 🟡 in progress · ✅ done (committed) · ⏸️ deferred.

## Phase 1 — Discovery + authoring (foundation)
- ✅ **1a** `[BlueprintEvent(Category, DisplayName?)]` attribute (mirror `[BlueprintCallable]`)
- ✅ **1b** Reflection discovery of `[BlueprintEvent]` C# structs + fields (mirror `ISharedStructTypeProvider`) — the 2a path
- ✅ **1c** Editor-authored event-definition model + JSON asset (2b) — data model + round-trip
- ⬜ **1d** Event-definition authoring UI (mirror blackboard/variables authoring)
- ✅ **1e** Unified discovery (2a reflected + 2b asset) → single picker source
- ⏸️ **1f** Migrate system-predefined events to reflection (retire baked `PayloadFields`) — after publish path proven

## Phase 2 — Publish
- ✅ **2a** PublishEvent data-in pins from discovery (editor bakes `EventTypeFqn` + `(field,type)` onto node)
- ⬜ **2b** PublishEvent → generic carrier lowering (`PublishRaw`/`InjectIntoCurrentBySize`)
- ✅ **2c** PublishEvent editor-addable (palette entry per discovered event)

## Phase 3 — Dispatch (net-new core)
- ✅ **3a** Subscription registry: event **type-id → subscribers**, built at load (extend `[BlueprintRegistrar]` scan)
- 🟡 **3b** Dispatch pump — CORE DONE (bus ReadRawByTypeId + BlueprintEventDispatch helper + folded into BlueprintTickSystem, validated); remaining: event-identity keying so real events route
- ⬜ **3c** Payload marshalling: unstub `EmitEventThunk` (fill from carrier bytes via reinterpret-cast)
- ⬜ **3d** `Self`/`Any` recipient filter enforced at dispatch

## Phase 4 — Subscribe UX
- ⬜ **4a** `EventEntryNode` keyed by type-id + reflected data-out pins (payload)
- ⬜ **4b** `Self`/`Any` filter on the node + JSON (`TargetFilter`)

## Decisions / notes (autonomous judgment calls logged here)
- Keyed by event **type-id** (architect §7.1).
- Delivery = registry-indexed by type; pump gated by `HasEvent` (§7.2).
- Carrier = unmanaged/blittable only; strings via `FixedString32/64` (§7.3).
- Instance-dispatch blueprints first (§7.4).

## ⏸️ Checkpoint — author + publish HALF done; dispatch/subscribe runtime remains

**Done + committed + validated (the entire "author + publish" half):**
- **1a, 1b, 1c, 1e** — discovery + authoring-data foundation (attribute, C# reflection discovery, editor-def
  model + JSON, unified picker source).
- **2a** — `PublishEvent` baked custom-event fields → publishes without a catalog entry; proof suite 184/184
  byte-identical; compiles to a typed bus publish.
- **2c** — "Publish: {Event}" palette entries per discovered event (editor-addable).
- 10 unit tests, no regressions, full ClusterRunner builds 0 errors.

So a designer can now **author** a custom event (C# `[BlueprintEvent]` today; editor-def model ready) and
**publish** it from a blueprint, compiled. The `.bp.json`/proof gates are all green.

**Remaining = the "dispatch + subscribe" runtime half** (the net-new the architect flagged): **2b** generic
carrier (for editor-def events with no C# type), **3a–3d** the bus→Event-graph dispatch (registry + per-tick
pump + payload marshalling + Self/Any), **4a/4b** the `EventEntry` subscribe pins + filter, **1d** authoring UI,
**1f** system-event migration. These need **iterative runtime-scenario validation** (a live Instance blueprint
subscribing + receiving) — deliberately left for careful, attended work rather than a blind push. Precise
seam-level resume notes for each are above.

### Precise resume notes (per remaining slice)
- **2a — PublishEvent pin baking.** Add to `PublishEventNode` (Nodes.cs:617): `string EventTypeFqn` +
  `List<EventPayloadField> PayloadFields` (baked by the editor from `DiscoveredBlueprintEvent`). Extend
  `Stage0_Rehydrate.EnrichPublishEventPins` (Stage0:321) to use the baked fields when `EventTypeFqn` is set
  (else the existing catalog path). Gate: Stage0 pin test + a compile fixture publishing a `[BlueprintEvent]`
  struct via `--no-incremental` AI.Behaviors build. **Treat like the WaitForChannel schema change** (deterministic
  pins, round-trip, proof suite stays green).
- **2c — palette entries.** Mirror `BlueprintCallablePaletteEntries.Discover` → one `PublishEvent` + one
  `EventEntry` descriptor per `UnifiedEventDiscovery.All(...)` event, baking `EventTypeFqn`+fields at create.
  Register in `BlueprintEditorBootstrap.CreatePaletteRegistry`. Editor-only, testable. **Do after 2a** (else the
  created node has no payload pins for custom events).
- **2b — generic carrier lowering.** For 2b (no C# struct) events, Stage5 lowers `PublishEvent` to the type-erased
  bus path (`FdpEventBus.PublishRaw`/`InjectIntoCurrentBySize`, type-id = hash of the event name) packing fields
  into a blittable payload; 2a (real struct) keeps the existing typed `world.Bus.Publish(new T{…})`. New
  `IrOp_PublishCustomEvent` (or a flag on `IrOp_PublishBusEvent`). Novel — hands-on.
- **3a/3b/3c/3d — dispatch (the core net-new runtime; deepest slice — do attended + iteratively).** Seam
  detail (scoped): `BlueprintDefinition.EventHandlers` is `IReadOnlyDictionary<string, EventHandlerDelegate>`
  keyed by graph **name** → **re-key to event type-id (§7.1)**. `EventHandlerDelegate(Span<byte> stateBytes,
  ISimulationView view, IEntityCommandBuffer ecb, Entity self, float time, float dt, ReadOnlySpan<byte>
  payload)`. The pump is **2-dimensional**: (present event-types via `HasEvent`) × (entity-instances whose
  `def.EventHandlers` handle that type). Steps: (3a) type-id → subscribers registry from the
  `[BlueprintRegistrar]` scan / def keys; (3b) per-tick system near `BlueprintTickSystem` iterating entity
  blueprint slots, reading each present event (typed `Read<T>` or the untyped stream by type-id) and invoking
  the handler with the instance's state bytes + the event payload bytes; (3c) unstub
  `InstanceEmitter.EmitEventThunk` (currently `default(T)` — reinterpret the `payload` span onto the event/State
  struct); (3d) enforce Self/Any by comparing the event's Target field to the entity before invoking. Needs
  runtime behavioral tests (mirror `WhenNodeRuntimeTests`). Instance-dispatch first (§7.4).
- **4a/4b — subscribe UX.** `EventEntryNode` keyed by type-id + reflected data-out pins (payload) + the
  `Self`/`Any` `TargetFilter` (node UX + JSON per design §4.5).
- **1d — authoring UI.** ImGui event-definition editor reusing `BlackboardAuthoringWindow`; writes the
  `BlueprintEventCatalog` JSON. Hard to unit-test — needs an attended visual pass.
- **1f — migrate system events** to reflection (retire baked `PayloadFields`) — after the publish path is proven.

## Gate policy (every slice)
Build 0 errors; relevant unit tests; for compiler changes: `Hrot.AiEditor.Generators.Tests` clean-rebuilt +
serial stays green (183/184-class baseline); full ClusterRunner build before finishing.

## Session-2 progress (dispatch runtime)
Done + committed + validated this session (on top of the author+publish half):
- **3a** subscription registry (type-id → subscribers), unit-tested.
- **3b CORE**: `FdpEventBus.ReadRawByTypeId(typeId, out elemSize)` + `HasEvent(int)` (engine core, architect-
  approved, additive); `BlueprintEventDispatch.DispatchForSlot` (invokes handlers with the raw payload span,
  HasEvent-gated; resolver FQN→[EventId].Id/hash) — **validated against a real bus** (publish → handler runs
  with payload=42); folded into `BlueprintTickSystem`'s 3 per-entity tiers (no-op for non-subscribers, 22
  tick tests green). Full ClusterRunner builds 0 errors.

**The one remaining wiring for real dispatch (next):** the `EventHandlers` dict key must be the event
**FQN** (my resolver turns FQN→type-id correctly), but today the emitter keys it by the Event graph's **name**
— which is also used as a C# method-name suffix (`Event_{Name}`) so it can't hold dots/FQN. So the event
identity must be plumbed: `EventEntryNode.EventTypeId` → a new `IrGraph.EventTypeFqn` (set in Stage5) →
`CSharpEmitter` keys `EventHandlers` by it + `InstanceEmitter.EmitEventThunk` reinterprets `payload` as
`global::{EventTypeFqn}`. This is coupled with **4a** (the subscribe node that sets `EventTypeId`) and wants a
full compile-and-run integration test (Instance bp w/ Event graph → publish → dispatch → assert handler ran).
Then **3d** Self/Any (needs the target-field offset threaded), **2b** carrier, **1d** UI, **1f** migrate.

**Bottom line:** the dispatch *mechanism* is proven end-to-end at the helper level; the remaining work is the
compiler event-identity plumbing + the subscribe node + the full-pipeline integration test — a focused coupled slice.

## Session-3 progress (dispatch wiring + subscribe-compiler validated)
Every link of the publish→dispatch→subscribe chain is now validated at the piece level (and they compose by
construction — the emitter produces the exact thunk the dispatch helper invokes with the payload it reads):
- **3b-wiring** (committed): event identity plumbed — `IrGraph.EventTypeFqn` ← `EventEntryNode.EventTypeId`
  (Stage5); `CSharpEmitter` keys `EventHandlers` by the FQN; `InstanceEmitter` thunk reinterprets the payload
  as `global::{FQN}` + passes fields. **Validated** by `EventGraphEmitTests` (compile → keyed by FQN + thunk
  marshals `__ev.Value`). Proof suite 184/184 byte-identical.
- **Stage2 BP1400 relax** (committed): a fully-qualified custom-event identity on an `EventEntry` is accepted
  (baked, unverifiable — mirrors PublishEvent); non-FQN typos still error.
- Builder gained `Entry(eventTypeId?)` + `WithInput` (minimal Event-graph test support).

**Remaining:**
- **Capstone integration test** (ties it together end-to-end): `fixture.CompileAndLoad(eventBp)` →
  `SpawnAndAttach` → `World.Bus.Publish(new TheEvent{...})` → `harness.Pump(1)` (TickFrame SwapBuffers →
  BlueprintTickSystem dispatches → handler runs) → `ReadIntField` asserts. Needs the Event-graph body to write
  observable state — i.e. `SetVariable`-with-a-wired-value (a Literal wired into the SetVariable value pin);
  the builder's `SetVariable` doesn't wire a value yet, so add that (or a small manual-wire helper).
- **3d** Self/Any filter at dispatch (needs the target-field byte offset threaded to `DispatchForSlot`).
- **2b** generic carrier lowering (editor-authored events with no C# struct).
- **4a/4b** the editor subscribe node UX (`EventEntry` reflected payload pins + Self/Any `TargetFilter`).
- **1d** authoring UI; **1f** system-event migration.

**Bottom line:** the runtime dispatch + both compiler ends (publish + subscribe) are built and each validated;
what's left is the capstone integration test, the Self/Any refinement, the 2b carrier, and the editor UX.
