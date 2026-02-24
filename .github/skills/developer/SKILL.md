---
name: developer
description: Workflow guide for developers working on batch-based tasks. Use when: starting work on a batch instruction file, implementing features from batch specifications, writing a batch report after completing work, handling review feedback (changes required / approved / rejected), asking clarifying questions to the development lead, or understanding quality standards for code and tests in this project.
---

# Developer Workflow Guide

Implement features through a structured batch system. Each batch is self-contained with complete
instructions. **Read everything before writing any code.**

## Folder Structure

```
.dev-workstream/
├── batches/BATCH-XX-INSTRUCTIONS.md    # Your assignment
├── reports/BATCH-XX-REPORT.md          # Your submission
├── questions/BATCH-XX-QUESTIONS.md     # If you need clarification
└── reviews/BATCH-XX-REVIEW.md          # Feedback from development lead
```

## Workflow

### Step 1: Receive & Read (30–60 min before coding)

1. Read `batches/BATCH-XX-INSTRUCTIONS.md` **completely** — every section matters
2. Read all referenced documents (design docs, previous reviews, architecture)
3. Review existing code in the primary work area
4. Check the previous batch review (if referenced) — learn from prior feedback
5. Note any ambiguities

### Step 2: Plan

- Identify all tasks and their dependencies
- Determine required tests for each task
- Note unclear requirements — ask questions early, not mid-implementation

### Step 3: Implement

- One task at a time — test as you go
- Follow TDD when practical: failing test → implement → refactor
- Study existing patterns first; match existing style and architecture
- XML comments on public APIs; inline comments for complex logic

```bash
dotnet test [relative/path/to/tests/]    # Run after each task
```

### Step 4: Handle Questions

**Ask when:**
- Spec is ambiguous or contradictory
- Integration point with existing code is unclear
- Architectural decision is required
- You discover a fundamental design issue

**Don't ask about:** implementation details within spec, code style, basics that can be researched.

Create `.dev-workstream/questions/BATCH-XX-QUESTIONS.md` and notify the development lead.
Work on other tasks while waiting if possible.

### Step 5: Self-Review

Before submitting:
- [ ] All tasks implemented per spec
- [ ] No compiler warnings or errors
- [ ] Public APIs have XML documentation
- [ ] Tests verify actual behavior (not just that code compiles)
- [ ] Edge cases from spec are covered
- [ ] Negative cases tested
- [ ] No TODOs, FIXMEs, or commented-out code
- [ ] Performance: no `new` in hot paths, no LINQ in simulation loops

### Step 6: Submit Report

```bash
cp .dev-workstream/templates/BATCH-REPORT-TEMPLATE.md \
   .dev-workstream/reports/BATCH-XX-REPORT.md
```

Fill **every section** — see [references/report-guide.md](references/report-guide.md) for standards.
Then notify the development lead.

## Definition of Done

**Code:** All tasks implemented · no compiler warnings · public APIs documented · error handling present  
**Tests:** All passing · verify actual behavior · edge cases covered · negative cases present  
**Report:** Every section complete · design decisions explained · deviations documented · known issues listed  
**Process:** Committed to version control · no WIP or debug code

## Handling Review Feedback

| Verdict | Action |
|---|---|
| **APPROVED** | Move on to next batch |
| **APPROVED WITH NOTES** | Apply learnings in next batch |
| **CHANGES REQUIRED** | Fix listed issues · re-run tests · update report · resubmit |
| **REJECTED** | Corrective batch issued — treat as a new batch assignment |

Feedback is about code, not about you. Explain your reasoning if you disagree. Focus on the best solution.

## Code Standards

See [references/code-standards.md](references/code-standards.md) for project-specific rules:
no magic numbers, SimMath for rotations, ECS mutation patterns, zero-allocation hot path.
