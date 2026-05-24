# BATCH-14 Report

**Tasks:** GZ037, GZ038  
**Date:** 2026-05-07  
**Status:** Complete

---

## Files Created

### FDP submodule (`FDP/`)
| File | Description |
|------|-------------|
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Network/IDdsReader.cs` | New `IDdsReader<T>` interface |

### FDP submodule — Modified
| File | Change |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/DebugPrimitiveBuffer.cs` | Added `AppendRaw(in DebugPrimitive)` public method |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/DebugPrimitiveBufferPersistenceTests.cs` | Added SC-GZ038-5, SC-GZ038-7 tests |

### Root repo (`Hrot/Network/Hrot.Network.NED/Gizmos/`)
| File | Description |
|------|-------------|
| `GizmoInteractionEventKind.cs` | Enum (Started/DragUpdate/Commit/Cancel) |
| `GizmoInteractionBatch.cs` | DDS topic struct `[DdsTopic("GizmoInteractionBatch")]` |
| `GizmoInteractionEgressSystem.cs` | IG-side system draining local bus → DDS write |
| `GizmoInteractionIngressSystem.cs` | SimHost-side system DDS read → bus publish |
| `DebugPrimitivesIngressTranslator.cs` | Render-thread translator DebugPrimitivesBatch → buffer |

### Root repo — Modified
| File | Change |
|------|--------|
| `Hrot/Subsystems/Hrot.IG/IgApplication.cs` | Removed `DataDrivenGizmoSystem` and `StatelessGizmoSystem` kernel registrations |

### Test files created
| File | Tests |
|------|-------|
| `Hrot/Network/Hrot.Network.NED.Tests/GizmoInteractionTranslatorTests.cs` | SC-GZ037-1 through SC-GZ037-8 |
| `Hrot/Network/Hrot.Network.NED.Tests/GizmoIngressTranslatorTests.cs` | SC-GZ038-1, SC-GZ038-3, SC-GZ038-4 |

---

## Build Result

```
dotnet build IOS-IG-SimHost.sln --no-incremental -clp:ErrorsOnly
```
**Result:** Build succeeded. 0 Error(s).

---

## Test Results

### SC-GZ037 (Hrot.Network.NED.Tests)
| Test | Result |
|------|--------|
| SC-GZ037-1: GizmoInteractionBatch has DdsTopicAttribute | PASS |
| SC-GZ037-2: Egress writes DragUpdate record correctly | PASS |
| SC-GZ037-3: Ingress translates Commit to CommitEvent | PASS |
| SC-GZ037-4: Dead entity DragUpdate → CancelEvent | PASS |
| SC-GZ037-5: Cancel always forwarded | PASS |
| SC-GZ037-6: Field preservation round-trip | PASS |
| SC-GZ037-7: Null writer — no-op | PASS |
| SC-GZ037-8: Null reader — no-op | PASS |

Total NED tests: **81 Passed, 0 Failed**

### SC-GZ038 (Fdp.Toolkits.Tests)
| Test | Result |
|------|--------|
| SC-GZ038-5: AppendRaw overflow increments DroppedCount | PASS |
| SC-GZ038-7: AppendRaw-populated buffer has correct frame content | PASS |

SC-GZ038-1, SC-GZ038-3, SC-GZ038-4 tested in `Hrot.Network.NED.Tests` (all PASS).  
SC-GZ038-2 is verified implicitly by build success.

---

## Git Commit Hashes

- **FDP submodule:** `12a0902` — "GZ037/GZ038: IDdsReader interface and AppendRaw on DebugPrimitiveBuffer"
- **Root repo:** `9c7eacc` — "GZ037/GZ038: GizmoInteraction DDS translators, IG dumb terminal ingress"

---

## Deviations from Spec

1. **`SystemPhase.PreSimulation` does not exist.** Used `SystemPhase.BeforeSync` instead.  
   `PreSimulation` is not a valid value in the `SystemPhase` enum (values are: Input, BeforeSync, Simulation, PostSimulation, Export, Manual). `BeforeSync` is the closest equivalent for pre-simulation DDS ingress/egress.

2. **Test SC-GZ038-1/3/4 placed in `Hrot.Network.NED.Tests` instead of FDP tests.**  
   The spec suggests adding SC-GZ038-1/3/4 to `DebugPrimitiveBufferPersistenceTests.cs` but `DebugPrimitivesIngressTranslator` lives in `Hrot.Network.NED`, so the tests were placed in the NED test project (`GizmoIngressTranslatorTests.cs`) for proper code locality. SC-GZ038-5 and SC-GZ038-7 were added to `DebugPrimitiveBufferPersistenceTests.cs` as specified.

3. **`_gizmoRegistry`, `_gizmoBuffer`, `_statelessGizmoRegistry`, `_gizmoSettingsRegistry` fields and `GizmoRegistry` public property preserved** in `IgApplication.cs` per spec instructions.

---

## TASK-TRACKER Status

- [x] **TASK-GZ037** Networked GizmoInteractionEvent DDS translators
- [x] **TASK-GZ038** IG dumb terminal — DebugPrimitivesIngressTranslator + removed system registrations
