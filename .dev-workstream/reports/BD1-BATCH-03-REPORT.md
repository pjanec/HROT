# BD1-BATCH-03 Report

**Batch:** BD1-BATCH-03  
**Developer:** GitHub Copilot  
**Date:** 2026-03-19  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| BD1-P3T1 | ✅ Complete | `BdcTkbBuilder.WithPhysics` now appends `PhysicsCollider` using `Math.Max(Length, Width) / 2f` and `PhysicsConstants.EntityCollisionLayer` |
| BD1-P3T2 | ✅ Complete | `SimHostScenarioManager.SpawnEntityLocal` now adds `PhysicsCollider` immediately after `NavState`; same radius formula as P3T1 |
| BD1-P4T1 | ✅ Complete | `SimHostVisualization.Initialize` sets `_map.Camera.Offset = new Vector2(1280 / 2f, 720 / 2f)` on the same line as map creation |
| BD1-P5T1 | ✅ Complete | `DisTypeStruct` DDS wire type introduced in `GenericDescriptors.cs`; all 10+ broken call sites updated; egress and ingress translators updated; round-trip verified |
| BD1-P6T1 | ✅ Complete | `ComponentReflector.DrawComponents` now performs byte-cache change detection using `Marshal.StructureToPtr`; changed fields highlighted yellow; cache cleared on entity switch |
| BD1-P7T1 | ✅ Complete | `CreateEntityRequestSystem` extracts `ProcessIncomingRequest` method; `_processRequestDelegate` field cached once in constructor; no allocation per `Execute` call |

---

## 🧪 Testing Results

**Bagira.Map.Common.Tests:** 64 / 64 passed  
**Bagira.SimHost.Tests:** 235 / 235 passed  
**Bagira.IG.Tests:** 311 / 311 passed  
**Bagira.DDS.DataModel.Tests:** 23 / 23 passed  
**FDP.Toolkit.ImGui.Tests:** 42 / 42 passed  
**Bagira.SimHost.Integration.Tests:** 27 / 28 — 1 pre-existing failure (`EntityMission_MovesEntity`, confirmed broken at HEAD before this batch)  
**Bagira.Runner.Integration.Tests:** 31 / 31 passed  
**Bagira.Runner.Tests:** 99 / 99 passed

**New tests added in this batch: 16**

**Key Test Scenarios Verified:**

- [x] `BdcTkbBuilderPhysicsTests.WithPhysics_AddsPhysicsCollider` — collider present after `WithPhysics`
- [x] `BdcTkbBuilderPhysicsTests.WithPhysics_ColliderRadius_IsHalfOfLargerDimension` — radius = Max(L,W)/2
- [x] `BdcTkbBuilderPhysicsTests.WithPhysics_ColliderLayer_IsEntityCollisionLayer` — layer == 1
- [x] `BdcTkbBuilderPhysicsTests.WithPhysics_NonSquarePreset_UsesLargerDimension` — assymmetric vehicle uses length
- [x] `SimHostScenarioManagerTests.SpawnEntityLocal_AddsPhysicsCollider` — collider on spawned entity
- [x] `SimHostScenarioManagerTests.SpawnEntityLocal_PhysicsCollider_HasCorrectLayerAndRadius` — layer and radius match
- [x] `SimHostVisualizationTests.Initialize_SetsMapCameraOffset` — offset = (640, 360)
- [x] `EntityMasterEgressTranslatorTests.DisType_EgressFieldsMappedCorrectly` — all 8 DIS fields round-trip egress
- [x] `EntityMasterTranslatorTests.DisTypeStruct_IngressRoundTrip_ReconstructsCorrectUlongValue` — bit-shift reconstruction correct for `Kind=1, Extra=1` sentinel value
- [x] `ComponentReflectorTests.DrawComponents_DoesNotThrow_ForEmptyWorld` — smoke: no crash on empty query
- [x] `ComponentReflectorTests.DrawComponents_CachesBytes_AfterFirstDraw` — dictionary populated after first frame
- [x] `ComponentReflectorTests.DrawComponents_DetectsChange_AfterComponentMutation` — byte diff detected after mutation
- [x] `ComponentReflectorTests.DrawComponents_ClearsCache_OnEntitySwitch` — cache wiped when inspected entity changes
- [x] `ComponentReflectorTests.DrawComponents_DoesNotThrow_WhenBothFramesSameValue` — stable state: no change triggered
- [x] `CreateEntityRequestSystemTests.ProcessRequests_UsesPreCachedDelegate` — single delegate instance reused across calls
- [x] `CreateEntityRequestSystemTests.ProcessRequests_DelegateCache_BehaviourRegression` — caching doesn't change behavior

---

## 📝 Developer Insights

**Q1: What issues did you encounter regarding the DDS structural changes (BD1-P5T1)? How did you resolve them?**

Changing `EntityMaster.DisType` from `ulong` to `DisTypeStruct` broke 10+ files because numeric literals (`DisType = 0`, `DisType = 0x0100_0000_0000_0001UL`) are no longer assignable to a struct type.

Resolution strategy:
- `DisType = 0` / `DisType = 0UL` → replaced with `DisType = default` (zero-initialises all struct fields).
- Explicit ulong values such as `0x0100_0000_0000_0001UL` (used in test constants) required decomposing into individual fields. The `DISEntityType` struct uses `[StructLayout(LayoutKind.Explicit)]` with byte offsets 7 (Kind), 6 (Domain), 4–5 (Country ushort, little-endian), 3 (Category), 2 (Subcategory), 1 (Specific), 0 (Extra). So `0x0100_0000_0000_0001UL` = `Kind=1, Extra=1` as a `DisTypeStruct`.
- The ingress translator and `DescriptorMapper` reconstruct the `DISEntityType.Value` ulong from the struct using: `(Kind<<56)|(Domain<<48)|(Country<<32)|(Category<<24)|(Subcategory<<16)|(Specific<<8)|Extra`.
- Tests for both egress (ECS→DDS) and ingress (DDS→ECS) round-trip were added to validate the byte layout.

**Q2: What efficiency considerations apply to the byte-cache used in ComponentReflector (BD1-P6T1)?**

The cache stores one `byte[]` snapshot per component type, keyed by `Type`. The comparison reads the live value by marshalling to a temporary native buffer (`Marshal.AllocHGlobal` / `StructureToPtr` / `Copy` / `FreeHGlobal` in a `finally` block) to avoid a pinned GC allocation per component per frame. The allocated buffer is ephemeral — only the snapshot `byte[]` persists in `_unmanagedCache`.

Managed types (reference types, types containing reference fields) are entirely skipped because `Marshal.SizeOf` throws on them. The first time a component is seen its bytes are silently captured as the new baseline with no style change applied, preventing a false yellow flash on the first frame.

The cache is cleared entirely when the inspected entity changes; this keeps the cache warm only for the single entity currently being displayed, keeping memory proportional to component count of one entity.

**Q3: What design decisions did you make for the delegate-caching change (BD1-P7T1)?**

The lambda `request => { ... }` in `Execute` was a genuine allocation source because C# captures `this` in a new delegate object each call. The extracted `ProcessIncomingRequest` method is a non-static instance method, so `_processRequestDelegate = ProcessIncomingRequest` in the constructor allocates exactly once (the compiler-synthesised delegate wrapping the method pointer + receiver).

One alternative — marking the handler `static` and passing `this` explicitly — was considered but rejected as unnecessary complexity. Another alternative — using a static delegate field — would require passing all dependencies through separate means, becoming over-engineered for this call pattern.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- `PhysicsCollider` component must be registered in any `EntityRepository` that templates are applied to, otherwise `template.AddComponent` will silently no-op. The `SimHostInstance` test harness already registered `PhysicsCollider` in `BuildWorld()`, so Task 1 and Task 2 templates work correctly without any harness changes. However, if a test creates a bare `EntityRepository` without registering `PhysicsCollider`, the template silently drops it. 

- The `Country` field in `DISEntityType` is a `ushort` at offset 4 (little-endian in the ulong layout). This means the reconstruction formula uses `(ulong)Country << 32` — not a split shift — because the 2-byte country field occupies bits 32–47 of the ulong. This was verified by checking `FieldOffset` attributes on `DISEntityType`.

- `ComponentReflector` gets `Type` from `component?.GetType()` through `ImGui.DrawComponents`'s reflection path. For value types boxed through the ECS reflection API, `GetType()` returns the concrete struct type. `Marshal.SizeOf(type)` succeeds exactly when the type contains no managed fields, which is the guard condition used.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

- `ComponentReflector`: the `Marshal.AllocHGlobal` + `FreeHGlobal` path allocates a native heap block every frame per visible component. For a typical inspector view with 10–20 components this is negligible, but if the inspector becomes a hot path a pooled `NativeArray<byte>` or stackalloc approach for small structs (< 64 bytes) would eliminate the allocation entirely.
- `BdcTkbBuilder.WithPhysics`: `Math.Max(Length, Width)` executes twice per call (once to get the radius, once implicitly in `VehicleParams`). No practical concern at construction time.

---

## ⚠️ Outstanding Issues / Next Steps

- `EntityMission_MovesEntity` (Bagira.SimHost.Integration.Tests) is a **pre-existing failure** confirmed to fail at HEAD commit `309be3a` before any BD1-BATCH-03 changes. The mission pipeline (MissionAdapterSystem → BTreeTickSystem → MoveToExecutor) is not wired up to drive `NavState` through `NavigationIntent`→`NavigationStatus`→`CarKinematicsSystem` in the integration test harness. This should be filed as a separate investigation item.
- `FDP.Toolkit.ImGui.Tests` crashes when run in parallel with other assemblies in the solution-wide `dotnet test` invocation (native ImGui library loading conflict). All 42 tests pass when the project is run in isolation. This is a pre-existing infra limitation.
