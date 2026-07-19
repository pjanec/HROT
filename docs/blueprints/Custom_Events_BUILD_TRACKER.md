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
- ⬜ **2a** PublishEvent data-in pins from discovery (editor bakes `EventTypeFqn` + `(field,type)` onto node)
- ⬜ **2b** PublishEvent → generic carrier lowering (`PublishRaw`/`InjectIntoCurrentBySize`)
- ⬜ **2c** PublishEvent editor-addable (palette entry per discovered event)

## Phase 3 — Dispatch (net-new core)
- ⬜ **3a** Subscription registry: event **type-id → subscribers**, built at load (extend `[BlueprintRegistrar]` scan)
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

## Gate policy (every slice)
Build 0 errors; relevant unit tests; for compiler changes: `Hrot.AiEditor.Generators.Tests` clean-rebuilt +
serial stays green (183/184-class baseline); full ClusterRunner build before finishing.
