# BATCH-04 Report

**Workstream:** stride-mock
**Batch:** BATCH-04
**Tasks:** CA-01 (corrective) + SM-008
**Status:** COMPLETE

---

## 1. Files Created/Modified

| File | Action |
|------|--------|
| `Hrot\Subsystems\Hrot.StrideMock\StrideMockSubsystem.cs` | Modified (CA-01) |
| `Hrot\Runner\Hrot.FakeStrideApp\FakeStrideApp.cs` | Created (SM-008) |
| `Hrot\Runner\Hrot.FakeStrideApp\Program.cs` | Replaced stub (SM-008) |
| `Hrot\Runner\Hrot.FakeStrideApp\Hrot.FakeStrideApp.csproj` | Modified (exclude test subdir) |
| `Hrot\Runner\Hrot.FakeStrideApp\Hrot.FakeStrideApp.Tests\Hrot.FakeStrideApp.Tests.csproj` | Created (SM-008) |
| `Hrot\Runner\Hrot.FakeStrideApp\Hrot.FakeStrideApp.Tests\FakeStrideAppTests.cs` | Created (SM-008) |
| `IOS-IG-SimHost.sln` | Modified — added Hrot.FakeStrideApp.Tests project |

---

## 2. CA-01 Fix Description

**File:** `Hrot\Subsystems\Hrot.StrideMock\StrideMockSubsystem.cs`

**Problem:** `Update()` contained a stub comment block inside the `!_headless && _isActiveMapOwner()` guard instead of actually calling `Camera.HandleInput()`. This broke SC_SM006_6 which requires the camera to accept mouse/keyboard input in non-headless mode when the subsystem is the active map owner.

**Fix applied:**

1. Added `using Fdp.Toolkit.Vis2D.Defaults;` to the using block.
2. Replaced the empty stub `if` body with:
   ```csharp
   if (!_headless && _isActiveMapOwner())
       _core.Camera.HandleInput(new RaylibInputProvider());
   ```

The `!_headless` guard means `RaylibInputProvider` (which calls Raylib polling APIs) is never invoked in unit test contexts.

---

## 3. CA-01 Test Results (StrideMock)

```
Test Run Successful.
Total tests: 41
     Passed: 41
 Total time: 1.4953 Seconds
```

All 41 pre-existing tests pass unchanged, including `Update_HeadlessAfterInitialize_DoesNotThrow`.

---

## 4. SM-008 Test Results (FakeStrideApp)

```
Test Run Successful.
Total tests: 3
     Passed: 3
 Total time: 0.8444 Seconds
```

Tests:
- `FakeStrideApp_InheritsFromFdpApplication` (SC_SM008_1 type conformance)
- `FakeStrideApp_Constructor_WithValidConfig_DoesNotThrow` (SC_SM008_1 constructor safety)
- `FakeStrideApp_DefaultConfig_HasExpectedValues` (config defaults: 1280x720, 60fps, correct title)

---

## 5. Total Test Counts

| Project | Passed | Failed | Total |
|---------|--------|--------|-------|
| Hrot.StrideMock.Tests | 41 | 0 | 41 |
| Hrot.FakeStrideApp.Tests | 3 | 0 | 3 |
| **Grand total** | **44** | **0** | **44** |

---

## 6. Build Results

```
dotnet build "Hrot\Runner\Hrot.FakeStrideApp\Hrot.FakeStrideApp.csproj"
  13 Warning(s)   [all pre-existing, unrelated to this batch]
  0 Error(s)
```

Build succeeded. Warnings are all pre-existing (RS2008, CA2014, CS8602, CS8604, RS1032, CS0169) in unrelated projects.

One structural fix was needed: the main `Hrot.FakeStrideApp.csproj` uses SDK-style glob inclusion which picks up `.cs` files in subdirectories. Since the `Hrot.FakeStrideApp.Tests` subdirectory lives under the main project folder, an explicit exclude was added:

```xml
<ItemGroup>
    <Compile Remove="Hrot.FakeStrideApp.Tests\**" />
    <EmbeddedResource Remove="Hrot.FakeStrideApp.Tests\**" />
    <None Remove="Hrot.FakeStrideApp.Tests\**" />
</ItemGroup>
```

---

## 7. DT-005 Resolution Note

DT-005 (design spec error: calling `DemoTkbSetup.RegisterAll(tkb)` in `OnLoad()`) is **resolved**.

`FakeStrideApp.OnLoad()` does NOT call `DemoTkbSetup.RegisterAll`. The comment in the code explains: `HrotNodeBuilder.Build()` internally calls `HrotEnvironment.CreateTkb()` which already invokes `NedTkbCatalog.RegisterAll(tkb)`, registering `TkbEntityTypes.Tank_M1Abrams = 100` and other NED types. Calling `DemoTkbSetup.RegisterAll` a second time would throw `InvalidOperationException: Template with TkbType '100' already exists`.

Only `UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates(tkb)` is called, which registers IDs 1001-2003 (no overlap with the NED catalog).

---

## 8. Deviations from Spec

| # | Deviation | Reason |
|---|-----------|--------|
| 1 | `EffectType` imported from `Hrot.IG.Components` (not `Hrot.StrideMock`) | `EffectType` is defined in `Hrot.IG.Components.VisualEffectState.cs`; `Hrot.StrideMock` does not declare its own `EffectType`. This is consistent with how `StrideMockSubsystem.cs` uses it (via `using Hrot.IG.Components`). |
| 2 | `<Compile Remove="...">` added to `Hrot.FakeStrideApp.csproj` | SDK-style .csproj auto-includes subdirectory `.cs` files; the test project folder must be excluded from the main exe project. This is a standard MSBuild pattern, not a design deviation. |
| 3 | Window title uses `--` instead of `--` em-dash (Unicode) | AGENTS.md requires no Unicode in string literals. Used `--` (two ASCII hyphens) instead of the em-dash in the instruction spec. |

---

## 9. Suggested Git Commit Message

```
feat: CA-01 + SM-008 - Camera.HandleInput fix + FakeStrideApp (BATCH-04)

CA-01: StrideMockSubsystem camera input fix
- Update() now calls Camera.HandleInput(new RaylibInputProvider()) when
  !_headless && _isActiveMapOwner(), satisfying SC_SM006_6
- Added using Fdp.Toolkit.Vis2D.Defaults to StrideMockSubsystem.cs
- All 41 StrideMock.Tests still pass

SM-008: FakeStrideApp standalone windowed runner
- FakeStrideApp.cs: FdpApplication subclass; OnLoad step order per DESIGN.md
  §4.2 (participant -> factory -> nodeConfig -> BootstrapNode -> TKB -> script)
- Program.cs: top-level entry with --domain / --node CLI args; defaults 0/700
- Hrot.FakeStrideApp.Tests: 3/3 tests (type conformance, ctor, config defaults)
- Hrot.FakeStrideApp.csproj: exclude test subdir from glob compilation
- IOS-IG-SimHost.sln: Hrot.FakeStrideApp.Tests added (Runner solution folder)

DT-005 resolved: DemoTkbSetup.RegisterAll not called; NedTkbCatalog already
registers TkbType 100 via HrotNodeBuilder.Build() -> HrotEnvironment.CreateTkb()

Tests: 41/41 StrideMock.Tests + 3/3 FakeStrideApp.Tests = 44/44 total
```
