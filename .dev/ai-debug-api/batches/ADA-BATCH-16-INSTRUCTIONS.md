# ADA-BATCH-16: Educating Semantic Errors at the API (Tier 1, C#)

**Batch Number:** ADA-BATCH-16
**Tasks:** Make `DebugApiService` thrown/returned error messages actionable — state *what* was wrong + the
*corrective action* (the endpoint/tool to call). The Tier-1 (server-side) half of "educating errors".
**Executor:** sonnet
**Priority:** MEDIUM
**Dependencies:** all prior batches. **Runs in parallel with BATCH-15** (BATCH-15 is JS-only under
`tools/ai-debug-mcp/`; this batch is C#-only under `Hrot/Subsystems/Hrot.Editor/DebugApi/` + its tests — no
file overlap).

---

## Goal

The API's consumer is an AI agent. Today some error strings are bare symptoms (`"Unknown eventType: 'X'"`,
`"Entity N not found."`) with no cure. Make each *semantic, user-correctable* error name the fix — ideally
the discovery endpoint that resolves it. This composes with BATCH-15's MCP-side per-tool `hint`: the agent
gets a precise *what+fix* from the API plus *how-to-call* from the proxy.

> No codebase-memory MCP (hangs — Grep/Glob/Read). No git commit. Report HONESTLY; the lead re-runs the
> tests + a live reproduce that triggers these errors and reads the messages. Run the FULL build.

## Pattern
Format: **`<what was wrong>. <corrective action, naming the endpoint>.`** Reference the HTTP endpoint (the
API's own contract — e.g. `GET /commands`); the agent's MCP layer maps endpoints→tool names, and BATCH-15's
hint supplies the tool name, so endpoint references are correct and sufficient here. Keep messages to one or
two short sentences.

## Scope — the error sites to upgrade (in `DebugApiService.cs`)
Make these actionable (line numbers approximate — find by message text):
- `"Unknown eventType: '{X}'"` (SendCommand) → add `" List publishable events with GET /commands."`
- `"filterNetworkId {N} not found."` (AddBreakpoint) → add `" List entities with GET /entities."`
- `"Breakpoint '{X}' not found."` (RemoveBreakpoint/ParseBreakpointId) → add `" List with GET /breakpoints."`
- `"Unknown baselineId: '{X}'."` (CompareBaseline) → add `" Capture one with POST /diff/capture."`
- `"Entity {N} not found."` (PatchEntityAttribute, EditEntityComponent, DumpEntity if applicable) → add
  `" List entities with GET /entities."`
- `"Unknown component type: '{X}'"` (EditEntityComponent) → add `" List registered components with GET /components."`
- `"Unknown annotation type '{X}'..."` — already lists supported types; leave or lightly polish.
- **Wait-gating reason** `"sim not running"` (SendCommand / SpawnEntity, and any `reason` field) → enrich to
  `"sim not running — time only advances in preview while unpaused; call POST /preview/enter then POST /sim/play, or POST /sim/step to advance."`
- Recording `"Live mode recording is not supported..."` — already names `mode:preview`; leave.
- Already-good ones (keep): `"Already checkpointed or in preview. Exit preview or restore first."`,
  `"No replay loaded. Call /replay/load first."`, the diff-from-checkpoint redirect, `"Use 'preview' or 'live'."`,
  the attribute/StructEdit parse errors (they already quote the field + expected type).

**Do NOT change** the "not available / not wired" `InvalidOperationException`s (e.g. `"Breakpoint manager not
available."`, `"EcsRecordReplayController not available."`, `"DebugPrimitiveBuffer not available..."`). Those
are server-configuration conditions, not agent-correctable — leave them (optionally append
`" (server configured without this capability.)"` but no tool reference).

Optional small helper (nice, not required): a private `static string Fix(string what, string action) => $"{what} {action}";`
or just inline the strings — keep it simple and greppable.

## Constraints (hard)
- Behavior unchanged — only message TEXT changes (and the HTTP status codes stay as they are: 400/404/409/500
  as already mapped by the host). Do not change which exception type is thrown (the host maps them).
- Messages are short and reference the API's own endpoints (not MCP tool names). Don't touch
  availability/wiring errors. Frozen `TestAssets`; never the production scan path; never regenerate snapshots.
- **Stay out of `tools/ai-debug-mcp/`** — that's BATCH-15's territory (running concurrently).

## Verification
- **Tier-1 (EditorHarness):** add/extend tests asserting the upgraded messages contain BOTH the symptom and
  the corrective endpoint substring (e.g. `Assert.Contains("GET /commands", ex.Message)` for an unknown
  eventType; the entity-not-found message contains `"GET /entities"`; the wait-gating `reason` contains
  `"preview"`). Cover the main upgraded sites.
- **Live reproduce (the lead will re-run; include your output):** launch headless, load test-move, then via
  raw HTTP (curl) trigger and quote the new messages:
  - `POST /entities/command {"eventType":"NopeNope"}` → error names `GET /commands`.
  - `GET /entities/999999` → error names `GET /entities`.
  - `POST /entities/999999/attribute {"patchJson":{"Name":"x"}}` → entity-not-found names `GET /entities`.
  - `POST /entities/1000/component {"componentType":"Nope","patch":{}}` → names `GET /components`.
  - a `send_entity_command {wait:true}` while not in preview → `reason` mentions preview/step.
- `dotnet build IOS-IG-SimHost.sln` (0 errors); `dotnet test … --filter "FullyQualifiedName~DebugApi"` green.
  (No need to run `npm run verify` here — no JS changes — but don't break it.)

## Deliverables
- Upgraded messages in `DebugApiService.cs` + tests asserting they are actionable.
- `.dev/ai-debug-api/reports/ADA-BATCH-16-REPORT.md` (DEV-GUIDE format): the sites upgraded, before→after
  examples, FULL `dotnet test` summary, the live reproduce output quoting the new messages, blockers, debt.
