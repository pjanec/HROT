# BF-02 Review (delegated to Zoo worker)
**Status:** ✅ APPROVED   **Date:** 2026-06-10

## Summary
Fixes `SetVariable`/`GetVariable` nodes authored with a `"var:<Guid>"` VariableId (My-Blueprint panel item-id format) compiling to uncompilable `s.__var_-1`. `Stage5_Schedule.FindVariableIndex` did not strip the `var:` prefix before `Guid.TryParse` (every other resolver does) → returned index -1.

## Verification performed (independent — Zoo: trust diffs)
- **Scope:** `git status` shows ONLY `Stage5_Schedule.cs` + the new test file. `Count5.bp.json` is untracked and was NOT staged/committed (user experiment). No scope creep, no suppression.
- **Fix diff:** strips `var:` (case-insensitive) into `idStr`, uses it for the GUID parse AND the name-fallback — mirrors `Stage0_Rehydrate.ResolveVariableTypeId` exactly. Search order preserved.
- **Tests** (`Stage5VarPrefixResolutionTests.cs`, 2): run the full Stage2→Stage7 pipeline; assert no compile diagnostics, `__var_-1` absent (precise regression guard), and the real field (`s.Count` / `s.Speed`) emitted. SetVariable + GetVariable both covered (GetVariable case keeps the SetVar on a bare GUID to isolate the read path).
- **Ran full `Hrot.Blueprints.Tests` myself:** 1737 passed / 7 failed / 8 skipped / 1752 total — same 7 documented pre-existing reds, **zero new failures**; both new tests pass.

## Notes / follow-up debt
- The compiler **silently** emitted invalid code (`s.__var_-1`) for an unresolved variable rather than a clean diagnostic → confusing downstream CS1061. Tracked as P2 (DBG2-D4): Stage2/Stage5 should emit a BP-error when a SetVariable/GetVariable VariableId resolves to -1.
- `Count5.bp.json` (user experiment) intentionally left uncommitted.

## Verdict
APPROVED — committed.
