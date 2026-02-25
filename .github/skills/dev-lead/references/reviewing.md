# Batch Review Guide

## Review Process

Total time: 1–1.5 hours per batch.

### Step 1: Read the Report (5–10 min)

Check for:
- [ ] All tasks marked complete
- [ ] Test results included (with passing count)
- [ ] Issues encountered documented
- [ ] Design decisions made documented

**Red flags:**
- No issues or decisions mentioned (likely incomplete work)
- Test counts with no description of what they test
- Missing required sections

### Step 2: Review Code Changes (20–30 min)

```bash
git diff --stat        # See what changed
git diff               # View actual changes
```

Look for:
- ❌ Incomplete implementation (missing features from spec)
- ❌ Architectural violations
- ❌ Compiler warnings
- ❌ Missing error handling
- ❌ Obvious performance issues (new allocations on hot path)
- ❌ Unhandled edge cases from spec

**Code Standards checks** (`CODE-STANDARDS.md`):
- ❌ Magic numbers in production code — all literals must be named constants (§1)
- ❌ Raw `Quaternion.CreateFromYawPitchRoll` or `System.Numerics` quaternion math — use `SimMath` (§2)
- ❌ `GetComponentRW` called from async/background context — use command buffer (§3)
- ❌ `new` allocations or LINQ inside `OnUpdate` loops (§4)
- ❌ Managed reference fields on ECS components or missing `[StructLayout(LayoutKind.Sequential)]` (§5)

### Step 3: Review Tests — CRITICAL (15–20 min)

**⚠️ ALWAYS VIEW THE ACTUAL TEST CODE. Never trust test names or counts.**

Use your file viewing tools on test files. Read the actual assertions.

Also apply the **mandatory test quality questions** from `CODE-STANDARDS.md` §0 — key questions:
- Does it assert the specific field/value that matters, or just "no exception"?
- Does it verify the full chain, not just isolated units?
- Does it distinguish "fired" from "fired correctly"?
- When a named constant exists in production code, does the test reference it?
- Is there a negative case for every positive case?
- Does the test catch a realistic regression, or only the implementation as written?

**Common test quality failures to reject:**

❌ **String Presence Tests** (most common mistake):
```csharp
// WRONG — just checks a string exists in generated output
Assert.Contains("public int Id;", generatedCode);
Assert.Contains("Marshal array Numbers", marshallerCode);
// Code could be completely broken but test passes
```

❌ **Shallow Tests**:
```csharp
// WRONG — tests nothing meaningful
var component = new NetworkSpawnRequest();
Assert.NotNull(component);
```

❌ **Missing Coverage**: Required scenarios from spec not tested at all.

✅ **Good Tests**:
```csharp
// GOOD — compiles generated code, invokes it, checks ACTUAL runtime values
var assembly = CompileToAssembly(code, nativeCode);
var marshaller = Activator.CreateInstance(marshallerType);
method.Invoke(marshaller, args);
Assert.Equal(42, actualValue);  // actual runtime value verified
```

**Test Quality Checklist:**
- [ ] Tests verify ACTUAL values, not just string presence
- [ ] Tests would catch a broken implementation
- [ ] Edge cases from spec are covered
- [ ] Negative cases present (e.g., "valid channel is NOT cleared")
- [ ] No "object exists" / "no exception" tests (unless truly the whole contract)
- [ ] Generated code is compiled and executed (if applicable)
- [ ] Actual field values, offsets, sizes are asserted (if applicable)

**⚠️ If test quality is poor: REJECT the batch immediately.**

**Quality vs Quantity:** Test count says nothing. Analyze coverage. Do not demand more tests
if existing tests already cover the full contract with depth.

### Step 4: Check Completeness (5–10 min)

Compare batch instructions to implementation:
- [ ] All features from spec implemented
- [ ] All acceptance criteria met
- [ ] All edge cases from spec handled

### Step 5: Run Tests (5 min)

```bash
dotnet test [relative/path/to/tests/]
```

Verify all tests pass. Identify any flaky tests.

---

## Writing the Review

Create: `.dev-workstream/reviews/BATCH-XX-REVIEW.md`

**Principles:**
- **Brief** — max ~100 lines. No praise, no fluff.
- **Issue-focused** — document problems only. Skip "good job" sections.
- **Specific** — point to exact files, line numbers, test gaps.
- **Actionable** — developer knows exactly what to fix.

```markdown
# BATCH-XX Review

**Status:** ✅ APPROVED / ⚠️ NEEDS FIXES / ❌ REJECTED

## Summary

[1–2 sentences: what was done, overall status]

---

## Issues Found

[If none: write "No issues found." and skip to Commit Message]

### Issue 1: [Brief Title]

**File:** `relative/path/to/file.cs` (Line X)
**Problem:** [What's wrong]
**Fix:** [What needs to change]

### Issue 2: [Test Coverage Gap]

**Missing Tests:**
- [Specific scenario not tested]
- [Edge case not covered]

**Why It Matters:** [Impact of missing coverage]

---

## Verdict

**Status:** APPROVED / NEEDS FIXES / REJECTED

[If NEEDS FIXES — Required Actions:]
1. [Specific fix]
2. [Specific fix]

---

## 📝 Commit Message

[Only when APPROVED — see tracking.md for format]
```

**Do NOT include sections for:**
- ❌ Strengths / "What went well"
- ❌ "Excellent work" commentary
- ❌ Long explanations of what was done

---

## Issue Severity

| Level | Meaning | Action |
|---|---|---|
| P0 Critical | Crashes, security, safety | Fix before anything else |
| P1 High | Correctness, architecture, test quality | Must fix — Corrective Task 0 next batch |
| P2 Medium | Maintainability, performance, edge cases | Add to DEBT-TRACKER.md |
| P3 Low | Style, suggestions | Add to DEBT-TRACKER.md |

---

## Review Quality Examples

❌ **Vague:**
> "Tests are not good enough."

✅ **Specific:**
> "Test coverage insufficient:
> - `NetworkSpawnerSystem_Creates_Entity` only checks entity exists, doesn't verify components
> - Missing: What happens when TKB template is missing? (should log error)
> - Missing: Null entity reference handling
> Add these 3 tests before approval."

❌ **Unnecessary praise:**
> "Great work on the state machine! The code is very clean."

✅ **Brief, issue-focused:**
> "No issues found. Ready to merge."
