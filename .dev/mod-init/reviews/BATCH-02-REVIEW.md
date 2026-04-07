# BATCH-02 Review

**Batch:** BATCH-02
**Tasks Reviewed:** MODINIT-S201, MODINIT-S202, MODINIT-S401
**Reviewer:** Dev Lead
**Date:** 2026-04-07
**Decision:** ✅ APPROVED (with noted design deviation — correct resolution)

---

## Verification Summary

### Build
`dotnet build IOS-IG-SimHost.sln` → **0 errors**. Confirmed independently.

### File Audit
| Check | Result |
|---|---|
| `Hrot.Network/Replication/NedReplicationModule.cs` exists | ✅ |
| `Hrot.ClusterRunner/Replication/NedReplicationModule.cs` deleted | ✅ |
| `Hrot.Common/Abstractions/INedReplicationModule.cs` exists | ✅ |
| `Hrot.Network/Infrastructure/HrotNodeBuilderReplicationExtensions.cs` exists | ✅ |
| No `.cs` files contain `using Hrot.ClusterRunner.Replication;` | ✅ (grep: 0 matches) |
| No stale `using Hrot.SimHost*` in NedReplicationModule.cs | ✅ (DEBT-002 resolved) |
| `CgfSubsystem._nedReplicationModule` field deleted | ✅ (grep: 0 matches) |

### Test Results
| Project | Passed | Pre-existing Failures | New Failures |
|---|---|---|---|
| `Hrot.ClusterRunner.Tests` | 152 | 0 | 0 (net increase of 12 tests from new S202 tests + updated S201 tests) |
| `Hrot.ClusterRunner.Integration.Tests` (CgfComponentRegistryTests) | 4 | 0 | 0 |

---

## Design Deviation: Wrapper-Type Pattern

**Decision: APPROVED** — developer's solution is correct.

The batch instructions proposed `Build(this HrotNodeBuilder builder)` as an extension method sharing the same name as the native `HrotNodeBuilder.Build()` instance method. The developer correctly identified a fundamental C# language constraint: **instance methods always take precedence over extension methods with the same signature**. The extension `Build()` would be unreachable in practice — the native instance method would always win.

The **wrapper-type pattern** (`HrotNodeBuilderWithReplication`) is architecturally superior:
- `HrotNodeBuilder.WithReplication(role)` (extension) returns `HrotNodeBuilderWithReplication`
- `HrotNodeBuilderWithReplication.Build()` is an instance method with no name conflict
- The type system enforces that `.Build()` must be called on the wrapper, not the raw builder
- This provides compile-time enforcement rather than runtime guard enforcement

The design intent (mandatory replication before Build) is satisfied at the type level, which is stronger than the proposed runtime guard approach.

**The `_replicationConfigured` and `_replicationRole` internal fields on `HrotNodeBuilder`** are now unused. They should be cleaned up in BATCH-03.

---

## Issues Found

### P1 — `EyesAndMuscleSubsystem` partially migrated
`Hrot.ClusterRunner/Services/EyesAndMuscleSubsystem.cs` was an undocumented caller of `NedReplicationModule` found during BATCH-02. The developer correctly updated its `using` directive to `Hrot.Network.Replication`. However, the subsystem still instantiates `NedReplicationModule` directly rather than using `.WithReplication(role).Build()`. This is a **P1 debt** because:
- It demonstrates the old pattern is still in use
- It was supposed to be migrated as part of the `mod-init` workstream
- BATCH-03 must include migration of `EyesAndMuscleSubsystem`

### P2 — `domainId: 0` in extension Build()
`HrotNodeBuilderWithReplication.Build()` passes `domainId: 0` to `NedReplicationModule`. Previously, `CgfSubsystem` passed `config.DomainId`. Currently `domainId` is reserved/unused in the module. If it becomes meaningful, the extension needs to be updated. Track as P2.

### P3 — Unused internal fields on HrotNodeBuilder
`_replicationConfigured` and `_replicationRole` are added but unused after the wrapper-type refactor. Clean up in BATCH-03.

### P3 — CgfSubsystem test uses reflection on context
`CgfSubsystemTests.cs` uses reflection to access `_context.NedReplication`. Should be a clean test-hook property (`internal INedReplicationModule? NedReplication => _context?.NedReplication`) on CgfSubsystem. Low priority.

---

## Developer Insights Extracted

**Key findings for BATCH-03:**
1. **EyesAndMuscleSubsystem** must be migrated to use `.WithReplication()` — include as new task
2. **`context.World.Bus` vs `context.EventBus`**: The extension correctly uses `context.World.Bus` (matching CgfSubsystem's documented pattern). Stage 3 callers (SimHostApp, IgApplication) must use the same bus or verify which is correct.
3. **`HrotNodeContext.NodeId` was added** as a new property to avoid depending on `ClusterSlave` internals — this is a clean design improvement.

---

## Debt Tracker Updates

| Action | Item |
|---|---|
| ✅ Resolved | DEBT-002: Stale `using Hrot.SimHost*` in NedReplicationModule |
| ADD | DEBT-004 (P1): `EyesAndMuscleSubsystem` still instantiates `NedReplicationModule` directly; needs migration to `.WithReplication()` — include in BATCH-03 |
| ADD | DEBT-005 (P2): `domainId: 0` hardcoded in `HrotNodeBuilderWithReplication.Build()` — safe now, risk if domainId becomes used |
| ADD | DEBT-006 (P3): Unused internal fields `_replicationConfigured`/`_replicationRole` on `HrotNodeBuilder` — clean up in BATCH-03 |

---

## Suggested Git Commit Message

```
feat(mod-init): Stage 2 - relocate NedReplicationModule to Hrot.Network + context wiring (MODINIT-S201/S202/S401)

- Move NedReplicationModule: Hrot.ClusterRunner.Replication -> Hrot.Network.Replication
- Remove stale 'using Hrot.SimHost' / 'using Hrot.SimHost.Network' (DEBT-002 resolved)
- Define INedReplicationModule interface in Hrot.Common/Abstractions/
- Add HrotNodeContext.NedReplication (INedReplicationModule?) and NodeId properties
- Create HrotNodeBuilderWithReplication wrapper type in Hrot.Network/Infrastructure/
  (wrapper pattern: correct solution to C# instance-method-beats-extension-method constraint)
- Add WithReplication(NodeRole) extension method on HrotNodeBuilder
- Update CgfSubsystem to use .WithReplication(NodeRole.Brain).Build(); delete _nedReplicationModule field
- Update EyesAndMuscleSubsystem namespace reference to Hrot.Network.Replication

Build: 0 errors. ClusterRunner.Tests: 152 passed. CgfComponentRegistryTests: 4 passed.
```
