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
- ⬜ **3b** Dispatch pump: per-tick system, `HasEvent`-gated, iterates only present event-types
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
