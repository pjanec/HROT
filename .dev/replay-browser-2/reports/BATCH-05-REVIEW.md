# BATCH-05 Review

**Verdict: CHANGES REQUIRED**

Tests confirmed genuine:
- SR-T37 (authority): directly manipulates `EntityHeader.AuthorityMask.SetBit(typeId)` and verifies RequireAuthority / RequireGhost / Any separately -- correct.
- SR-T38 (event timing): fires a real event, replays, asserts count >= 1 -- genuine.
- SR-T34 (zero-alloc): `StepForward` correctly moved outside measurement window, only `QueryDelta` body measured -- genuine.
- SR-T23..SR-T27 (event scanner): replay-based, fire real events at specific frames, assert results at correct frames -- genuine.
- SR-T14..SR-T18 (structural): use `AddComponent` / `RemoveComponent` via harness, check "Gained"/"Lost" context messages -- genuine.
- SR-T05..SR-T08 (compound): real entity setups, legitimate AND/OR logic checks -- genuine.
- SR-T01a..SR-T01i (serialization): full round-trip through JSON for all DTO subtypes -- genuine.

## Issues Found

### Issue 1: SR-T09 -- COMPLETELY MISSING

SR-T09 is in the BATCH-05 checklist ("SR-T02..SR-T09: component property and compiler tests pass")
and in DESIGN.md:
> SR-T09: `QueryDelta` chunk skipping: harness fills 64 KB-worth of stationary entities + one mutating
>  entity per frame; with profiling spy, the inner loop visits exactly the mutating entity each frame.

No test with name SR_T09 exists anywhere in the codebase. The optimization being tested
(EntityRepository.QueryDelta visits only changed entities, not all stationary ones) is still present
in EntityRepository and is important -- Fix 3 even improved QueryDelta's allocation behavior.

**Required fix**: Add SR-T09 as a unit test for `EntityRepository.QueryDelta` directly.
Place it in a new file `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Search/QueryDeltaChunkSkipTests.cs`
or append to PredicateCompilerTests.cs. The test must:
- Use FdpRecordingHarness to build a recording with >= 50 stationary entities and exactly 1 mutating entity per delta frame.
- Replay via `PlaybackController` + `EntityRepository` directly (not via RecordingSearchService).
- Build an `EntityQuery` targeting only the mutating entity's component.
- Wrap the call-to-entity collect Action with a counter to count actual entity visits per frame.
- Assert that visit count per frame is exactly 1 (only the mutating entity is visited), not 50+.

### Issue 2: SR-T35 -- Weaker than DESIGN specification

DESIGN.md:
> SR-T35: short-circuit AND: a deliberately-expensive evaluator placed second in an AND chain is not
> invoked when the first leaf returns false (verified via call-count spy).

Current implementation just asserts `result == false` -- it does NOT verify the second condition
was not invoked. A correct AND implementation where both conditions are evaluated but the second
returns true would still pass this test even with no short-circuit.

**Required fix**: Replace the body of `SR_T35_CompoundAnd_ShortCircuit_SecondNotCalledWhenFirstFails`
to use a call-count spy. Since `CompileComponentPredicate` takes `SearchPredicateDto`, the spy
must wrap an existing predicate type. Approach:
- Create a `CountingPredicateDto` test helper that records invocation count (or use a closure-captured int via a custom `IPropertyEvaluator` replacement).
- Alternative: verify via a `PropertyMatchDto` that targets a component the entity does NOT have --
  count how many times the evaluator's `GetValueAsString` is called using a testable wrapper.
- Simplest feasible approach: Use a local `int secondCallCount = 0` counter and inject a spy
  via a subclass of `PredicateCompiler` that overrides the inner lambda (if feasible), OR
  assert that the number of evaluations matches expected (use reflection on evaluator call count).
- If the above is too invasive: at minimum, add a separate entity that HAS both components but
  fails the first condition, and use `Assert.Equal(0, secondCallCount)` where the count is tracked
  via a counting `IPropertyEvaluator` mock passed to a testable `PredicateCompiler` subclass.

The simplest correct approach that does NOT require architecture changes:
1. Expose a testable `CompileComponentPredicateWithSpy(predicate, out int[] callCounts)` overload
   (or add a `Func<EntityRepository, Entity, bool>` factory delegate to `PredicateCompiler` that
   accepts a per-leaf call-count array).
2. OR: Keep the test as-is but add an ADDITIONAL assertion that checks a captured counter value.

The most practical approach: use a `List<int>` reference captured by the lambda approach.
Create the AND compound such that the first condition uses a `PropertyMatchDto` pointing to a
field that evaluates to false. Count invocations of the second by wrapping it in a closure counter.
Since `PredicateCompiler.CompileComponentPredicate` builds closed-form lambdas, we can't spy
directly. Instead: verify via a second entity that DOES pass the first condition but fails the
second -- show total results == 0, and additionally prove the second WAS evaluated for that entity
(and NOT for the first entity). This proves short-circuit for the failing case.

Pragmatic requirement for BATCH-05C:
Replace SR-T35 to:
- Spawn TWO entities:
  - Entity A: X = 1 (fails first condition X > 40), no HarnessVelocity.
  - Entity B: X = 50 (passes first condition X > 40), Vx = 1 (fails second Vx > 2).
- Both should NOT match the AND predicate.
- For entity A: the second predicate should NOT be evaluated (short-circuit).
- Verify via a spy: `_compiler` must be modified OR use a `PropertyEvaluator` with a counting wrapper.
- The test must assert `secondCallCount` (or equivalent counter) is 0 for entity A.
