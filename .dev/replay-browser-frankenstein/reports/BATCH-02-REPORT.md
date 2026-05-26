# BATCH-02 Report — Replay Browser Frankenstein

**Batch**: BATCH-02
**Feature**: Replay Browser Frankenstein (RBF)
**Status**: COMPLETED

---

## Summary

| Task     | Title                                     | Status    | Tests |
|----------|-------------------------------------------|-----------|-------|
| RBF-P2T3 | Subsystem wiring (FederatedReplayManager) | COMPLETED | 3     |
| RBF-P3T1 | NetworkIdGuid                             | COMPLETED | 2     |
| RBF-P3T2 | FederatedGuidResolver                     | COMPLETED | 5     |
| RBF-P3T3 | ScenarioSerializer.DeserializeWith        | COMPLETED | 5     |
| RBF-P3T4 | BitMask512.BitwiseAndNot                  | COMPLETED | 2     |
| RBF-P3T6 | Extract RepositoryPriming                 | COMPLETED | 1     |

**Total new tests**: 18

---

## Task Details

### RBF-P2T3 — Subsystem wiring (FederatedReplayManager)

**Production files modified**:
- `Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs`
  - Added `using Fdp.Toolkit.ReplayBrowser.Federation;`
  - Added fields: `private FederatedReplayManager? _manager;`, `private EntityRepository? _activeRepo;`
  - Added internal test accessors: `Manager`, `ActiveRepo`
  - `Initialize` clears both fields to null
  - `OnManagerTimeChanged()` looks up `LocalEntitiesProviderNodeId` in `Contexts` and calls `RebindActiveRepo`
  - `RebindActiveRepo(EntityRepository repo)` sets `_activeRepo = repo`
  - `LoadFdpViaManager(string path)` disposes old manager, calls `FederatedReplayManager.LoadGroup`, subscribes `OnTimeChanged`, calls `OnManagerTimeChanged()`
  - `Shutdown` disposes `_manager` before `_context`

**Test file modified**:
- `Hrot/Subsystems/Hrot.ReplayBrowser.Tests/ReplayBrowserSubsystemTests.cs`
  - Added usings: `System.IO`, `Fdp.Core.FlightRecorder`, `Fdp.Core.FlightRecorder.Metadata`, `Fdp.Toolkit.ReplayBrowser.Federation`
  - 3 new tests:
    - `RBF_P2T3_Subsystem_InitialState_ManagerIsNull`
    - `RBF_P2T3_Subsystem_LoadOneFile_BindsActiveRepo`
    - `RBF_P2T3_Subsystem_SeekAfterLoad_ActiveRepoRemainsCorrect`
  - Helper: `CreateMinimalFdpFile(string directory, Guid exerciseId, int nodeId)` — writes a valid `.fdp` + `.meta.json` pair using `AsyncRecorder`

---

### RBF-P3T1 — NetworkIdGuid

**Production files created**:
- `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/NetworkIdGuid.cs`
  - `internal static class NetworkIdGuid`
  - `static Guid From(long value)` — packs long into first 8 bytes of a Guid via `MemoryMarshal`
  - `static long ToLong(Guid g)` — reads the packed long back

**Test file created**:
- `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Federation/NetworkIdGuidTests.cs`
  - `RBF_P3T1_NetworkIdGuid_RoundTrips` — [Theory] with several long values including 0, MinValue, MaxValue, negative
  - `RBF_P3T1_NetworkIdGuid_ProducesValidGuidString` — verifies `From(42).ToString()` is a non-empty valid GUID string

**Bug fixed during verification**:
- `MemoryMarshal.Write(bytes, ref value)` → `MemoryMarshal.Write(bytes, in value)` (CS9191 in .NET 8)

---

### RBF-P3T2 — FederatedGuidResolver

**Production file created**:
- `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/FederatedGuidResolver.cs`
  - `public sealed class FederatedGuidResolver : IGuidResolver`
  - `SetSaveMap(Dictionary<Entity, string>)` — hot-swappable; replaces the current save map
  - `SetLoadMap(Dictionary<string, Entity>)` — hot-swappable; replaces the current load map
  - `Resolve(Entity)` — returns mapped string or `"null"` on miss (never throws)
  - `Resolve(string)` — returns mapped entity or `Entity.Null` on miss (never throws)

**Test file created**:
- `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Federation/FederatedGuidResolverTests.cs`
  - `RBF_P3T2_SaveMap_Hit` — Resolve(Entity) returns correct GUID string
  - `RBF_P3T2_SaveMap_Miss` — Resolve(Entity) returns "null" for unmapped entity
  - `RBF_P3T2_LoadMap_Hit` — Resolve(string) returns correct Entity
  - `RBF_P3T2_LoadMap_Miss` — Resolve(string) returns Entity.Null for unmapped string
  - `RBF_P3T2_HotSwap_SaveMap` — swapping save map replaces previous mapping

---

### RBF-P3T3 — ScenarioSerializer.DeserializeWith

**Production file modified**:
- `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs`
  - Added `public void DeserializeWith(EntityRepository repo, JsonObject dom, IGuidResolver loadResolver, Dictionary<string, Entity> preAllocated)`
  - No pass-1 (entities from `preAllocated` only; unknown DOM keys are silently skipped)
  - Runs custom translators with caller-supplied `loadResolver`
  - Runs `AutoSerializer.TryInject` for remaining component types, forwarding `loadResolver`

**Design correction applied**: `DeserializeWith` skips (rather than throws on) entity keys in the DOM that are not in `preAllocated`. This supports the federated replay use case where the caller pre-allocates only entities belonging to their node.

**Test file created**:
- `FDP/Toolkits/Fdp.Toolkits.Tests/Scenario/ScenarioSerializerDeserializeWithTests.cs`
  - `RBF_P3T3_DeserializeWith_IgnoresSubsystemFilter` — DOM from "Hrot.SimHost" deserialized by "Hrot.CGF" serializer; standard `Deserialize` returns 0 entities; `DeserializeWith` injects `DummyPosition`
  - `RBF_P3T3_DeserializeWith_InjectsComponentsViaCustomResolver` — basic DummyPosition round-trip
  - `RBF_P3T3_DeserializeWith_AcceptsEntityNullFromResolver` — GuidedTarget with missing cross-ref; resolver returns `Entity.Null`; no throw; `TargetId == Entity.Null`
  - `RBF_P3T3_DeserializeWith_ResolverReachesAutoSerializer` — `CountingResolver` verifies resolver called >= 1 for a GuidedTarget-bearing entity
  - `RBF_P3T3_DeserializeWith_DefaultDeserializeStillThrowsOnMissingGuid` — tampered foreign GUID; regular `Deserialize` must throw `InvalidOperationException`

---

### RBF-P3T4 — BitMask512.BitwiseAndNot

**Production file modified**:
- `FDP/Engine/Fdp.Core/BitMask512.cs`
  - Added `public void BitwiseAndNot(in BitMask512 other)` after `BitwiseOr`
  - Clears bits in `this` that are set in `other` using `_q0 &= ~other._q0; ... _q7 &= ~other._q7;`
  - Decorated with `[MethodImpl(AggressiveInlining)]`

**Test file created**:
- `FDP/Engine/Fdp.Core.Tests/BitMask512AndNotTests.cs`
  - `RBF_P3T4_ConsensusMask_AndNot_AllBitsCovered` — clears claimed bits from candidate mask; all expected bits cleared
  - `RBF_P3T4_ConsensusMask_EmptyClaimed_ReturnsCandidate` — BitwiseAndNot with empty mask preserves all bits

---

### RBF-P3T6 — Extract RepositoryPriming

**Production files created/modified**:
- `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/RepositoryPriming.cs` (NEW)
  - `internal static class RepositoryPriming`
  - `internal static void RegisterDiscoveredComponents(EntityRepository repo, FdpEventBus? bus = null)`
  - Reflects all loaded assemblies, skips `System.*` and `Microsoft.*` namespaces
  - Finds all types with `[ComponentId]` attribute
  - Calls `repo.RegisterComponent<T>()` for each via reflection (`RegisterComponent<T>` generic method invocation)
- `Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserContext.cs` (MODIFIED)
  - Removed `PrimeAppDomainAndSandbox` (inlined logic moved to `RepositoryPriming`)
  - Constructor now calls `Federation.RepositoryPriming.RegisterDiscoveredComponents(SandboxRepo, SandboxBus)`

**Test file created**:
- `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Federation/RepositoryPrimingTests.cs`
  - `RBF_P3T6_RegisterDiscoveredComponents_RegistersHarnessPosition` — clears `ComponentTypeRegistry`, calls `RegisterDiscoveredComponents`, verifies `HarnessPosition` (ComponentId 202) is discoverable by attempting `SetComponent` + `HasComponent` + `GetComponent`

---

## Build & Test Results

```
dotnet test FDP/Engine/Fdp.Core.Tests  --filter "RBF_P3T4"    => Passed: 2
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests  --filter "RBF_P3" => Passed: 23
dotnet test Hrot.ReplayBrowser.Tests  --filter "RBF_P2T3"       => Passed: 3
dotnet build IOS-IG-SimHost.sln                                 => 0 Errors, 5 pre-existing Warnings
```

---

## Success Criteria — Verification

| Criterion | Met? |
|-----------|------|
| `BitMask512.BitwiseAndNot` exists and passes tests | YES |
| `NetworkIdGuid.From` / `ToLong` round-trips | YES |
| `FederatedGuidResolver` resolves both directions, hot-swappable | YES |
| `ScenarioSerializer.DeserializeWith` ignores subsystem filter, uses caller resolver | YES |
| `RepositoryPriming` extracted from `ReplayBrowserContext` | YES |
| `ReplayBrowserSubsystem` wired to `FederatedReplayManager` | YES |
| Solution builds with 0 errors | YES |
| All new tests pass | YES (18/18) |
