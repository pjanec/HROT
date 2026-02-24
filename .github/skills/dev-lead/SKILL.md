---
name: dev-lead
description: Workflow guide for a Development Lead managing batch-based software development. Use when: writing batch instructions for a developer, reviewing a completed batch report, assessing test quality in a review, creating corrective batches for failed/partial reviews, updating TASK-TRACKER.md or TASK-DEFINITIONS.md, generating a git commit message after batch approval, planning how to group tasks into batches, or maintaining the technical debt tracker.
---

# Development Lead Guide

Manage implementation work through a structured batch system. Each batch may be executed by a
**different developer** — always write complete, self-contained instructions.

## Responsibilities

1. Plan work — group tasks from `TASK-DEFINITIONS.md` into batches
2. Write batch instructions (`batches/BATCH-XX-INSTRUCTIONS.md`)
3. Review completed batches (code + tests)
4. Approve / request fixes / reject
5. Maintain `TASK-TRACKER.md` and `DEBT-TRACKER.md`
6. Generate commit messages after approval

## Folder Structure

```
.dev-workstream/
├── TASK-DEFINITIONS.md          # Stable atomic task specs (you maintain)
├── TASK-TRACKER.md              # Brief checklist (you maintain after each review)
├── DEBT-TRACKER.md              # P2/P3 deferred issues (you maintain)
├── batches/
│   └── BATCH-XX-INSTRUCTIONS.md
├── reports/
│   └── BATCH-XX-REPORT.md       # Developer submissions
├── questions/
│   └── BATCH-XX-QUESTIONS.md    # Developer questions
└── reviews/
    └── BATCH-XX-REVIEW.md       # Your feedback
```

**DEBT-TRACKER rules:**
- P1 issue → Corrective Task 0 in next batch (never enters tracker)
- P2/P3 issue → Add to DEBT-TRACKER.md with source, description, target batch
- Resolved → Mark ✅ (never delete rows)

## Core Workflow

### Phase 1: Plan & Assign
1. Define/update tasks in `TASK-DEFINITIONS.md` (for new features only)
2. Group 4–10 hours of tasks into one batch
3. Write `batches/BATCH-XX-INSTRUCTIONS.md` — see [references/batch-writing.md](references/batch-writing.md)
4. Assign to developer

### Phase 2: Developer Works
- Be available for questions in `questions/BATCH-XX-QUESTIONS.md`
- Answer in the questions file; update batch instructions if ambiguity is revealed

### Phase 3: Review
When developer submits `reports/BATCH-XX-REPORT.md`:

1. Read report (5–10 min)
2. Review code changes (`git diff --stat`, then view changed files)
3. **View actual test code** — do NOT trust test names or counts
4. Check completeness against batch spec
5. Run tests (`dotnet test`)
6. Write `reviews/BATCH-XX-REVIEW.md`

Full review criteria, test quality standards, and review template:
→ **[references/reviewing.md](references/reviewing.md)**

### Phase 4: Decision

| Outcome | Action |
|---|---|
| **Approved** | Write review with approval · generate commit message · update TASK-TRACKER.md |
| **Needs Fixes** | List specific fixes in review · quick re-review after (15–30 min) |
| **Serious Issues** | Create Corrective Task 0 in next batch · mark affected tasks ⚠️ in tracker |

Commit message format and task tracking procedures:
→ **[references/tracking.md](references/tracking.md)**

## Batch Sizing

| Duration | Guidance |
|---|---|
| < 2 hours | Too small — combine with other work |
| 4–10 hours | Target range |
| > 12 hours | Split into multiple batches |

## Reference Files

- **[references/batch-writing.md](references/batch-writing.md)** — Batch structure template, onboarding section, writing rules
- **[references/reviewing.md](references/reviewing.md)** — Review process, test quality criteria (with failure examples), review template
- **[references/tracking.md](references/tracking.md)** — Task tracking system structure, commit message format
