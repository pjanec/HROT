# TH-1: Test-health pass — categorize + Trait-mark + fix-the-cheap-ones (hot suites first)
**Goal:** make `dotnet test` on the two suites we hit every iteration **fast-green** (0 failed) WITHOUT deleting
tests — by categorizing the ~80 pre-existing/flaky failures, marking the genuinely-unstable ones with a `Stability`
Trait (so fast runs filter them out), and *fixing* the cheap deterministic ones. Establish a reusable convention +
ledger for the rest of the solution. **Read `.dev/.guides/DEV-GUIDE_claude.md`; use codebase-memory MCP.**
**Scope (this batch):** `FDP/Toolkits/Fdp.Toolkits.Tests` and `Hrot/Subsystems/Hrot.SimHost.Tests` only.

## Why
These suites carry ~35 (Fdp.Toolkits, swings 28–53) and ~44 (SimHost, 41–48) failures unrelated to current work, and
they're badly flaky. Every delegated batch wastes huge tokens because "is the suite green?" is unanswerable. Fixing
this is the highest-leverage iteration-cost win: a documented `Stability` trait + default filter gives every future
batch a clean green target.

## The convention to establish (REUSABLE — document it)
- xUnit trait on each genuinely-unstable/broken test: `[Trait("Stability", "<bucket>")]` where bucket ∈
  **`Flaky`** (intermittent: timing/zero-alloc/parallel/static-state-order), **`Environment`** (deterministic but
  env-specific: locale, CRLF, ALC-GC, off-main-thread), **`Broken`** (deterministic failure that looks like a real
  bug or stale test — NOT cheap to fix here).
- Each marked test also gets a one-line inline `// STABILITY(<bucket>): <reason> — <resolution/target>` comment.
- **Fast-run filter (document in the ledger + a `.dev/_DONE/test-health/README` note):**
  `dotnet test <proj> --filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"` → must be **0 failed**.
- **Ledger:** create `.dev/_DONE/test-health/TEST-HEALTH.md` — a table: `Test | Suite | Bucket | Reason | Resolution/Target`.
  One row per marked or fixed test.

## What to FIX (cheap, deterministic — make the test pass for the right reason; do NOT mark these)
- **Locale** (e.g. `"0.8"` vs `"0,8"`): fix the production OR test formatting to `CultureInfo.InvariantCulture`
  (prefer production if it formats for output/messages with current culture — that's a real latent bug).
- **CRLF/LF snapshot mismatches:** normalize line endings on both sides of the comparison (`.Replace("\r\n","\n")`)
  and/or `.gitattributes eol=lf` for the snapshot files. Must pass regardless of checkout line endings.
- **Test-local ComponentId collisions** (like EditLoad/Checkpoint earlier): renumber to IDs free across production
  (≤264) AND all test/fake ranges (check `NavFakeIds` etc.) — verify the chosen ID is unused repo-wide.
- Any other genuinely trivial deterministic fix you're confident in.

## What to MARK (not fix here)
- **Flaky:** confirm by running the suite **3×** — a test that fails intermittently (passes ≥1 of 3) is Flaky. Mark +
  reason (e.g. "zero-alloc: 3.2 B/entity/frame, GC-timing sensitive"). Do NOT delete; do NOT try to fix expensive
  perf/threading flakiness in this batch.
- **Environment:** deterministic but env-bound and not cheap to fix → mark + reason + target.
- **Broken:** deterministic real-looking failure that isn't a cheap fix → mark `Broken` + reason ("suspected real bug
  / stale test — investigate") + add a ledger target. **Do NOT silently 'fix' by weakening the assertion** and do NOT
  mark a real bug as `Flaky` to hide it — `Broken` is the honest bucket and it stays visible in the ledger.

## Tests / verification
- After marking+fixing: `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"`
  and the same for `Hrot/Subsystems/Hrot.SimHost.Tests` → **both 0 failed**, run **twice** to confirm no remaining
  flakiness leaked through (any test that still flakes under the filter must be marked Flaky).
- The cheap-fixed tests pass UNFILTERED (deterministically) — list them.
- Total marked + total fixed counts reported; the ledger complete.

## Success criteria
- [ ] Filtered fast-run is 0-failed (×2) for BOTH suites; convention + filter documented.
- [ ] `.dev/_DONE/test-health/TEST-HEALTH.md` ledger complete (every failing test → bucket + reason + resolution/target).
- [ ] Cheap fixes are REAL (test passes for the right reason), not weakened assertions or marks.
- [ ] No tests deleted; no production behavior changed except genuine cheap fixes (locale invariant etc.).
- [ ] Report at `.dev/_DONE/test-health/reports/TH-1-REPORT.md`: buckets + counts, what was fixed vs marked, the filter
      command, and any `Broken` tests flagged for follow-up investigation.

## Guardrails / honesty
Do NOT mark a test just to turn the filter green without genuinely categorizing it (flaky claims need the 3× evidence;
real bugs go in `Broken`, visible). Do NOT delete tests, weaken assertions, or change unrelated production code. Report
the REAL counts. Return a summary; the Lead reviews + commits.
