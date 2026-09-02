# ADA-BATCH-04 Review (Group F — commands + discovery + spawn)

**Verdict:** ACCEPTED (first pass). **Reviewer:** dev lead (diff + real headless smoke, re-run personally).

## Verified independently (lead)
- `dotnet test … --filter "FullyQualifiedName~DebugApi"` → **32/32 passed** (was 21; +11 in
  `DebugApiBatch04Tests.cs`). Build 0 errors.
- **Real headless smoke** (`ADA_RUN_HEADLESS_SMOKE=1`, `HeadlessSmoke` filter) → **1/1 passed**. The smoke is
  genuinely end-to-end: `GET /commands` non-empty → load `test-move` (entityCount>0) →
  `POST /entities/spawn {tkbType:1001}` → polls entityCount **increases** → clean `/shutdown`. This is the
  arbiter gate and it is green on the real process (not just unit-green).

## Diff review
- `ListCommands()` / `ListComponents()` — enumerate `EventType.GetAllRegistered()` /
  `ComponentTypeRegistry.GetAllTypes()` + `JsonShapeDescriber` field schemas. Marshalled to main thread
  (defensive; registry is read-only after boot).
- `SendCommand(eventType, payload, wait)` — resolves CLR type by name (unknown → 400, no crash; verified by
  `SendCommand_UnknownEventType_Returns400Error`), deserializes payload, publishes via reflection dispatch
  (`PublishEventObject`: value type → `Publish<T>`, else `PublishManaged<T>`). Wait-gating owned by the API:
  `!wait || !timeAdvancing` → `{awaited:false, reason:"sim not running"}`.
- `SpawnEntity(...)` — builds + `PublishManaged`s a `SpawnEntityCommand` (NetworkId=0 → allocate), optional
  transform/components/attributesJson best-effort parsed. Verified to raise entityCount in both Tier-1 and
  the real headless smoke.

## Deviations / debt (both honestly disclosed by the agent — not faked)
- **ADA-04-D01** (sanctioned): `awaited:true` correlated-ack happy path not implemented — needs a multi-tick
  continuation the synchronous main-thread job can't express. The instructions explicitly permitted this
  ("may be best-effort… log debt, don't fake it"). Returns `awaited:false, reason:"ack-wait not yet supported"`
  when sim is advancing.
- **ADA-04-D02** (real spec gap, follow-up): `/commands` lists only **unmanaged** `[EventId]` events.
  `EventType.GetAllRegistered()` is `where T : unmanaged`; managed events (`SpawnEntityCommand`,
  `MissionControlIntent`) live in the bus's `_managedStreams`, registered lazily, with **no static catalog**.
  The Tier-1 tests assert `MissionControlAckEvent` / `CenterOnEntityCommand` (both unmanaged) rather than the
  spec's literal `SpawnEntityCommand` / `MissionControlIntent` examples — i.e. the assertions track the
  implementation's reach, not the spec's. Acceptable because (a) spawn has its own dedicated endpoint and
  (b) a complete managed-event listing requires an engine seam (bus-level `GetRegisteredManagedEventTypes()`)
  or an assembly scan for a marker — genuine design work, not a mechanical fix. **Promoted to ADA-P1-T06b**
  (managed-event discovery), folded into the next discovery-adjacent batch; not silently dropped.

## Payload-deserialization note (watch, not blocking)
`SendCommand` deserializes the inbound payload with default `System.Text.Json` (input only; output still uses
the DTO path). Fine for simple command structs; complex domain payloads (FixedString / InlineArray / fixed
buffers) could mis-deserialize. No such command is exercised yet; revisit if/when a rich-payload command
endpoint is added.

## Lesson
First batch this workstream where the agent's "done" survived the real-headless gate intact. The discipline
still earned its keep on review: it surfaced that the `/commands` tests had been scoped to the
implementation's reach (unmanaged only) rather than the spec's named examples — caught by reading the
assertions against the spec, and converted into a tracked follow-up (T06b / D02) instead of a buried gap.
