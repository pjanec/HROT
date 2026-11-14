# ADA-BATCH-11 Review (Logs Query Group J + Entity Filter/Spatial Group B + MCP)

**Verdict:** ACCEPTED after one verify-fix round. **Reviewer:** dev lead (full build + diff + live reproduce
of logs/filters + independent `npm run verify` + orphan check).

## Verified independently (lead)
- Full build → 0 errors. `dotnet test … --filter DebugApi` → **91/91** (11 new).
- **Live reproduce:**
  - `GET /logs?max=5` → ok, well-formed `{timestamp, level, logger, message}`; `?level=Error` → 0 (narrows
    from the Debug/Info entries — clean run has no errors).
  - `GET /entities?component=BrainBlackboard` → 1; `?component=NoSuchComponent` → 0.
  - `GET /entities?near=-6,0,100` → 1 (entity 1000 at `[-6,5,0]`, XZ-plane); `?near=9999,9999,10` → 0.
- **`npm run verify` (independent, after the fix) → 168/0, VERIFICATION PASSED**, orphan before=0 after=0.

## Diff review
- `GetLogs(level?, logger?, since?, max?)` reads `NLogMessageLogTarget.SharedInstance` + `AiBehaviorLogTarget`
  off-thread (lock-guarded; no `RunMain`); level = minimum inclusive, since = ISO-8601, max default 200,
  newest-first. `logSinks` injected via an optional ctor param. Sound.
- `ListEntities(component?, near?)` — case-insensitive has-component filter + XZ-plane radius test, composable.
  Agent self-caught a real bug: the near-filter initially read `Position.X` but `Vector3` serializes as
  `[x,y,z]` — fixed to index the array. Good catch.
- `get_logs` MCP tool added (1:1); `list_entities` passes `component`/`near` through. NaN-safe dumps inherited.

## Fix round (gate caught it)
First pass shipped a **red `verify.mjs`** (166/1) — and the agent did not honestly run it to green before
reporting (the SECOND time after BATCH-07's syntax error, despite an explicit warning in the prompt). The
failing assertion was a **test bug, not a product bug**: Step 10h compared the component-filtered count
against a STALE unfiltered baseline (1, captured before earlier spawn steps; 3 entities by Step 10h, all with
SimTransform) → `3 <= 1` failed. The filter itself is correct (returned all entities that have the component;
`NonExistent` → empty). Fix: re-capture the unfiltered count fresh immediately before the assertion + assert
the subset relation (`<=`) and `>= 1`. Re-verified green by the lead.

## Lesson
The product was right and live-verified, but the gate still caught a false-green report on `verify.mjs` — the
recurring failure mode is agents editing the verify script and not re-running it. The lead's independent
`npm run verify` is the only reliable check for that. (Worth considering a standing instruction or a
pre-report hook so agents can't claim verify-green without the actual tally.)
