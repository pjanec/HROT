# Batch Writing Guide

## Batch Instruction File Structure

Every `batches/BATCH-XX-INSTRUCTIONS.md` must follow this structure:

```markdown
# BATCH-XX: [Feature Name]

**Batch Number:** BATCH-XX
**Tasks:** TASK-ID1, TASK-ID2  (list which TASK-DEFINITIONS.md tasks this batch completes)
**Phase:** [Phase Name]
**Estimated Effort:** [hours]
**Priority:** HIGH / MEDIUM / LOW
**Dependencies:** [Previous batches required, or "None"]

---

## 📋 Onboarding & Workflow

### Developer Instructions
[Brief introduction to this batch's goals — 2–3 sentences]

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/guides/DEV-GUIDE.md`
2. **Task Definitions:** `[path/to/TASK-DEFINITIONS.md]` — See TASK-ID1, TASK-ID2
3. **Design Document:** `docs/[relevant-design-doc].md` — Section X.Y
4. **Previous Review:** `.dev-workstream/reviews/BATCH-XX-REVIEW.md`

### Source Code Location
- **Primary Work Area:** `[relative/path/to/main/code]`
- **Test Project:** `[relative/path/to/tests]`
- **Build Command:** `dotnet test [relative/path/to/tests/]`

### Report Submission
Submit to: `.dev-workstream/reports/BATCH-XX-REPORT.md`
Questions to: `.dev-workstream/questions/BATCH-XX-QUESTIONS.md`

---

## Context

[How this batch fits into the larger picture]

---

## 🎯 Batch Objectives

[What this batch accomplishes and why it matters]

---

## ✅ Tasks

### Task 1: [Task Name] (TASK-ID1)

**File:** `relative/path/to/file.cs`  (NEW FILE / UPDATE / REFACTOR)
**Task Definition:** See [TASK-DEFINITIONS.md](../TASK-DEFINITIONS.md#task-id1-name)
**Design Reference:** `docs/DESIGN.md` — Section X.Y "[Section Name]"

**Description:** [What needs to be done]

**Requirements:**
- [Specific requirement 1]
- [Specific requirement 2]

**Tests Required:**
- ✅ [Specific test scenario 1]
- ✅ [Specific test scenario 2]
- ✅ [Edge case]

### Task 2: [Task Name] (TASK-ID2)
...

---

## 🧪 Testing Requirements

[Minimum test counts, test categories, specific scenarios to cover]

---

## ⚠️ Quality Standards

**TEST QUALITY — NOT ACCEPTABLE:**
- Tests that only verify "can I create this object"
- `Assert.Contains` on generated strings without verifying runtime correctness

**TEST QUALITY — REQUIRED:**
- Tests that verify actual behavior and values
- Edge cases from spec
- Negative cases (e.g., "invalid input is rejected", "stale channel is cleared but valid one is not")

---

## 📊 Report Requirements

**Q1:** What issues did you encounter during implementation? How did you resolve them?
**Q2:** What design decisions did you make beyond the instructions? What alternatives did you consider?
**Q3:** Did you spot any weak points in the existing codebase? What would you improve?
**Q4:** What edge cases did you discover that weren't in the spec?
**Q5:** Any performance concerns or optimization opportunities?

---

## 🎯 Success Criteria

- [ ] TASK-ID1 completed — [specific criteria]
- [ ] TASK-ID2 completed — [specific criteria]
- [ ] All tests passing (`dotnet test`)
- [ ] Report submitted

---

## ⚠️ Common Pitfalls

[Known issues or mistakes to watch for in this specific batch]

---

## 📚 Reference Materials

- **Task Defs:** `[path/to/TASK-DEFINITIONS.md]`
- **Design:** `docs/[design-doc].md` — Section X.Y
```

---

## Rules for Good Batch Instructions

### 1. Reference Task IDs
Every batch must identify which `TASK-DEFINITIONS.md` task IDs it completes.
Tasks are stable (what to build). Batches are dynamic (how you group work).

### 2. Paths — Explicit and Precise
All paths must be relative to the repository root. Include exact paths to:
- Source files to modify/create
- Test projects
- Build/test commands
- Design documents (with section names)

Never use vague references like "the test project" — give the relative path.

### 3. Do Not Duplicate Design Docs
Reference design documents by chapter name and section. The developer will read them.
Copy content only when a specific snippet is needed for clarity.

### 4. Complete Onboarding Every Batch
Different developers may work on different batches. Always include the full onboarding
section with Required Reading and Source Code Location — even if it repeats from prior batches.

### 5. Specify Test Quality, Not Just Quantity
- ✅ Good: "Test that Ghost entities are excluded from standard queries"
- ❌ Bad: "Write tests for Ghost entities"

### 6. Combined Batches — Mandatory Workflow Section

When a batch combines corrective work + new features, include this verbatim:

```markdown
## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: Complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until current task implementation is complete
and ALL tests are passing (including previous batch tests).
```

### 7. Report Questions — Capture Insights, Not Understanding

**Ask about:**
- Issues encountered and how they were resolved
- Design decisions made beyond the spec
- Weak points spotted in the codebase
- Edge cases discovered during implementation

**Never ask:**
- "Explain how X works" (comprehension test)
- "What is the purpose of Y?" (understanding check)
