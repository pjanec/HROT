# ADA-BATCH-16 Review (Educating Semantic Errors at the API — Tier 1, C#)

**Verdict:** ACCEPTED after one consistency fix. **Reviewer:** dev lead (build + 125/125 tests + live curl
reproduce of every upgraded message). Completes the educating-errors feature (Tier 1 API + Tier 2 proxy/B15).

## Verified independently (lead) — live curl, each error names its fix
- `POST /entities/command {eventType:"NopeNope"}` → `"Unknown eventType: 'NopeNope'. List publishable events
  with GET /commands."` ✅
- `POST /entities/999999/attribute {...}` → `"Entity 999999 not found. List entities with GET /entities."` ✅
- `POST /entities/1000/component {componentType:"Nope"}` → `"Unknown component type: 'Nope'. List registered
  components with GET /components."` ✅
- `send_entity_command {wait:true}` while not in preview → `reason: "sim not running — time only advances in
  preview while unpaused; call POST /preview/enter then POST /sim/play, or POST /sim/step to advance."` ✅
- `POST /diff/compare {baselineId:"BL#999"}` → `"Unknown baselineId: 'BL#999'. Capture one with POST
  /diff/capture."` ✅
- `GET /entities/999999` → `"Entity 999999 not found. List entities with GET /entities."` ✅ (the fix below)
- Build 0 errors; `dotnet test --filter DebugApi` → **125/125**.

## Consistency fix (gate caught it)
First pass upgraded the 9 *service-layer* messages but missed the `GET /entities/{id}` 404, which lives in
the **host route** (`DebugApiHost.cs:92`), not the service method — so the most common "not found" path stayed
bare while its siblings educated. Fixed to the identical wording, plus a real host-level test
(`GetEntitiesById_UnknownId_Returns404WithHelpMessage` spins up `DebugApiHost`, asserts 404 + both
substrings). Now uniform. Caught by reading the live reproduce of *every* site, not just the ones the agent
listed.

## Diff review
- 9 service-layer messages + 1 host message upgraded; ONLY message text changed (exception types, status
  codes 400/404/409, behavior all unchanged). Availability/wiring errors ("X not available") correctly left
  alone (not agent-correctable). 10 new tests asserting symptom + corrective-endpoint substrings.

## Closeout — educating errors complete (both tiers)
- **Tier 1 (API, this batch):** the simulation's own error names *what + the fix endpoint*.
- **Tier 2 (proxy, BATCH-15):** `toolError` appends the per-tool `hint` (required params + example) + a docs
  ref, sourced from the single catalog.
Together: a failing call now returns e.g. `{error:"Unknown eventType 'X'. List publishable events with GET
/commands.", hint:"Req: eventType (string from list_commands)… Example: …", docs:"…"}`. An agent learns both
*why it failed* and *how to call it right*.

## Lesson
Same discipline that ran the whole workstream: re-run the real sequence for *every* claimed site, not a
sample. The service-layer messages were perfect; the one host-route message was the gap, and only exercising
all six live cases surfaced it.
