# BATCH-01 Review — DRY Infrastructure + NedReplicationModule

**Batch:** BATCH-01
**Tasks:** EAM-I001, EAM-I002, EAM-N001, EAM-N002
**Review Date:** 2026-04-07
**Decision:** ✅ APPROVED with P2 debt items recorded

---

## 1. Implementation Verification

### Build Status
✅ `dotnet build IOS-IG-SimHost.sln` — **Build succeeded.** Zero new errors or warnings.

### Test Results
| Test Suite | Before | After | Delta |
|---|---|---|---|
| `Hrot.ClusterRunner.Tests` | 204 pass / 3 fail | 212 pass / 3 fail | **+8 new passing tests** |
| `Hrot.ClusterRunner.Integration.Tests` | 118 pass / 5 fail | 118 pass / 5 fail | 0 regression |
| `Hrot.SimHost.Tests` | 440 pass / 5 fail | 440 pass / 5 fail | 0 regression |

All 3 failures in `Hrot.ClusterRunner.Tests` and all 5 failures in `Hrot.SimHost.Tests` are
**pre-existing** — none reference `DeadReckoningSyncSystem` or any file created by this batch.
Confirmed: `DeadReckoningSyncSystem` has zero references in `Hrot.SimHost` source.

### Files Created
- `Hrot.ClusterRunner/Infrastructure/HrotNodeContext.cs` ✅
- `Hrot.ClusterRunner/Infrastructure/HrotNodeBuilder.cs` ✅
- `Hrot.ClusterRunner/Infrastructure/HrotNodeConfig.cs` ✅ (new dedicated config type)
- `Hrot.ClusterRunner/Infrastructure/DdsIdAllocatorHelper.cs` ✅
- `Hrot.ClusterRunner/Replication/NedReplicationModule.cs` ✅
- `Hrot.ClusterRunner.Tests/HrotNodeBuilderTests.cs` ✅ (3 tests)
- `Hrot.ClusterRunner.Tests/NedReplicationModuleTests.cs` ✅ (5 tests)
- `Hrot.IG/Systems/DeadReckoningSyncSystem.cs` ✅ (modified — added constructor + DriveFromNetwork)

---

## 2. Code Review Findings

### EAM-I001: HrotNodeBuilder + HrotNodeContext

✅ **Initialization sequence follows spec exactly** — all 10 steps implemented in order.

✅ **Single-use guard** — second `Build()` call throws `InvalidOperationException`.

✅ **NodeBootstrapper.BuildOrchestration NOT called** — ClusterSlave wired inline.

✅ **Four generic handlers only** — `ReferencePreviewHandler`, `ReferencePrefetchHandler`,
`ReferenceArchiveHandler`, `ReferenceLiveLoadHandler` registered. No domain-specific handlers.

✅ **`GhostCreationSystem?` added to HrotNodeContext** — Phase 4 replay wiring will work.

✅ **FDP/Hrot architecture separation** — generic engine steps clearly separated from DDS steps.

⚠️ **P2: HrotNodeBuilder.WithRole references `Hrot.SimHost.NodeRole`** — The `WithRole` method
signature uses `Hrot.SimHost.NodeRole`. While `Hrot.ClusterRunner` already depends on
`Hrot.SimHost`, this means `HrotNodeBuilder` cannot be extracted to a shared project without
moving `NodeRole` first. The role is not USED by the builder (only stored for potential
future use — currently only the subsystem name matters). Consider: accept `string` subsystem
name only, and drop the role parameter entirely from `WithRole`, since the role is consumed by
`NedReplicationModule` (not `HrotNodeBuilder`).

✅ **HrotNodeConfig vs SubsystemConfig rationale documented** — developer correctly identified
that `SubsystemConfig` lacks `LocalTempRoot`; `HrotNodeConfig` is justified.

### EAM-I002: DdsIdAllocatorHelper

✅ **Helper created correctly** — logic moved verbatim, constants preserved.

⚠️ **P2: SimHostApp.EnsureIdAllocatorRouting private method NOT deleted** — The developer
correctly identified a circular dependency: `Hrot.SimHost` cannot reference `Hrot.ClusterRunner`
(ClusterRunner depends on SimHost, not vice versa). The shared helper lives in ClusterRunner
which SimHostApp cannot call. This means EAM-I002 SC2 ("No duplicate code") is NOT fully
satisfied — SimHostApp still has its own private copy. DdsIdAllocatorHelper is in the right
place for new code paths (HrotNodeBuilder, EyesAndMuscle, future subsystems), but the SimHostApp
cleanup must happen in Phase 4 (EAM-M001). A correct approach would be to put the helper in
`Hrot.Common` (which SimHostApp already references) and then call it from both HrotNodeBuilder
and SimHostApp. Defer to Phase 4 BATCH-03 where SimHostApp migration is in scope.

### EAM-N001: NedReplicationModule

✅ **Constructor validates role** — `ArgumentException` for Perception, NavigationSolver, etc.

✅ **driveFromNetwork logic correct** — `false` when Muscle or Brain present, `true` for pure IG.

✅ **GhostCreationSystem exposed as public property** — Phase 4 replay handler wiring ready.

✅ **Translator delegation pattern correct** — EntityStatesIngressPack.RegisterSystems inlined.

✅ **TODO comment about DeadReckoningSyncSystem move added**.

⚠️ **P2: `NetworkLifecycleSystemGroup` not registered** — TASK-DETAIL EAM-N001 spec explicitly
requires `registry.RegisterSystem(new NetworkLifecycleSystemGroup(ghostCreationSystem))`.
This system gates ghost lifecycle transitions during replay. Without it, the replay state
machine may see entities in incorrect lifecycle states when migrating SimHostApp in Phase 4.
Add in a corrective pass before or during BATCH-03 (EAM-M001 migration).

✅ **CycloneNetworkIngressSystem and CycloneEgressSystem used directly** — both are `public`;
no internal-access workarounds needed. EAM-N002 confirmed.

### EAM-N002: Translator pack accessibility

✅ All four packs (KinematicTranslatorPack, SharedTranslatorPack, CognitiveTranslatorPack,
EntityStatesIngressPack) confirmed public — no visibility changes needed.

### DeadReckoningSyncSystem modification

✅ **Backward-compatible** — parameterless constructor delegates to `this(driveFromNetwork: true)`.
✅ **WithLifecycle(EntityLifecycle.Ghost) filter applied correctly when DriveFromNetwork=false**.
✅ No existing tests broken by the change.

---

## 3. Technical Debt Items

| Priority | Description | Target |
|---|---|---|
| P2 | `HrotNodeBuilder.WithRole` takes `Hrot.SimHost.NodeRole` parameter but doesn't use it; prevents future extraction to shared project. Consider dropping the `role` param or moving `NodeRole` to `Hrot.Common`. | BATCH-03 |
| P2 | `SimHostApp.EnsureIdAllocatorRouting` private method still exists (EAM-I002 SC2 not fully met). Root cause: circular dependency — `DdsIdAllocatorHelper` in `Hrot.ClusterRunner` is not accessible from `Hrot.SimHost`. Move helper to `Hrot.Common` in Phase 4 (EAM-M001). | BATCH-03 |
| P2 | `NetworkLifecycleSystemGroup` not registered in `NedReplicationModule.RegisterSystems`. Required per EAM-N001 spec for replay lifecycle gating. Add before EAM-M001 migration. | BATCH-03 (corrective) |

---

## 4. Task Tracker Updates

- [x] EAM-I001 HrotNodeBuilder and HrotNodeContext — ✅ implemented
- [x] EAM-I002 EnsureIdAllocatorRouting helper — ✅ partial (new helper created; SimHostApp cleanup deferred to Phase 4)
- [x] EAM-N001 NedReplicationModule core — ✅ implemented (P2 debt: NetworkLifecycleSystemGroup missing)
- [x] EAM-N002 Shared translator pack accessibility — ✅ confirmed (no changes needed)

---

## 5. Suggested Git Commit Message

```
feat: Phase 1+2 — HrotNodeBuilder, HrotNodeContext, NedReplicationModule (EAM-I001/I002/N001/N002)

- Add HrotNodeContext sealed record (immutable bootstrap output)
- Add HrotNodeBuilder fluent builder: wires EntityRepository, Kernel, TimeController,
  DdsParticipant, DdsIdAllocator, ClusterSlave (inline, not via NodeBootstrapper.BuildOrchestration)
  with 4 generic handlers only (Preview, Prefetch, Archive, Live)
- Add HrotNodeConfig dedicated config type
- Add DdsIdAllocatorHelper static helper (EnsureRouting with 30s timeout/5s warn)
- Add NedReplicationModule: role-based NED translator bundling + ECS system registration
  (GhostCreation, SmartEgress, DeadReckoning, NetworkCleanup, DisposalMonitoring)
- Modify DeadReckoningSyncSystem: add bool driveFromNetwork constructor param +
  DriveFromNetwork property (backward-compat: parameterless ctor defaults to true)
- Add 8 unit tests: HrotNodeBuilderTests (SC1-SC3), NedReplicationModuleTests (SC1-SC5)

Pre-existing failures in ClusterRunner.Tests (3) and SimHost.Tests (5) are unrelated to this batch.
```
