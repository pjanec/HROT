# BF-BATCH-06 Report — ChannelCommand Param Enrichment (DEBT-BCP-006)

**Branch:** `blueprint-integ-1`
**Date:** 2026-06-06

---

## 1. Per-Action FQN Mapping

| Action | Real ParamsTypeFqn | Source File |
|---|---|---|
| `MoveTo` | `Fdp.Toolkit.Navigation.MoveToParams` | `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationActions.cs` (line 33) |
| `FollowRoute` | `Fdp.Toolkit.Navigation.FollowRouteParams` | `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationActions.cs` (line 127) |
| `AimAndFire` | `Fdp.Toolkit.Combat.Executors.AimAndFireParams` | `FDP/Toolkits/Fdp.Toolkits/Combat/Executors/AimAndFireParams.cs` (line 11) |
| `OpenDoor` | `Fdp.Toolkit.Behavior.Executors.OpenDoorParams` | `FDP/Toolkits/Fdp.Toolkits/Behavior/Executors/OpenDoorExecutor.cs` (line 11) |
| `EjectPassengers` | `System.Int32` (**unchanged**) | No executor param struct exists — `EjectPassengersExecutor.Execute()` reads `PassengerBuffer` directly from the entity via `EntityRepository.GetComponentRW`. No struct to project. |

### Public instance fields confirmed

All four structs use `[StructLayout(LayoutKind.Sequential)]` and expose public instance fields only (no properties). `ReflectDataMembers` in `NodePinSchema` iterates `GetFields(Public | Instance)` which will yield exactly these fields:

- `MoveToParams`: Destination (Vector3), ArrivalRadius (float), Speed (float), RouteHandle (int), LayerMask (uint), ReverseAllowed (byte), Flags (byte), MaxReplans (byte), BackendForce (byte) — **9 fields**
- `FollowRouteParams`: TrajectoryId (int), IsLooped (byte) — **2 fields**
- `AimAndFireParams`: Target (Entity), CooldownSeconds (float) — **2 fields**
- `OpenDoorParams`: TargetDoor (Entity) — **1 field**

---

## 2. Catalog Change

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Catalogs/BuiltInChannelCommandCatalog.cs`

Replaced placeholder `"System.Int32"` with real FQNs for 4 of 5 entries. `EjectPassengers` retains `"System.Int32"` (no struct exists) which triggers the graceful single-value-pin fallback path in `NodePinSchema.ChannelCommandPins`.

---

## 3. Tests Added

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/CatalogTests.cs`

Five new `[Fact]` tests added to the existing `CatalogTests` class:

| Test | Asserts |
|---|---|
| `ChannelCommandPins_MoveTo_ProjectsOneDataPinPerMoveToParamsField` | 9 data-IN pins with exact field names; no `Int32` placeholder pin |
| `ChannelCommandPins_AimAndFire_ProjectsTargetAndCooldownPins` | 2 data-IN pins: Target, CooldownSeconds |
| `ChannelCommandPins_FollowRoute_ProjectsTrajectoryIdAndIsLoopedPins` | 2 data-IN pins: TrajectoryId, IsLooped |
| `ChannelCommandPins_OpenDoor_ProjectsTargetDoorPin` | 1 data-IN pin: TargetDoor |
| `ChannelCommandPins_EjectPassengers_FallsBackToSingleValuePin` | Exec In+Out + exactly 1 data-IN value pin (Int32 primitive fallback) |

All 5 pass: `Passed: 5, Failed: 0` (12 ms).

The tests use `NodePinSchema.GetCanonicalPins(node, channelCommands: BuiltInChannelCommandCatalog.Instance)` which is accessible via the existing `[assembly: InternalsVisibleTo("Hrot.Blueprints.Tests")]` declaration in `Hrot.Blueprints.Editor/AssemblyInfo.cs`.

---

## 4. netstandard2.0 Generator / No Hard Fdp.Toolkits Dependency

- `BuiltInChannelCommandCatalog` contains only `string` constants — the FQN strings are stored as data, not CLR type references. No `using` for Fdp.Toolkits was added.
- `Hrot.Blueprints.Compiler.csproj` already gates Fdp.Toolkits to `net8.0` only (`Condition="'$(TargetFramework)' == 'net8.0'"`). The `netstandard2.0` slice has zero Fdp.Toolkits types.
- In the generator host (netstandard2.0), `NodePinSchema.ResolveType("Fdp.Toolkit.Navigation.MoveToParams")` will return `null` → `ChannelCommandPins` falls back to a single typed pin named `"MoveToParams"` (the graceful `paramsType == null` branch at line 542–544). No crash; BCF-D03 behavior is preserved.
- Full Rebuild of `Hrot.AI.Behaviors` (which hosts `Count2.bp.json`) via `-t:Rebuild`: **Build succeeded. 0 Warning(s). 0 Error(s).** No BP0002 regression.

---

## 5. Gate Results

### `dotnet build IOS-IG-SimHost.sln -c Debug`
```
Build succeeded.
0 Error(s)
18 Warning(s) — all pre-existing, none from BF-06 changes
Time Elapsed 00:00:25.65
```

### Full Rebuild (netstandard2.0 generator path, Count2.bp.json)
```
dotnet build Hrot.AI.Behaviors.csproj -t:Rebuild
Build succeeded. 0 Warning(s). 0 Error(s).
```
No BP0002 emitted.

### `dotnet test Hrot.Blueprints.Tests.csproj -c Debug`
```
Failed:   7, Passed: 1379, Skipped: 8, Total: 1394
```

**Final 7 failures (all pre-existing, 0 new):**
1. `Hrot.Blueprints.Tests.Editor.ConditionSummaryAttachmentTests.Synthesize_EqsResult_ScoreCrossed_IncludesThreshold`
2. `Hrot.Blueprints.Tests.Compiler.LibraryEmitGoldenTests.Library_EmitMatchesGoldenSource`
3. `Hrot.Blueprints.Tests.Compiler.AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource(assetName: "MoveToAndFire")`
4. `Hrot.Blueprints.Tests.Compiler.AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource(assetName: "HasVisibleTarget")`
5. `Hrot.Blueprints.Tests.Runtime.AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes`
6. `Hrot.Blueprints.Tests.Demos.LibraryMathDemoTests.LibraryMath_GeneratedSource_Snapshot`
7. `Hrot.Blueprints.Tests.Demos.MoveToAndFireDemoTests.MoveToAndFire_GeneratedSource_Snapshot`

Baseline before batch: same 7. **0 new failures.**

### `dotnet test Hrot.ClusterRunner.Integration.Tests.csproj --filter "FullyQualifiedName~EditorSubsystemBoot"`
```
Passed: 10, Failed: 0 — 10/10
```

---

## 6. Deviations

- **EjectPassengers** retains `"System.Int32"` placeholder (documented). The executor has no dedicated param struct; it operates purely via `PassengerBuffer` on the entity. This is the correct behavior: one exec-time value pin is the graceful fallback.
- No golden snapshots were regenerated.
- No user WIP files (RecipeCreateModal, AssetBrowserWindow, EditorSubsystem) were touched.
- No commit made (lead commits).
