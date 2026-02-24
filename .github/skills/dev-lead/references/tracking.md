# Task Tracking & Commit Messages

## Two Files You Maintain

### TASK-DEFINITIONS.md (Stable — update rarely)

Purpose: Atomic task definitions with unique IDs.

```markdown
## Phase X: [Phase Name]

### TASK-X01: [Task Name]
**Status:** ✅ DONE / ⚠️ PARTIAL / ⚪ TODO
**Deliverable:** [What this task produces]
**Design Ref:** [Link to design doc section]

**Scope:** [What this task covers]
**Constraints:** [Critical rules, e.g. "no managed allocations"]
**Current Issues:** [If partial/needs fixes — populated during reviews]
```

Update when:
- New feature → add new task definitions
- Architect decision changes scope → update constraints
- Review finds issues → add "Current Issues" section

### TASK-TRACKER.md (Dynamic — update after every review)

Purpose: Quick hierarchical checklist.

```markdown
# Task Tracker

**See:** [TASK-DEFINITIONS.md](TASK-DEFINITIONS.md) for details

## Phase D: [Phase Name]

- [x] **TASK-D01** ROM Enumerations → [details](TASK-DEFINITIONS.md#task-d01)
- [x] **TASK-D02** ROM State Definition → [details](TASK-DEFINITIONS.md#task-d02)
- [⚠️] **TASK-D09** Blob Container → [details](TASK-DEFINITIONS.md#task-d09) *needs fixes*
- [ ] **TASK-D10** Instance Manager → [details](TASK-DEFINITIONS.md#task-d10)

## Phase C: [Phase Name]

- [ ] **TASK-C06** Flattener → [details](TASK-DEFINITIONS.md#task-c06)

**Progress:** X done, Y needs fixes, Z remaining
```

Update when:
- Batch approved → mark completed task IDs as `[x]`
- Batch needs fixes → mark affected tasks as `[⚠️]`
- New tasks added → append to appropriate phase

---

## Commit Message Generation

**You generate commit messages. You do NOT run `git commit`.**

Provide the message in the review or as a separate comment:

```markdown
## 📝 Git Commit Message

When you commit this batch, use:

\`\`\`
[type]: [brief summary] (BATCH-XX)

Completes TASK-ID1, TASK-ID2, TASK-ID3

[2–3 sentence description of what changed]

[Component 1 (TASK-ID1):]
- [Key change 1]
- [Key change 2]

[Component 2 (TASK-ID2):]
- [Key change]

Tests: [X tests, covering Y and Z]

Related: TASK-DEFINITIONS.md, docs/[design-doc].md
\`\`\`
```

**Commit types:**

| Type | Use for |
|---|---|
| `feat:` | New feature |
| `fix:` | Bug fix |
| `refactor:` | Code restructure without behavior change |
| `test:` | Adding/improving tests |
| `docs:` | Documentation |
| `perf:` | Performance improvement |
| `chore:` | Maintenance (dependencies, config) |

**Example:**

```
feat: compiler flattener & emitter (BATCH-07)

Completes TASK-C06 (Flattener), TASK-C07 (Emitter), TASK-D09 (Blob fix)

Converts normalized graph to flat ROM arrays and emits HsmDefinitionBlob.

HsmFlattener (TASK-C06):
- BFS-ordered state flattening (cache locality)
- Hierarchy preserved via ParentIndex, FirstChildIndex, NextSiblingIndex
- LCA cost computation (Architect Q6)
- Dispatch table: ActionIds[], GuardIds[] sorted deterministically

HsmEmitter (TASK-C07):
- Header population (magic, counts, format version)
- StructureHash: topology only (stable across renames)
- ParameterHash: changes when logic changes

HsmDefinitionBlob (TASK-D09):
- Sealed, arrays now private readonly
- ReadOnlySpan<T> accessors exposed

Tests: 20 tests, covering flattening, emission, hash stability

Related: TASK-DEFINITIONS.md, Architect Q6, docs/hsm-design.md
```

---

## Quick Reference

```
Task Defs:    .dev-workstream/TASK-DEFINITIONS.md
Tracker:      .dev-workstream/TASK-TRACKER.md
Debt:         .dev-workstream/DEBT-TRACKER.md
Instruction:  .dev-workstream/batches/BATCH-XX-INSTRUCTIONS.md
Report:       .dev-workstream/reports/BATCH-XX-REPORT.md
Questions:    .dev-workstream/questions/BATCH-XX-QUESTIONS.md
Review:       .dev-workstream/reviews/BATCH-XX-REVIEW.md
```

**Batch numbering:** BATCH-01, BATCH-02, BATCH-03…  
**Parallel work (avoid):** BATCH-05a, BATCH-05b

**Time estimates:**
- Write batch: 1–2 hours (first), 30–45 min (with practice)
- Review batch: 1–1.5 hours (thorough)
- Quick re-review: 15–30 min (after minor fixes)
