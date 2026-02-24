# Report Quality Guide

## What Makes a Good Report

A batch report is how the development lead understands your work. It captures professional
insights — not comprehension. Write it as a skilled developer documenting what happened.

**Complete:** Every section filled · all specific questions answered · full test output · known issues listed  
**Detailed:** Design decisions explained · challenges described · integration notes thorough  
**Honest:** Limitations acknowledged · deviations documented · uncertainty flagged  

---

## Required Report Sections

1. **Implementation Summary** — what you built (brief overview)
2. **Design Decisions** — choices YOU made beyond the spec and why
3. **Deviations** — any changes from instructions (with rationale)
4. **Test Results** — full output, not just "passing"
5. **Challenges** — what was hard and how you solved it
6. **Integration Notes** — how this fits with the rest of the system
7. **Known Issues** — limitations or concerns

**"N/A" is not acceptable** unless the batch instructions explicitly say so.

---

## Answering Questions in the Report

When batch instructions include specific questions, answer thoroughly.

**❌ Bad:**
> "I changed the lifecycle state."

**✅ Good:**
> "The transition happens in `NetworkSpawnerSystem.ProcessSpawnRequest()`:
> 1. Check current state (Ghost or new entity)
> 2. If Ghost, set `preserveExisting=true` when applying TKB template (preserves Position from EntityState)
> 3. Call `repo.SetLifecycleState(entity, EntityLifecycle.Constructing)`
> 4. Call `elm.BeginConstruction()` to start ELM coordination
>
> Key consideration: Must preserve Position from Ghost — if template is applied without `preserveExisting`,
> it overwrites position with template default (0,0,0).
>
> Test coverage: `Test_GhostPromotion_PreservesPositionFromNetworkState` verifies this."

---

## Documenting Deviations

Deviations are acceptable — and expected — when properly documented.

```markdown
### Deviation 1: Used Strategy Pattern Instead of Direct Implementation

**What:** Implemented `IOwnershipStrategy` interface instead of hardcoded logic
**Why:** Original design placed ownership rules in `NetworkSpawner`, making them hard to test
**Benefit:** Testable (can mock strategy), flexible (swappable at runtime), follows existing patterns
**Risk:** One additional level of indirection
**Recommendation:** Keep this approach — fits architecture better
```

Always document: WHAT you changed · WHY · BENEFIT · RISK

---

## Test Output

Include the full `dotnet test` output — not just "all tests pass":

```
Test Run Successful.
Total tests: 23
     Passed: 23
     Failed: 0
      Total time: 1.843s
```

If any tests fail, explain why and whether it is known/expected.

---

## Example: Specific Insights the Lead Wants

**Issues encountered:**
> "Hit a race condition in `EntityStateTranslator` when processing concurrent network events.
> Fixed by moving entity creation into the command buffer instead of direct world mutation.
> Discovered that `ISimulationView.GetComponentRW` is intentionally absent — took 30 min to find
> the `IEntityCommandBuffer` pattern in existing code."

**Weak points spotted:**
> "`NetworkSpawnRegistry` has no capacity limit — it will grow unbounded. Flagged as a tech debt item."

**Edge cases discovered:**
> "What happens when a Ghost entity receives a second `EntityState` before it is promoted?
> Spec doesn't address this. Current implementation silently drops the duplicate. Should this be logged?"
