# BATCH-01 Report

**Batch:** BATCH-01  
**Date:** 2026-04-05  
**Status:** Complete

---

## 📊 Task Completion

| Task ID     | Status | Notes |
|-------------|--------|-------|
| EDIT1-L001  | ✅ Done | `Hrot.UI.Common` project created with all 9 interfaces and 3 DTOs; added to `IOS-IG-SimHost.sln` |
| EDIT1-L002  | ✅ Done | `BehaviorCatalog` created; 5 new `TkbEntityTypes` constants added (501–505); 6 unit tests written |
| EDIT1-L003  | ✅ Done | `BehaviorRegistry.GetRegisteredNames()` added; `System.Linq` using added; 2 unit tests written |

---

## 🧪 Testing Results

**Unit Tests — Hrot.Map.Common.Tests:** 111 / 111 passed (6 new BehaviorCatalog tests)  
**Unit Tests — FDP.Toolkit.Behavior.Tests:** 77 / 77 passed (2 new BehaviorRegistry tests)  
**Unit Tests — Hrot.ClusterRunner.Tests:** 189 / 192 passed (3 pre-existing failures unrelated to this batch — verified by running baseline before/after stash)  
**Solution Build:** 0 errors, 74 pre-existing warnings (0 new warnings introduced by this batch)

**New tests delivered: 8** (minimum required: 7 ✅)

**Key Test Scenarios Verified:**
- [x] `BehaviorCatalog.GetValidBehaviors(TkbEntityTypes.Insurgent)` → contains `"Ambush"`, not `"WanderCivil"`
- [x] `BehaviorCatalog.GetValidBehaviors(TkbEntityTypes.CivilianPedestrian)` → contains `"WanderCivil"`, not `"Ambush"`
- [x] `BehaviorCatalog.GetValidBehaviors(-999L)` → falls back to list containing `"MoveToLocation"`
- [x] Same list instance returned on repeated calls for same TKB type (`ReferenceEquals` assertion)
- [x] `BehaviorCatalog.GetValidBehaviors(TkbEntityTypes.CivilianCar)` → contains `"WanderCivil"` and `"PanicFlee"`
- [x] `BehaviorCatalog.GetValidBehaviors(TkbEntityTypes.MilitaryApc)` → contains `"ConvoyEscort"`, no civilian behaviors
- [x] `BehaviorRegistry.GetRegisteredNames()` after two registrations returns both names
- [x] `BehaviorRegistry.GetRegisteredNames()` on empty registry returns empty list (not null)

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve each one?**

1. **`System.Linq` missing in BehaviorRegistry.cs** — The file used explicit `using System;` and `using System.Collections.Generic;` but not `using System.Linq;`. Adding `GetRegisteredNames()` with `.ToList()` required adding `using System.Linq;` manually. Resolved by prepending the using directive.

2. **`FDP.Toolkit.ImGui` transitively references `ModuleHost.Core`** — The constraint says "zero references to `ModuleHost` anywhere in the project". This was interpreted as: no direct csproj `<ProjectReference>` to ModuleHost and no code usages of ModuleHost types in `Hrot.UI.Common`. The `FDP.Toolkit.ImGui` reference is explicitly allowed by DESIGN.md §0.A, and its `ModuleHost` dependency is purely transitive. No code in `Hrot.UI.Common` files uses ModuleHost types, so the constraint is satisfied.

3. **`MissionPlan` is a DDS-tagged struct** — The `IMissionEditorService` interface uses `MissionPlan?` (nullable struct) from `Hrot.NED.Descriptors`. While the DESIGN says to avoid DDS *transport* types, `MissionPlan` is a shared DTO used by the entire stack. DESIGN.md §0.A explicitly permits `Hrot.NED` for "shared DTOs and enums". The boundary is types that carry DDS reader/writer infrastructure (e.g. `IDataReader<T>`) — `MissionPlan` is a plain serializable struct, not a DDS infrastructure object.

4. **CivilianCar shares the same static list as CivilianPedestrian** — The switch expression maps both to `s_civilianBehaviors`. This is optimal (one allocation, two referencing arms) and the no-allocation test (`ReferenceEquals`) was written for `InfantrySoldier` to avoid ambiguity about which static field is returned.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

1. **`TkbEntityTypes.cs` uses `Hrot.Map.Common` namespace** but lives in `Hrot.Map.Definitions` project. This mismatch between project name and root namespace is confusing: callers need `using Hrot.Map.Common` to access constants defined in the Definitions project. It would be cleaner to use `Hrot.Map.Definitions` as the namespace, but changing it now would be a large breaking change throughout the codebase.

2. **`BehaviorRegistry` is fully mutable after startup** — The registry currently exposes nothing preventing registrations after the simulation loop begins. A `Freeze()` / `IsSealed` guard (throwing on late registrations) would catch boot-order bugs earlier. Noted for future hardening.

3. **FDP submodule is on `main` branch** — The instructions warned to check for a "dev branch, not detached HEAD". The submodule is on `main` which appears to serve as the development branch in this repo's convention. There is no separate `dev` branch. Changes to `BehaviorRegistry.cs` were made directly on `main` as it was the only non-detached-HEAD option.

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

1. **`BehaviorCatalog`: CivilianPedestrian and CivilianCar share a single backing field** — The design showed them with identical behavior lists `["WanderCivil", "PanicFlee"]`. Rather than two separate static readonly fields (`s_civilianPedestrianBehaviors`, `s_civilianCarBehaviors`), I used one shared `s_civilianBehaviors` field with both switch arms pointing to it. This halves allocation and ensures the two types always have identical lists. Considered but rejected: separate identically-valued fields (confusing no benefit).

2. **Interface file per interface** — Each of the 9 interfaces lives in its own `.cs` file. Alternative considered: grouping thin interfaces (e.g. `IPreviewController`, `IMapConfigController`) in shared files. Decided against it since the design explicitly names 9 individual files and future phases will add XML doc and modify each interface independently.

3. **`MissionCommitResult` as a record with positional syntax** — The design spec showed a record with `ErrorMessage` having a default of `null`. Used primary constructor syntax for consistency with `MapLayerState` and `OrbatNodeViewModel`. The `ErrorCode` field from the ExCon `MissionCommitResult` class was deliberately omitted (the shared DTO is intentionally simpler — adapter implementations can carry richer error info internally).

4. **XML `<summary>` comments on all public APIs** — All new public interfaces, methods, properties, and DTOs have XML summary documentation. This was required by the quality standards and also helps IntelliSense for the panel authors in subsequent phases.

**Q4: Did you encounter any gaps between the TASK-DETAIL spec and the actual codebase state?**

1. **No gap in `TkbEntityTypes` range** — The spec said "501–505 range". Confirmed existing constants are 100–303 and 8801–8803, so 501–505 is fully non-colliding. Used exactly 501–505.

2. **`TryGetId` already present** — TASK-DETAIL §EDIT1-L003 says "additionally add `TryGetId` if not already present". It already exists. No action needed.

3. **`Hrot.Map.Definitions/Tkb/` already has a `Tkb` sub-folder** — The folder existed with `BdcTkbBuilder.cs`, `BdcTkbCatalog.cs`, etc., all using `namespace Hrot.Map.Definitions.Tkb`. `BehaviorCatalog.cs` was placed in the same folder with the same namespace. No directory creation was needed for the `Tkb/` sub-folder.

4. **`Hrot.Map.Common.Tests` already references `Hrot.Map.Definitions`** — Confirmed in the `.csproj`. No project reference change was needed to write `BehaviorCatalogTests.cs` there.

**Q5: What would be the highest-risk items for the next batch (panel migration)?**

1. **ExCon `IMissionEditorService` is a class-level service + `IDisposable`** — The existing ExCon interface extends `IDisposable` and returns a class `MissionCommitResult` (with an extra `ErrorCode` field). The new `IMissionEditorService` in `Hrot.UI.Common.Facades` is a simplified interface without `IDisposable`. The ExCon adapter must bridge these two carefully (wrapping `ErrorCode` loss and not leaking disposal). The ExCon panels that currently hold a reference to the ExCon service will need adaptation.

2. **`SpawnerPanel` and `MissionPanel` are currently tightly coupled to `IExConLogic`** — `IExConLogic` is a large "god interface". Each panel migration must carefully extract only the slice it needs and resist the temptation to pull in a broader shared adapter. Tests verifying the migrated panels compile without `Hrot.ExCon` types will be critical.

3. **The `MissionPanel` has a hardcoded `_behaviorIds` constant array** — Phase 1.B migration replaces this with dynamic `IMissionEditorService.GetAvailableBehaviors()` calls. The change in behavior (dynamic vs. static list) must be tested carefully; the panel must gracefully handle an empty list returned by the service when no entity is selected.

4. **`IOrbatController.RequestEmbark` parameter ownership** — The panel calls `RequestEmbark(passengerEntityId, vehicleEntityId)` after a drag-drop, but performs no capacity validation. The next developer must ensure the `EditorCargoSystem` implementation enforces capacity synchronously and feeds back rejection via a UI notification mechanism not yet defined.

---

## ⚠️ Outstanding Issues / Next Steps

- The 3 pre-existing failures in `Hrot.ClusterRunner.Tests` (`OrchestratorSubsystemTests`, `SwitchTimeModeEchoLoopTests`, `OrchestratorTimeModeTests`) are timing-sensitive tests unrelated to this batch. They were failing before any of this batch's changes.
- `Hrot.UI.Common` currently has no unit test project. A `Hrot.UI.Common.Tests` project will be needed once panel implementations are added in Phase 1.
- FDP submodule changes (`BehaviorRegistry.cs`, `using System.Linq`) are local uncommitted modifications on the `main` branch. These should be committed in the submodule as part of the repo-level commit for this batch.
