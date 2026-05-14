# BATCH-03 Review

**Workstream:** stride-mock  
**Batch:** BATCH-03  
**Reviewer:** Dev Lead  
**Status:** APPROVED WITH DEBT ITEMS

---

## Summary

BATCH-03 implements SM-006 (`StrideMockSubsystem`) and SM-007 (ClusterRunner wiring). The
implementation is structurally correct — thin adapter pattern over `StrideNodeBootstrapper`,
proper lifecycle delegation, headless guards on rendering calls, and two sets of tests.
Tests: 41/41 StrideMock, 237/239 ClusterRunner (2 pre-existing failures unrelated to this batch).

---

## Code Review

### `StrideMockSubsystem.cs`

| Concern | Verdict |
|---------|---------|
| Implements ISubsystem + IMapCameraProvider | PASS |
| Constructor null-guards networkFactory | PASS |
| Initialize: BootstrapNode FIRST, then TKB population | PASS |
| TitleBarColor = (0.8, 0.4, 0.1, 1.0) orange | PASS |
| DrawWorld/DrawUI guarded on `_headless` | PASS |
| Shutdown disposes `_core` and nulls both refs | PASS |
| No business logic (thin adapter) | PASS |

### DemoTkbSetup.RegisterAll deviation

The agent correctly omitted `DemoTkbSetup.RegisterAll(tkb)` after verifying that
`HrotEnvironment.CreateTkb()` (called inside `HrotNodeBuilder.Build()`) invokes
`NedTkbCatalog.RegisterAll(tkb)`, which registers `TkbEntityTypes.Tank_M1Abrams = 100`.
Calling `DemoTkbSetup.RegisterAll` after would throw
`InvalidOperationException: Template with TkbType '100' already exists`.

`UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates` (IDs 1001-2003) is correctly called
after bootstrap. The design spec was written before this internal pre-registration was known.
The deviation is **correct and necessary**.

### `HrotRunnerConfiguration.cs` / `Program.cs`

- `"stridemock"` added to `validNames`. `"all"`/`"demo"` NOT expanded. Correct.
- `"STRIDEMOCK" => 700` inserted before the default `_ => 600`. Correct.

---

## Test Quality Review

### SM-006 Tests (StrideMockSubsystemTests)

| Test | Coverage Quality |
|------|-----------------|
| SC_SM006_1: Name property | Good |
| SC_SM006_2: TitleBarColor | Good |
| SC_SM006_3: Constructor null guard | Good |
| SC_SM006_3 (Init): Initialize no-throw | Acceptable; headless guard prevents TKB verification |
| SC_SM006_4: GetCameraView non-null | Good — proves bootstrap completed |
| SC_SM006_5: ApplyCameraView roundtrip with value assertion | Good |
| SC_SM006_6: Update no-throw | Acceptable; see P2 issue below |
| SC_SM006_7: DrawWorld headless no-throw | Acceptable; visual can't be unit-tested |
| SC_SM006_8: DrawUI headless no-throw | Acceptable |
| SC_SM006_9: Shutdown no-throw | Good |
| Shutdown before Initialize: no-throw | Good bonus test |

### SM-007 Tests (RunModeTests additions)

| Test | Coverage Quality |
|------|-----------------|
| SC_SM007_1: Validate "stridemock" mode | Good |
| SC_SM007_2: Validate "orchestrator,cgf,stridemock" | Good |
| SC_SM007_3: No regression on existing modes | Good |
| SC_SM007_4: ISubsystem reflection check | Acceptable; ResolveAppNodeId is private static — see debt DT-004 |
| SC_SM007_5: IMapCameraProvider reflection check | Good |
| AllMode does not contain stridemock | Good |

---

## Issues Found

### P2 Issues (must be tracked and addressed)

**P2-A: SC_SM006_6 — Camera.HandleInput never called**

The `StrideMockSubsystem.Update()` method calls `Camera.Update(dt)` but the block for
`Camera.HandleInput()` is commented out with a note deferring to SM-008. The SC_SM006_6
success condition explicitly requires HandleInput to be called when `IsActiveMapOwner()` is
true. Without this, the map will not pan or zoom when StrideMock is the active tab in
ClusterRunner mode.

The fix is simple and already headless-safe:
```csharp
if (!_headless && _isActiveMapOwner())
    _core.Camera.HandleInput(new RaylibInputProvider());
```

This is deferred to BATCH-04 as a corrective action (item CA-01). BATCH-04 (SM-008) will
also wire camera input in the standalone app, so both paths can be aligned at once.

**P2-B: SC_SM007_4 — ResolveAppNodeId(STRIDEMOCK) not directly verified**

`ResolveAppNodeId` is `private static` in `Program.cs` so reflection is needed to test it.
The agent used type-reflection tests instead. The actual mapping `"STRIDEMOCK" => 700` is
visible in the source but has no automated verification. Add DT-004 to debt tracker.

### P3 Issues

**P3: DemoTkbSetup.RegisterAll omission (DESIGN deviation)**

The design/spec calls for DemoTkbSetup.RegisterAll + UrbanCombatNewScenario, but
NedTkbCatalog already registers TkbType 100 internally. The spec was incorrect.
Document in debt tracker as reference for SM-008 (FakeStrideApp design says both calls
are needed — FakeStrideApp developer must re-verify if the same pre-registration happens
when using NedNetworkFactory).

---

## Decision

**APPROVED** — no blocking failures.  
P2-A corrective action added to BATCH-04 instructions.  
Debt items DT-004 and DT-005 added to DEBT-TRACKER.md.

---

## Commit Message

```
feat: SM-006 + SM-007 - StrideMockSubsystem + ClusterRunner wiring (BATCH-03)

SM-006: StrideMockSubsystem
- Thin ISubsystem + IMapCameraProvider adapter over StrideNodeBootstrapper
- Initialize: BootstrapNode first, then UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates
- DemoTkbSetup.RegisterAll omitted: HrotNodeBuilder pre-registers TkbType 100 via NedTkbCatalog
- Headless guards on DrawWorld/DrawUI/Update
- Orange title bar (0.8, 0.4, 0.1, 1.0)
- Hrot.StrideMock.csproj: added Fdp.Examples.Scenarios, Raylib-cs, rlImGui-cs

SM-007: ClusterRunner wiring
- HrotRunnerConfiguration: "stridemock" added to validNames (not to all/demo expansion)
- Program.cs: "STRIDEMOCK" => 700 in ResolveAppNodeId
- Hrot.ClusterRunner.csproj: Hrot.StrideMock project reference already present (BATCH-01)

Tests: 41/41 Hrot.StrideMock.Tests, 237/239 Hrot.ClusterRunner.Tests (2 pre-existing failures)
```
