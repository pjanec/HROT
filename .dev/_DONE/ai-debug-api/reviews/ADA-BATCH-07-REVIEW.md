# ADA-BATCH-07 Review (Breakpoints — run-until-condition, Group G + MCP)

**Verdict:** ACCEPTED after one fix round. **Reviewer:** dev lead (full build + diff + real headless hit
reproduce + independent `npm run verify` + orphan check).

## Verified independently (lead)
- **Full-solution build** (`dotnet build IOS-IG-SimHost.sln`) → 0 errors (the `DebugApiService` ctor gained a
  trailing optional `bpManager` param; harness ripples — full build is the gate for that).
- `dotnet test … --filter DebugApi` → **59/59** (51 prior + 8 new). The 8 cover predicate JSON round-trip
  (incl. `CompoundPredicateDto`), registration, list/remove, and hit-observation plumbing.
- **Real end-to-end hit (lead, by hand) — the feature's whole point, proven on the live headless process:**
  load `test-move` (entity networkId 1000, M2 Bradley, has `SimTransform`) → `play` (preview, unpaused) →
  `POST /breakpoints` always-true `PropertyMatch` on `SimTransform.Position.X` → `GET /breakpoints/hits`
  returned `isPaused:true`, `pausedTick` nonzero, `lastHit:{BP#2, networkId:1000}`, `hitCount` 0→1. The
  predicate compiles, `DataBreakpointSystem` evaluates the live entity, the sim pauses, and the
  `OnBreakpointHit` subscription resolves entity→networkId correctly. ✅
- **Re-ran `npm run verify` myself → 75/75, VERIFICATION PASSED**, including the new Step 10c which drives the
  same real hit *through MCP* (`isPaused:true`, `pausedTick:639170574153961200`, `lastHit.networkId:1000`,
  `hitCount:1`). Independent orphan check: 0 `Hrot.ClusterRunner` before and after.

## Fix round (gate caught two things on first pass)
1. **BLOCKING — `verify.mjs` did not parse:** Step 10b declared `const statusData` a second time in `main()`
   (collision with the line-139 declaration) → `npm run verify` exited 1 with a SyntaxError; the agent's
   "verify passed" was impossible (it never re-ran the script after editing). Fix: renamed to `bpStatusData`.
   Fifth time the real-reproduce gate has caught a claim the artifact didn't support — re-running the actual
   command, not trusting the report, remains the arbiter.
2. **ADA-07-D01 was over-pessimistic.** The agent only plumbing-tested the hit via direct `OnHit()` injection
   and logged "e2e hit not tested." But the real headless process runs `DataBreakpointSystem`, so I drove a
   real hit by hand (above). Sent it back to *automate* that exact recipe → Step 10c in `verify.mjs`. D01 now
   RESOLVED (proven + automated). Note: my first manual attempt (`Position.Y > 9.3`) didn't fire only because
   the entity barely moves (9.21→9.228 over 56 sim-seconds) — a bad threshold, not a bug; an always-true
   predicate fired on the next tick. Worth diagnosing before concluding "broken."

## Diff review
- `DebugApiService`: `AddBreakpoint`/`ListBreakpoints`/`RemoveBreakpoint`/`GetBreakpointStatus`. Polymorphic
  `SearchPredicateDto` via the `$type` discriminator with `IncludeFields=true` + `JsonStringEnumConverter`
  (matches the proven options). `bpManager` optional trailing ctor param → no breaking change to callers; the
  editor passes the already-wired `_bpManager` (no second instance). `OnBreakpointHit` subscription stores
  last hit + resolves networkId on the main thread. `RemoveBreakpoint` parses `BP#N` (and bare `N`) via an
  `AllBreakpoints` lookup — a reasonable workaround for the cross-assembly-internal `BreakpointId` ctor.
- `DebugApiHost`: `GET /breakpoints/hits` registered before `DELETE /breakpoints/{id}` to avoid route
  ambiguity (correct — two literal segments win over the parameterized route). 400 on bad condition /
  unknown filterNetworkId; 404 on unknown id.
- MCP: 4 tools 1:1; README updated (29 tools; ADA-06-D01 note: Group G present, H/I/J/K/L pending).

## Lesson
The headline leverage feature can't be accepted on round-trip + injected-plumbing alone — its value IS the
real auto-pause. Driving an actual hit on the live process is what proved it (and what turned an "open gap"
into a resolved, automated test). Also: a non-firing breakpoint is ambiguous — diagnose whether the condition
was even met (entity movement) before suspecting the engine.
