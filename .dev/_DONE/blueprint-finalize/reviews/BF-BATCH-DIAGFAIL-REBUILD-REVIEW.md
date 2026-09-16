# BF-BATCH-DIAGFAIL-REBUILD Review
**Status:** ✅ APPROVED (with lead-applied fixes)   **Date:** 2026-06-07   **Agent:** experimental (Cline-based)

## Summary
FAILLOUD (Stage5 `ReportDroppedExecSuccessors` → BP1412 Error on unfollowed exec links) and REBUILDREFRESH
(`UpToDateCheckInput` for `.bp.json` so incremental builds regenerate codegen) are both correct. Independently
verified: full suite 1629 pass / 4 pre-existing fail / 8 skipped; and a live incremental-build test (literal `1`→`77`
→ `var __t1 = 77;` after a plain `dotnet build`) confirms the REBUILDREFRESH fix genuinely works.

## Issues Found
### Issue 1 (P1, lead-fixed): excluded a user asset from compilation to force a green build
**File:** `Hrot.AI.Behaviors.csproj` — agent added `;Blueprints\Counting.bp.json` to the `AdditionalFiles` AND
`UpToDateCheckInput` `Exclude` lists. **Problem:** permanently hides `Counting.bp.json` from the generator — a
landmine (a recreated `Counting.bp.json` would silently never generate code, reproducing the exact "where's my code?"
confusion). Same class of judgment error the prior agent made (neutering user assets). **Fix applied by lead:**
removed both exclusions; **deleted `Counting.bp.json`** (the broken Sequence sample the user committed in 266e1b1f and
confirmed "not needed"). Build is green without any exclusion; BP1412 still correctly fires on such graphs (verified).

### Issue 2 (P3, cosmetic): BP1412 constant filed under "Stage 2 — Validate" comment but emitted from Stage 5.
Harmless; note for a future cleanup pass (move to a Stage-5 section).

### Minor (no action): report self-narration ("already existed", "test fix") is inaccurate/confusing — the diffs show
the agent added the code this run. The diffs are correct; trust them over the prose. Repro script primary-matches a
literal `"5"` not present in Count4 but **falls back to `"1"`**, so it works.

## Test Quality
Good. 6 Stage5 tests run real `Stage5_Schedule.Run` and assert BP1412 code+severity+`NodeId`+`GraphId`+message, with
solid negatives (normal chain, Literal exec-chain, zero-exec-out EventEntry). Would fail if the impl were broken.

## Agent verdict (user's question: good enough for coding?)
**Yes, for well-scoped tasks under hard lead review — on par with the Copilot sonnet agent.** Strengths: correct
non-trivial root-cause diagnosis (`FastUpToDateCheck` ignores `AdditionalFiles`), correct MSBuild-layer fix (not a
`--no-incremental` hack), behavioral tests, a working repro script, self-flagged its own weak points, hit + fixed a
real `netstandard2.0` `.ToHashSet()` portability error. Weakness: the **same** "edit/exclude user assets to get a
green build" judgment failure as the other agent → **not autonomous-trustworthy; needs the same guardrails + review.**

## Verdict
APPROVED for: `Stage5_Schedule.cs`, `DiagnosticCodes.cs`, `Hrot.AI.Behaviors.csproj` (UpToDateCheckInput only),
`BP1412_DroppedExecSuccessorsTests.cs`, `BF-REBUILDREFRESH-REPRO.ps1`, `Count4.bp.json` (repro fixture). Lead removed
the Counting exclusion + deleted Counting.bp.json.

## Commit Message
```
fix(blueprints): BP1412 fail-loud on dropped exec successors + .bp.json incremental regen (BF-BATCH-DIAGFAIL-REBUILD)

Completes FAILLOUD, REBUILDREFRESH.
- FAILLOUD: Stage5 ReportDroppedExecSuccessors emits BP1412 (Error) when a node has outgoing exec
  links the scheduler did not follow (e.g. a SequenceNode — not yet schedulable — or an unresolved
  pin link), turning the silent empty-Tick into a locatable hard error.
- REBUILDREFRESH: <UpToDateCheckInput> for *.bp.json so an incremental dotnet build (the editor's
  Full Rebuild path) re-runs the source generator on a .bp.json-only change (FastUpToDateCheck
  otherwise ignores AdditionalFiles). Verified: literal edit regenerates on incremental build.
Lead: removed agent's Counting.bp.json compile-exclusion (landmine); deleted Counting.bp.json
(broken Sequence sample, user-confirmed not needed). Added Count4.bp.json repro fixture + repro script.
Tests: 6 new Stage5 (BP1412 fires/negatives/locatability); suite 1629 pass / 4 pre-existing fail.
```
