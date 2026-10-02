# ADA-BATCH-13 Review (Live Mutation / Fault Injection, Group L + MCP)

**Verdict:** ACCEPTED after one fix round. **Reviewer:** dev lead (full build + diff + live 3-step mutation
reproduce on FRESH reads + independent `npm run verify` + orphan check). **Closes ADA-06-D01.**

## First pass — REJECTED by the live gate
Build + 105 tests were green and the agent was honest (didn't claim verify-green), but the live reproduce
exposed two real defects:
1. **StructEdit (P8-T02) was a silent no-op:** `POST …/component {Health:{Current:50}}` returned `ok:true`
   but a fresh `GET` still showed `Current:100`; invalid values weren't rejected. The 105 tests passed
   because they asserted on the edited in-memory object, NOT a re-extraction from the repo.
2. **Attribute patch (P8-T01)** only accepted `patchJson` as a stringified string; a natural nested object
   threw HTTP 500 — bad for an API consumed by an AI agent.

## Root causes (fix round)
1. **Key-prefix mismatch + swallowed error** (not the boxed-struct hypothesis): `CollectLeafNodes` stored
   patch keys as `"$.Current"` while `ApplyJsonValue` looked up bare `"Current"` → every field missed, and a
   silent `catch {}` returned `ok:true` with no change. Fix: strip the `"$."` prefix; remove the silent catch
   (throw `ArgumentException` on parse failure); map the failure to a **400**. The struct write-back to ECS
   was actually fine — the edit just never found its fields.
2. Attribute route now accepts `patchJson` as a JSON **object OR string** (serializes object→string before
   `Compile`); clean 400 on unusable input. Same treatment for the StructEdit `patch` field + MCP tools.

## Verified independently (lead, after fix) — the exact 3-step arbiter
- `POST …/attribute {"patchJson":{"Name":"Alpha"}}` (nested object) → `ok:true`; fresh `GET` →
  `EntityInfo.Name == "Alpha"`. ✅
- `POST …/component {"componentType":"Health","patch":{"Current":50}}` → `ok:true`; fresh `GET` →
  `Health.Current == 50` (**persists** — the no-op is gone). ✅
- `POST …/component {…"Current":"xyz"}` → **HTTP 400** (`"Invalid patch value: Cannot parse … expected
  Single"`); fresh `GET` → `Health.Current` still 50 (unchanged). ✅
- Full build 0 errors; `dotnet test --filter DebugApi` → **107/107** (the 2 new StructEdit tests now re-read
  via `Repo.GetComponentRO<Health>()`, so a no-op would fail). `npm run verify` → **193/0, VERIFICATION
  PASSED**, orphan before=0 after=0.

## Diff review
- `GetAttributesSchema` (RegisteredPaths + ExportSchema), `PatchEntityAttribute` (compiler-direct on the job
  queue, authority-aware, unregistered keys ignored), `EditEntityComponent` (StructEdit Open→apply→Commit→
  persist, 400 on validation/parse failure). 3 MCP tools (47 total). ADA-06-D01 CLOSED.

## Lesson
The canonical "green tests, broken live" case — and the canonical catch: re-run the real sequence AND assert
the value actually changed on a FRESH read, not the object you just edited. A silent `catch {}` returning
`ok:true` is the worst failure mode (looks successful, does nothing); the fix also made it loud (clean 400).
The strengthened tests now re-read from the repo, so the gap can't reopen silently.
