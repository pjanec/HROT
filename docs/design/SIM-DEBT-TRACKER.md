# Technical Debt & Deferred Issues Tracker

Tracks P2/P3 issues, known risks, and design decisions deferred from batch reviews.  
**P1 issues are never deferred** — they become Corrective Task 0 in the next batch.

Update this file when an item is resolved. Do not delete resolved rows — mark them ✅.

---

## How to Use

- **Dev lead:** during each review, add any new P2/P3 items here before writing the next batch.  
- **Developer:** check this file during onboarding. If your batch touches a file mentioned here, fix the relevant item even if it wasn't explicitly assigned.
- **Priority:** P2 = fix within the next 1–2 batches; P3 = fix before Phase complete or whenever the area is touched.

---

## Open Items

| ID | Sev | Source | Description | Target | Status |
|---|---|---|---|---|---|
| SIM-DEBT-02 | P3 | `EntityMissionEgressTranslator` | Table-level dirty flag tracking causes minor over-scan evaluation if entity A is changed and entity B is not, triggering a read of both. | TBD | Open |
| SIM-DEBT-03 | P2 | `EntityMissionTranslator` | Late-join race condition. Unknown entity IDs are safely silently skipped, but `EntityMission` data is dropped if it arrives before `EntityMaster`. | TBD | Open |
| SIM-DEBT-04 | P3 | `MissionAdapterSystem` | Unregistered BehaviorId string logs warning continuously. Implement Idle fallback behavior (`SimHostDoctrineIds.Idle_HSM = 3010`) to prevent log-spam. | TBD | Open |
| SIM-DEBT-05 | P2 | `VehicleAPI` | `VehicleAPI.JoinFormation` currently does not take a `FormationType` parameter. Implement the API overload to forward this correctly to `VehicleCommandSystem`. | TBD | Open |
| SIM-DEBT-06 | P4 | `Program.cs` | Refactor the setup ordering sequence if `VehicleAPI` needs data loaded upstream. Create a factory pattern like `SimulationLogicModule.Build(IKernelServices)` to separate concerns and setup bounds gracefully. | TBD | Open |
| SIM-DEBT-07 | P4 | `Integration.Tests` | Extract `MockIOSClient` and `SimHostInstance` into a reusable `DDS.TestMocks` library for broader test capability across component projects. | TBD | Open |
| SIM-DEBT-08 | P3 | INTS-BATCH-01-REPORT | `SimHostScenarioManager.MapVehicleClassToTkbType` uses arbitrary string-to-enum dispatch mapping which risks throwing on typos if converted. Harden this API before extending vehicle types. | Phase 2 | Open |
| SIM-DEBT-09 | P4 | INTS-BATCH-03-REPORT | Duplicated app-bootstrap composition across IG/SimHost/Runner paths. Create a generalized, shared composition helper in an app-layer assembly. | Phase 4 | Open |

| RUNNER-DEBT-001 | P3 | RUNNER-BATCH-01 | `AsyncRecorder` has `Dispose()` but doesn't implement `IDisposable` — violates .NET conventions, prevents polymorphic usage. | FDP.Kernel.FlightRecorder | Open |
| RUNNER-DEBT-002 | P3 | RUNNER-BATCH-01 | `ComponentTypeRegistry` fully static — makes test isolation hard, requires `Clear()` in every fixture, breaks parallel test runners. Propose `AsyncLocal<ComponentTypeRegistry>` instance pattern. | FDP.Kernel | Open |
| RUNNER-DEBT-003 | P3 | RUNNER-BATCH-01 | `PlaybackController.LoadMetadata()` swallows all exceptions, silently treats corrupted `.meta.json` as old recording. Add structured `MetadataLoadException` with file path. | FDP.Kernel.FlightRecorder | Open |
| RUNNER-DEBT-004 | P3 | RUNNER-BATCH-01 | No binary format version negotiation — `FdpConfig.FORMAT_VERSION` not checked at playback. Add `RecordingMetadata.FormatVersion` validation. | FDP.Kernel.FlightRecorder | Open |
| RUNNER-DEBT-005 | P3 | RUNNER-BATCH-01 | `ComponentTypeRegistry.GetOrRegister<T>()` is `internal` — third-party plugins cannot register custom components. Add public `Register<T>()` API with registration-phase lock. | FDP.Kernel | Open |

---

## Resolved Items (archive)

| ID | Sev | Description | Resolved In |
|---|---|---|---|
| SIM-DEBT-01 | P1 | `WGS84Transform` matrix rotation column vs row mix-up (found via test) | BATCH-01 ✅ |

---

## Notes
- Initialized for SimHost development.
