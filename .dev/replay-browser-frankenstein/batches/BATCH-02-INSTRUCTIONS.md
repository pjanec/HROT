# BATCH-02: Subsystem Wiring + Synthesis Engine Core

**Batch Number:** BATCH-02
**Tasks:** RBF-P2T3, RBF-P3T1, RBF-P3T2, RBF-P3T3, RBF-P3T4, RBF-P3T6
**Phase:** P2 (complete) + P3 (partial)
**Estimated Effort:** 10-14 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 (FederatedReplayManager, RecordingMetadata with federation fields)

---

## Onboarding & Workflow

### Developer Instructions

BATCH-01 delivered the metadata layer and headless `FederatedReplayManager`. This batch:

1. Wires the subsystem to own a `FederatedReplayManager` instead of a bare `ReplayBrowserContext`.
2. Builds the synthesis engine primitives: `NetworkIdGuid` encoding, `FederatedGuidResolver`, `ScenarioSerializer.DeserializeWith`, consensus-mask helper, and the `PrimeAppDomainAndSandbox` shared helper.

These components underpin the Frankenstein synthesis pipeline (Phase P3). `TransientMasterBuilder` and local-entities injection (Phase P3 completion) come in BATCH-03.

### Required Reading (IN ORDER)

1. **Design:** `.dev/replay-browser-frankenstein/DESIGN.md`
   - §5 "Federation infrastructure" — P2T3 subsystem wiring
   - §6.1 "Single-Node view" — active context binding
   - §7.1–§7.5 "Frankenstein synthesis pipeline" — P3T1 through P3T6
2. **Task Details:** `.dev/replay-browser-frankenstein/TASK-DETAILS.md`
   - Tasks: RBF-P2T3, RBF-P3T1, RBF-P3T2, RBF-P3T3, RBF-P3T4, RBF-P3T6
3. **BATCH-01 Review:** `.dev/replay-browser-frankenstein/reviews/BATCH-01-REVIEW.md`

### Source Code Locations

| File | Role |
|---|---|
| `Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs` | Swap `_context` for `_manager` (P2T3) |
| `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/NetworkIdGuid.cs` | **NEW** — long ↔ Guid encoding (P3T1) |
| `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/FederatedGuidResolver.cs` | **NEW** — IGuidResolver with hot-swap maps (P3T2) |
| `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs` | Add `DeserializeWith(IGuidResolver)` overload (P3T3) |
| `FDP/Engine/Fdp.Core/BitMask512.cs` | Add `BitwiseAndNot` if missing (P3T4) |
| `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/ReplayBrowserContext.cs` | Extract priming flow (P3T6) |
| `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/RepositoryPriming.cs` | **NEW** — shared priming helper (P3T6) |

**Test projects:**
- `Hrot/Diagnostics/Hrot.ReplayBrowser.Tests/` or create `Hrot/Subsystems/Hrot.ReplayBrowser.Tests/` — subsystem tests (P2T3)
- `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Federation/` — P3T1, P3T2, P3T4, P3T6 tests
- `FDP/Toolkits/Fdp.Toolkits.Tests/Scenario/` — P3T3 tests

### Report Submission

When done, submit your report to:
`.dev/replay-browser-frankenstein/reports/BATCH-02-REPORT.md`

---

## Context

**Dependency chain:**
- `FederatedReplayManager.LoadGroup` (BATCH-01) → `ReplayBrowserSubsystem._manager` (this batch)
- `NetworkIdGuid` + `FederatedGuidResolver` + `ScenarioSerializer.DeserializeWith` (this batch) → `TransientMasterBuilder` (BATCH-03)

**Related Tasks:**
- [RBF-P2T3](../TASK-DETAILS.md#rbf-p2t3--subsystem-wiring-replaybrowsersubsystem-owns-a-manager)
- [RBF-P3T1](../TASK-DETAILS.md#rbf-p3t1--networkidguid-helper)
- [RBF-P3T2](../TASK-DETAILS.md#rbf-p3t2--federatedguidresolver)
- [RBF-P3T3](../TASK-DETAILS.md#rbf-p3t3--scenarioserializerdeserializewithiguidresolver-overload)
- [RBF-P3T4](../TASK-DETAILS.md#rbf-p3t4--consensus-mask-helper)
- [RBF-P3T6](../TASK-DETAILS.md#rbf-p3t6--extract-primeappdomainandsandbox-to-shared-helper)

---

## Tasks

### Task 1: Subsystem wiring (RBF-P2T3)

**File:** `Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs` (UPDATE)
**Task Definition:** See [TASK-DETAILS.md RBF-P2T3](../TASK-DETAILS.md#rbf-p2t3--subsystem-wiring-replaybrowsersubsystem-owns-a-manager)
**Design ref:** DESIGN.md §5, §6.1

Replace the `private ReplayBrowserContext _context` field with:
```csharp
private FederatedReplayManager? _manager;
private EntityRepository? _activeRepo;
```

Key behaviour rules:
- `Initialize` no longer calls `new ReplayBrowserContext()`. The manager starts empty (`_manager = null` or an empty `FederatedReplayManager` placeholder). For now keep a single-context convenience: if the subsystem loads one file it should still work via the manager's `Contexts`.
- The `_session` (`RepositoryAdapter`) and all downstream gizmo systems are constructed over `_activeRepo`. Add a private helper `RebindActiveRepo(EntityRepository repo)` that disposes the existing `_session` and creates a new one.
- In single-node mode (default), `_activeRepo` is `_manager.Contexts[selectedNodeId].SandboxRepo`. After `LoadGroup` the selected node defaults to the lowest-NodeId context.
- Keep `_activeRepo` as a field. All `Update` paths pass `_activeRepo` to gizmo/selection systems.
- The merged-view binding (`TransientMasterBuilder`) is **not yet wired** in this batch (that comes in BATCH-04). For now, only single-node mode is active.
- When the manager fires `OnTimeChanged`, rebind `_activeRepo` to the current single-node context's `SandboxRepo`.

For loading a file: the existing `LoadFdpAsync` path in `ReplayBrowserSubsystem` (or the panel callback that drives it) currently calls `_context.LoadRecording(path)`. Reroute it to call `FederatedReplayManager.LoadGroup(new[] { path })` and update `_manager`.

**Note on test project for P2T3:** Look for `Hrot/Subsystems/Hrot.ReplayBrowser/` for the existing project; search for any existing test project for that subsystem. If none exists, write a minimal test project. Check if `Hrot.ReplayBrowser.csproj` is in the solution before deciding. The tests must be **headless** (no UI rendering) — all three success conditions can be verified via a headless subsystem instance (`config.Headless = true`).

**Tests Required:**
- `RBF_P2T3_Subsystem_InitialState_ManagerExistsEmpty` — after `Initialize(headless)`, no exceptions, manager is null or has 0 contexts.
- `RBF_P2T3_Subsystem_LoadOneFile_BindsSingleNode` — loading one `.fdp` via the internal load path sets `_activeRepo` to the context's `SandboxRepo`.
- `RBF_P2T3_Subsystem_ExistingSeekStillWorks` — calling `manager.SetBaseWallTicks(t)` causes `_activeRepo` to be the context's repo (which has been seeked).

---

### Task 2: `NetworkIdGuid` helper (RBF-P3T1)

**File:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/NetworkIdGuid.cs` (NEW FILE)
**Task Definition:** See [TASK-DETAILS.md RBF-P3T1](../TASK-DETAILS.md#rbf-p3t1--networkidguid-helper)
**Design ref:** DESIGN.md §7.1

```csharp
namespace Fdp.Toolkit.ReplayBrowser.Federation
{
    /// <summary>
    /// Encodes a <see cref="long"/> <c>NetworkIdentity.Value</c> as a deterministic
    /// <see cref="System.Guid"/> suitable for use as a JSON entity key in the merged-view DOM.
    /// Encoding: little-endian bytes 0..7 = long value; bytes 8..15 = zero.
    /// </summary>
    public static class NetworkIdGuid
    {
        public static Guid From(long value);
        public static long ToLong(Guid g);
    }
}
```

Encoding: use `Guid(int a, short b, short c, byte d0..d7)` constructor or `MemoryMarshal.Write` — pack the 8 bytes of the long into the first 8 bytes of the Guid (little-endian), zero the remaining 8 bytes.

**Tests Required** (add to `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Federation/`):
- `RBF_P3T1_NetworkIdGuid_RoundTrips` — `ToLong(From(v)) == v` for `{0, 1, -1, long.MinValue, long.MaxValue, 0xDEAD_BEEF_CAFE_BABE_L}`.
- `RBF_P3T1_NetworkIdGuid_IsParseable` — `Guid.TryParse(From(v).ToString(), out _)` is true for a sample value.

---

### Task 3: `FederatedGuidResolver` (RBF-P3T2)

**File:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/FederatedGuidResolver.cs` (NEW FILE)
**Task Definition:** See [TASK-DETAILS.md RBF-P3T2](../TASK-DETAILS.md#rbf-p3t2--federatedguidresolver)
**Design ref:** DESIGN.md §7.2, §7.6

```csharp
public sealed class FederatedGuidResolver : IGuidResolver
{
    private Dictionary<Entity, string>? _saveMap;
    private Dictionary<string, Entity>? _loadMap;

    public void SetSaveMap(Dictionary<Entity, string> map);
    public void SetLoadMap(Dictionary<string, Entity> map);

    /// <summary>Save phase. Returns "null" literal string if entity not in save map.</summary>
    public string Resolve(Entity entity);

    /// <summary>Load phase. Returns Entity.Null on miss — does NOT throw.</summary>
    public Entity Resolve(string guidStr);
}
```

Critical: `Resolve(string)` must return `Entity.Null` on miss, not throw. The engine's default `LoadResolver` throws; this is the whole point of the federated resolver (see DESIGN §7.6).

**Tests Required** (add to `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Federation/`):
- `RBF_P3T2_Resolver_SaveMapHit_ReturnsString`
- `RBF_P3T2_Resolver_SaveMapMiss_ReturnsNull` — verify returns `"null"` (literal), not null reference
- `RBF_P3T2_Resolver_LoadMapHit_ReturnsEntity`
- `RBF_P3T2_Resolver_LoadMapMiss_ReturnsEntityNull` — verify returns `Entity.Null` AND does NOT throw
- `RBF_P3T2_Resolver_HotSwap_SaveMap` — `SetSaveMap` replaces the active map for subsequent calls

---

### Task 4: `ScenarioSerializer.DeserializeWith` overload (RBF-P3T3)

**File:** `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs` (UPDATE)
**Task Definition:** See [TASK-DETAILS.md RBF-P3T3](../TASK-DETAILS.md#rbf-p3t3--scenarioserializerdeserializewithiguidresolver-overload)
**Design ref:** DESIGN.md §7.5

Add a new public method:

```csharp
/// <summary>
/// Deserializes entities from <paramref name="dom"/> into <paramref name="repo"/> using
/// a caller-supplied <paramref name="loadResolver"/>.
/// <para>
/// DIFFERENCES from <see cref="Deserialize(EntityRepository,JsonObject,bool,Guid?)"/>:
/// <list type="bullet">
/// <item>The <c>Header.SubsystemType</c> filter is bypassed unconditionally. The merged DOM
/// holds the union of components from all subsystem types; the filter would be a footgun.</item>
/// <item>Entities are NOT created by this method. All entity keys in <paramref name="dom"/>
/// must already be present in <paramref name="preAllocated"/>. An absent key is a programmer
/// error and throws <see cref="InvalidOperationException"/>.</item>
/// <item>All relational handle resolutions go through <paramref name="loadResolver"/>,
/// including handles resolved by the auto-serializer and by custom translators. This
/// means a missing handle returns <see cref="Entity.Null"/> rather than throwing
/// (provided the supplied resolver implements that contract).</item>
/// </list>
/// </para>
/// </summary>
public void DeserializeWith(
    EntityRepository repo,
    JsonObject dom,
    IGuidResolver loadResolver,
    Dictionary<string, Entity> preAllocated)
```

**Implementation guidance:**

Look at the existing `Deserialize(EntityRepository repo, JsonObject dom, ...)` implementation (around line 290-410 of `ScenarioSerializer.cs`). The new method reuses pass-2 (component injection) from that method with these differences:

1. **Skip SubsystemType check** — do not read or compare `Header.SubsystemType`. Proceed directly to the `Entities` node.
2. **Skip pass-1 (CreateEntity)** — instead, use `preAllocated` to look up each entity key: `if (!preAllocated.TryGetValue(kvp.Key, out var entity)) throw new InvalidOperationException(...)`.
3. **Use `loadResolver` everywhere** — pass it into:
   - Each `translator.Inject(repo, entity, scenarioData, loadResolver)` call
   - Each `AutoSerializer.TryInject(repo, entity, typeId, compKvp.Value, loadResolver)` call
4. **Do not throw on Entity.Null** from the resolver — the resolver's contract is to return `Entity.Null` on miss; the injection code already handles this since it will just write `Entity.Null` into whatever field resolves it.
5. The method must NOT construct a `new LoadResolver(...)` internally at any point.

**Key test: `RBF_P3T3_DeserializeWith_ResolverReachesAutoSerializer`**

This is the most critical test. It proves the resolver is forwarded into the auto-serializer path (not bypassed). Strategy:
- Register a test component (e.g. `struct TestEntityRef { public Entity Target; }`) in a fresh `EntityRepository`.
- Create a DOM with one entity node that has an encoded entity-handle field (the Guid string of a known entity).
- Supply a `FederatedGuidResolver` with a `_loadMap` that maps the Guid string to a known transient entity.
- Verify the injected `TestEntityRef.Target` equals the expected transient entity (not `Entity.Null`).

**Tests Required** (add to `FDP/Toolkits/Fdp.Toolkits.Tests/Scenario/`):
- `RBF_P3T3_DeserializeWith_IgnoresSubsystemFilter` — DOM with `Header.SubsystemType = "Foreign.Subsystem"` (different from the serializer's own type) still deserialises all entities and injects all components. This is the OPPOSITE of the existing `Deserialize` behaviour.
- `RBF_P3T3_DeserializeWith_InjectsComponentsViaCustomResolver` — pre-allocated entities receive a registered component.
- `RBF_P3T3_DeserializeWith_AcceptsEntityNullFromResolver` — a relational handle the resolver maps to `Entity.Null` does NOT throw; the resulting field is `Entity.Null`.
- `RBF_P3T3_DeserializeWith_ResolverReachesAutoSerializer` — auto-serialized entity-handle field resolves through the supplied `FederatedGuidResolver`, NOT the engine's default. Verify by supplying a custom resolver that records call counts; assert `callCount > 0`.
- `RBF_P3T3_DeserializeWith_DefaultDeserializeStillThrowsOnMissingGuid` — regression: the original `Deserialize` still throws on a missing relational handle, proving this change is non-invasive.

**Note on `RBF_P3T3_DeserializeWith_InlineArrayHandleResolves`:** The TASK-DETAILS lists this as a success condition. It requires a component with an `InlineArray` of `Entity` fields. Check if `FdpAutoSerializer` handles inline arrays. If the auto-serializer already handles them through the same `Resolve` path (it should, since it generates the injection lambda with the resolver), write this test. If inline array support is not in the current auto-serializer, document it as a DEBT-TRACKER item and skip it.

---

### Task 5: Consensus-mask helper (RBF-P3T4)

**File:** `FDP/Engine/Fdp.Core/BitMask512.cs` (check for `BitwiseAndNot`; add if missing)
**Task Definition:** See [TASK-DETAILS.md RBF-P3T4](../TASK-DETAILS.md#rbf-p3t4--consensus-mask-helper)
**Design ref:** DESIGN.md §7.3

Check whether `BitMask512` already has a `BitwiseAndNot(BitMask512)` method. If it does, write the tests. If it does not, add it:

```csharp
/// <summary>
/// Clears every bit that is set in <paramref name="other"/> (equivalent to AND NOT).
/// Used by <c>TransientMasterBuilder</c> to build per-node extraction masks.
/// </summary>
public void BitwiseAndNot(in BitMask512 other)
```

This is equivalent to: `this = this AND (NOT other)`. Exercise per-lane with bits 0, 63, 64, 511.

**Tests Required** (add to `FDP/Engine/Fdp.Core.Tests/`):
- `RBF_P3T4_ConsensusMask_AndNot_AllBitsCovered` — set bits 0, 63, 64, 511 in candidate; set bits 64 and 511 in alreadyClaimed; verify result has bits 0 and 63 set, bits 64 and 511 cleared.
- `RBF_P3T4_ConsensusMask_EmptyClaimed_ReturnsCandidate` — alreadyClaimed is empty; `extract == candidate`.

---

### Task 6: Extract `PrimeAppDomainAndSandbox` to shared helper (RBF-P3T6)

**File:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/ReplayBrowserContext.cs` (UPDATE)
**New file:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/RepositoryPriming.cs`
**Task Definition:** See [TASK-DETAILS.md RBF-P3T6](../TASK-DETAILS.md#rbf-p3t6--extract-primeappdomainandsandbox-to-shared-helper)
**Design ref:** DESIGN.md §7.4 step 1

`ReplayBrowserContext` currently has a private static `PrimeAppDomainAndSandbox(repo, bus)` method. Extract its body to:

```csharp
namespace Fdp.Toolkit.ReplayBrowser.Federation
{
    /// <summary>
    /// Shared utility for priming a fresh <see cref="EntityRepository"/> with all
    /// component types discovered in loaded assemblies.
    /// </summary>
    internal static class RepositoryPriming
    {
        /// <summary>
        /// Registers all <c>[ComponentId]</c>-annotated types discovered in loaded assemblies
        /// on <paramref name="repo"/> and connects <paramref name="bus"/> (if provided).
        /// Mirrors the priming performed by <see cref="ReplayBrowserContext"/> so that
        /// <c>TransientMasterBuilder</c> can prime a fresh repo without duplicating the
        /// reflection scan.
        /// </summary>
        internal static void RegisterDiscoveredComponents(EntityRepository repo, FdpEventBus? bus = null);
    }
}
```

`ReplayBrowserContext` should now call `RepositoryPriming.RegisterDiscoveredComponents(SandboxRepo, SandboxBus)` in its constructor instead of calling the private method.

**Tests Required:**
- Existing `ReplayBrowserContext` tests must still pass (no regression).
- `RBF_P3T6_Priming_RegistersComponentsOnFreshRepo` — calling `RepositoryPriming.RegisterDiscoveredComponents(repo)` on an empty `EntityRepository` results in at least one well-known `[ComponentId]` type being registered (check `ComponentTypeRegistry.GetAllTypeIds()` count > 0 after priming, or check a specific known ID).

---

## Testing Requirements

- Run full suites for all touched assemblies before writing the report:
  ```powershell
  dotnet test FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj
  dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj
  ```
- Also build the full solution to ensure the Hrot subsystem change compiles:
  ```powershell
  dotnet build IOS-IG-SimHost.sln
  ```
- Minimum 20 new tests across all tasks.
- The critical tests are `RBF_P3T3_*` — read the auto-serializer source before writing them.

---

## Quality Standards

**TEST QUALITY FOR P3T3 — CRITICAL:**
- `RBF_P3T3_DeserializeWith_IgnoresSubsystemFilter` must actually attempt loading a DOM with a foreign subsystem type and verify that components WERE injected. A test that only checks "no exception" is insufficient.
- `RBF_P3T3_DeserializeWith_ResolverReachesAutoSerializer` MUST use call-count tracking, not just "entity not Null". The point is to prove the forwarding path, not the result.
- `RBF_P3T3_DeserializeWith_AcceptsEntityNullFromResolver` must verify the field value IS `Entity.Null` after inject, not just "no exception".
- `RBF_P3T3_DeserializeWith_DefaultDeserializeStillThrowsOnMissingGuid` — this is a regression gate; do not skip it.

**TEST QUALITY FOR P2T3:**
- `RBF_P2T3_Subsystem_LoadOneFile_BindsSingleNode` must verify `_activeRepo` is the context's `SandboxRepo` (not null, not some other instance). Use reflection or expose `ActiveRepo` as `internal` for test assembly access.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

1. **Task 1 (RBF-P2T3):** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2 (RBF-P3T1):** Implement → Write tests → **ALL tests pass** ✅
3. **Task 3 (RBF-P3T2):** Implement → Write tests → **ALL tests pass** ✅
4. **Task 4 (RBF-P3T3):** Implement → Write tests → **ALL tests pass** ✅
5. **Task 5 (RBF-P3T4):** Implement → Write tests → **ALL tests pass** ✅
6. **Task 6 (RBF-P3T6):** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until current task tests pass. Fix build errors immediately; do not leave the project in a broken state. Do not ask for permission to run tests or fix build errors — do it all autonomously until everything is green, then write the report.

---

## Success Criteria

- [ ] `ReplayBrowserSubsystem` owns a `FederatedReplayManager` (single-node path functional)
- [ ] `NetworkIdGuid.From/ToLong` encode/decode losslessly
- [ ] `FederatedGuidResolver` returns `Entity.Null` on miss (does NOT throw)
- [ ] `ScenarioSerializer.DeserializeWith` bypasses subsystem filter and uses caller-supplied resolver
- [ ] `BitwiseAndNot` exists on `BitMask512` (added or already present)
- [ ] `RepositoryPriming.RegisterDiscoveredComponents` extracted and used by `ReplayBrowserContext`
- [ ] All new tests pass; all existing tests still pass
- [ ] `dotnet build IOS-IG-SimHost.sln` succeeds with zero errors
- [ ] Report submitted to `.dev/replay-browser-frankenstein/reports/BATCH-02-REPORT.md`

---

## Developer Insights

**Q1:** What problems did you encounter during implementation? How did you resolve them?

**Q2:** What design decisions did you make beyond the instructions? What were the alternatives?

**Q3:** What edge cases did you discover in `DeserializeWith` that weren't covered in the spec?

**Q4:** Did you find any issues in how `FdpAutoSerializer` handles inline-array `Entity` fields? Document what you found (or confirmed).

**Q5:** Are there any concerns about how `FederatedGuidResolver` and `DeserializeWith` will interact with `TransientMasterBuilder` in BATCH-03?

**Suggested commit message:** What did you achieve in this batch?

---

## Reference Materials

- **Task Defs:** `.dev/replay-browser-frankenstein/TASK-DETAILS.md` — RBF-P2T3, RBF-P3T1–P3T4, P3T6
- **Design:** `.dev/replay-browser-frankenstein/DESIGN.md` — §5, §6.1, §7.1–§7.5
- **ReplayBrowserSubsystem:** `Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs`
- **ScenarioSerializer:** `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs`
- **FdpAutoSerializer:** `FDP/Toolkits/Fdp.Toolkits/Scenario/FdpAutoSerializer.cs`
- **IGuidResolver:** `FDP/Toolkits/Fdp.Toolkits/Scenario/IGuidResolver.cs`
- **BitMask512:** `FDP/Engine/Fdp.Core/BitMask512.cs`
- **ReplayBrowserContext:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/ReplayBrowserContext.cs`
- **FederatedReplayManager (BATCH-01):** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/FederatedReplayManager.cs`
- **Developer skill guide:** `.github/skills/developer/SKILL.md`
