# Test-Health: Fast-Run Convention

## Fast-Run Filter

Run the hot suites with this filter to get a clean green result (0 failed):

```
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"
dotnet test Hrot/Subsystems/Hrot.SimHost.Tests --filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"
```

## Trait Convention

Genuinely-unstable tests are marked with `[Trait("Stability", "<bucket>")]` plus an inline comment:

```csharp
// STABILITY(<bucket>): <reason> — <resolution/target>
[Trait("Stability", "<bucket>")]
[Fact]
public void MyTest() { ... }
```

### Buckets

| Bucket | When to use |
|--------|-------------|
| `Flaky` | Intermittent failure: timing, zero-alloc GC, order-dependent static state, parallel interference. Passes ≥1 of 3 runs. |
| `Environment` | Deterministic failure that is environment-bound: locale, CRLF, off-main-thread, specific OS/runtime. |
| `Broken` | Deterministic failure that looks like a real bug or stale test. NOT cheap to fix. Stays visible in ledger as a follow-up target. |

## Ledger

See [`TEST-HEALTH.md`](TEST-HEALTH.md) for the full table of every marked or fixed test.

## Rules

1. **Do NOT delete tests** to make the suite green.
2. **Flaky** claims require 3× run evidence (passes in isolation, fails in full suite, or intermittent).
3. **Broken** is the honest bucket for real bugs — do NOT mark as Flaky to hide them.
4. **Fixed** tests must pass for the right reason (no weakened assertions).
5. When adding new tests, avoid ComponentId values that conflict with production IDs (see `GlobalComponentIds.cs` and `NavFakeIds.cs`). Safe test-only range: **291–299**.
