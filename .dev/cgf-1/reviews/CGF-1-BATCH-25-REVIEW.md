# CGF-1-BATCH-25 Review

**Batch:** CGF-1-BATCH-25  
**Reviewer:** Development Lead  
**Date:** 2026-03-30  
**Status:** **APPROVED** — All P1 and P2 debt from BATCH-24 resolved; no regressions; Part C policy documented.

**Report:** [CGF-1-BATCH-25-REPORT.md](../reports/CGF-1-BATCH-25-REPORT.md)  
**Instructions:** [CGF-1-BATCH-25-INSTRUCTIONS.md](../batches/CGF-1-BATCH-25-INSTRUCTIONS.md)

---

## Executive Summary

| Area | Verdict |
|------|---------|
| **A.1 — Runner tests green** | ✅ Met — `ParseMode_ComboAllFour_EqualsAllFlag` correctly uses `simhost,ig,ios,orchestrator`; `ParseMode_AllMode_HasAllFourFlags` asserts all four flags including Orchestrator; 138/138 pass. |
| **A.2 — `cgf` CLI token** | ✅ Met — `"cgf"` parseable as standalone and in combo (`orchestrator,cgf`); 3 new tests added; `RunMode.CGF ∉ RunMode.All` documented. |
| **B.1 — Fail-loud handlers** | ✅ Met — `AssertEntityCountActionHandler` and `AddMovingTagActionHandler` throw `InvalidOperationException` on null world / dead entity. |
| **B.2 — `AssertionRule.Exactly`** | ✅ Met — CS0108 eliminated; all JSON scripts, executor references, and test string literals updated. |
| **C — E2E CI policy** | ✅ Documented — `DsmE2eScriptTests` failures acknowledged as requiring a dedicated integration stage; `[Trait("Category","DsmE2e")]` deferred to next batch. |
| **No regressions** | ✅ Confirmed — all pre-existing passing suites unchanged. |

---

## Verification

```
dotnet test Bagira.Runner.Tests --nologo --no-build
  Passed! — Failed: 0, Passed: 138, Skipped: 0
```

Full solution: all previously-passing suites still pass. Pre-existing failures in `Bagira.Runner.Integration.Tests` (DsmE2e + MiniIos) and `Fdp.Examples.NetworkDemo.Tests` are unchanged.

---

## Part A — Source verification

**`RunnerConfigurationTests.cs`** — Two renames confirmed in source:
- `ParseMode_ComboAllFour_EqualsAllFlag` uses `"simhost,ig,ios,orchestrator"` and asserts `== RunMode.All`.
- `ParseMode_AllMode_HasAllFourFlags` asserts `HasFlag(RunMode.Orchestrator)` and `!HasFlag(RunMode.CGF)`.
- Three new `cgf` tests present and correct.

**`BagiraRunnerConfiguration.cs`** — `case "cgf": result |= RunMode.CGF; break;` confirmed in combat path; single-token path also handles `cgf`.

---

## Part B — Source verification

**`OrchestratorActionHandlers.cs`** — Six `throw new InvalidOperationException` statements confirmed; no remaining `LogWarning + return null/success` paths for null world or dead entity.

**`TestScript.cs`** — `public double? Exactly { get; set; }` confirmed; no `Equals` property remaining.

**`HeadlessTestExecutor.cs`** — All references to `rule.Exactly` confirmed; format string updated.

**JSON scripts and test inline strings** — grep for `"Equals":` in `Bagira.Runner.Integration.Tests/**` returns zero matches. Good catch from the developer that `RunnerIntegrationTests.cs` had inline JSON strings that also needed updating.

---

## Part C — Policy

The developer's choice is accepted:
- `DsmE2eScriptTests` require a live DDS + multi-subsystem stack — not suitable for default PR `dotnet test`.
- Adding `[Trait("Category", "DsmE2e")]` is tracked as a P3 item below.
- Policy: default CI excludes `DsmE2eScriptTests`; nightly/integration pipeline runs `Bagira.Runner.Integration.Tests` with domain isolation.

---

## Debt items to record

| Priority | Category | Description | Target |
|---------|----------|-------------|--------|
| P3 | Testing | `DsmE2eScriptTests` lacks `[Trait("Category","DsmE2e")]` to enable PR-build filtering. 3-line change. | CGF-1-BATCH-26 (or opportunistic) |
| P3 | Architecture | `ParseModeString` is private; tests must go through `Validate()`. Elevate to `internal` with `[InternalsVisibleTo]` for cleaner unit tests. | Opportunistic |
| P3 | Safety | `cgf,ci` combo silently drops `ci` token — add validation warning. | Opportunistic |

---

## Developer insights to record in debt tracker

Developer identified a valuable risk: **`Newtonsoft.Json` silent-ignore on unknown keys** in `AssertionRule`. When any JSON-bound property is renamed without a grep sweep, the rule becomes null silently. This is a systemic issue for test scripts. Consider:
- Adding `[JsonProperty(Required = Required.AllowNull)]` only on high-value assertion properties.
- Or a JSON schema validator step at test load time.

---

## Suggested git commit message

```
CGF-1-BATCH-25: green Runner tests + cgf CLI mode + fail-loud handlers + AssertionRule.Exactly

A.1: ComboAllFour combo + AllFourFlags assertions to align with RunMode.All (Orchestrator included).
A.2: cgf token in ParseModeString (single + combo paths); 3 new tests; CGF not in All documented.
B.1: AssertEntityCountActionHandler + AddMovingTagActionHandler throw InvalidOperationException
     on null world / dead entity (was warn+silent success).
B.2: AssertionRule.Equals renamed to Exactly (CS0108 fix); all JSON scripts, executor, and
     inline test strings updated; grep-verified zero stale Equals: references.
C: DsmE2e CI policy documented — dedicated integration stage; [Trait] deferred.
```

---

## Next steps

Phase 5 begins with **CGF-1-BATCH-26**. All 7 Phase 5 tasks (S0501–S0507) are open. The dependency chain is:

```
S0501 (ImGui overhaul) → S0502 (fan-out fix) → S0503 (time control) + S0504 (asset combo)
                        → S0505 (archive pipeline)
S0501+S0502+S0503+S0504+S0505 → S0506 (CQRS/ClusterUiCache)
S0506 → S0507 (IOS remote panel)
```

BATCH-26 will implement S0501 + S0502 as the foundation pair (ImGui overhaul + real network fan-out fix).
