# BATCH-04 Review

**Workstream:** stride-mock  
**Batch:** BATCH-04  
**Reviewer:** Dev Lead  
**Status:** APPROVED

---

## Summary

BATCH-04 delivers CA-01 (camera HandleInput fix) and SM-008 (FakeStrideApp). Both are
structurally correct. CA-01 correctly gates `RaylibInputProvider` behind the
`!_headless && _isActiveMapOwner()` guard. FakeStrideApp follows the mandatory OnLoad step
order, correctly omits `DemoTkbSetup.RegisterAll` (DT-005 resolved), and properly disposes
both `_core` and `_participant` in `OnUnload`.

Tests: 41/41 StrideMock, 3/3 FakeStrideApp = 44/44 total.

---

## Code Review

### CA-01 — StrideMockSubsystem.Update

| Concern | Verdict |
|---------|---------|
| `HandleInput(new RaylibInputProvider())` called | PASS |
| `!_headless && _isActiveMapOwner()` guard preserved | PASS |
| `using Fdp.Toolkit.Vis2D.Defaults` added | PASS |
| Headless test `Update_HeadlessAfterInitialize_DoesNotThrow` still passes | PASS |

SC_SM006_6 success condition now fully satisfied.

### SM-008 — FakeStrideApp

| Concern | Verdict |
|---------|---------|
| Inherits `FdpApplication` | PASS |
| OnLoad step order: participant, factory, config, BootstrapNode, TKB, script | PASS |
| `DemoTkbSetup.RegisterAll` correctly omitted with explanation | PASS |
| `UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates` called after BootstrapNode | PASS |
| `OnUpdate`: HandleInput, Camera.Update, script.Update, core.Tick | PASS |
| `OnDrawWorld`: BeginMode, entities, effects, EndMode | PASS |
| `OnDrawUI`: splash window on non-empty CurrentStateMessage | PASS |
| `OnUnload`: `_core.Dispose()` and `_participant.Dispose()`, base not called | PASS |
| `Hrot.FakeStrideApp.csproj`: test dir excluded from main project | PASS |

### DT-005 Resolution

`DemoTkbSetup.RegisterAll` omitted in FakeStrideApp with documented justification.
DT-005 is resolved — mark RESOLVED in DEBT-TRACKER.md.

---

## Test Quality

| Test | Coverage Quality |
|------|-----------------|
| `FakeStrideApp_InheritsFromFdpApplication` | Good — type safety |
| `FakeStrideApp_Constructor_WithValidConfig_DoesNotThrow` | Good — documents API |
| `FakeStrideApp_DefaultConfig_HasExpectedValues` | Acceptable — documents spec values |

Tests are appropriately limited by Raylib requirements. SC_SM008_2 through SC_SM008_7 are
integration-level (visual) and verified by manual testing only.

---

## Issues Found

None blocking. One P3 note:

**P3:** `FakeStrideApp_DefaultConfig_HasExpectedValues` tests a struct literal against
itself — it does not exercise `FakeStrideApp` internals. It serves as a spec documentation
test only. Acceptable given the constraints.

---

## Decision

**APPROVED** — no regressions, all requirements met, DT-005 resolved.

---

## Commit Message

```
feat: CA-01 + SM-008 - camera HandleInput fix + FakeStrideApp (BATCH-04)

CA-01: Fix StrideMockSubsystem.Update camera HandleInput
- Camera.HandleInput(new RaylibInputProvider()) now called when !headless && IsActiveMapOwner
- Satisfies SC_SM006_6 — camera pan/zoom works in ClusterRunner mode

SM-008: FakeStrideApp standalone Raylib/ImGui shell
- FakeStrideApp.cs: FdpApplication subclass with mandatory OnLoad step order
- OnLoad: DdsParticipant -> NedNetworkFactory -> BootstrapNode -> TKB -> SyncFdpToStrideScript
- DemoTkbSetup.RegisterAll omitted (HrotNodeBuilder pre-registers TkbType 100 via NedTkbCatalog)
- OnUpdate: Camera.HandleInput -> Camera.Update -> script.Update -> core.Tick
- OnUnload: disposes core + DDS participant; does not call base.OnUnload()
- Program.cs: replaces stub with real CLI entry point (--domain/--node args)
- Hrot.FakeStrideApp.Tests project created + added to IOS-IG-SimHost.sln

Resolves DT-005: DemoTkbSetup spec error confirmed on FakeStrideApp path.
Tests: 44/44 (41 StrideMock + 3 FakeStrideApp)
```
