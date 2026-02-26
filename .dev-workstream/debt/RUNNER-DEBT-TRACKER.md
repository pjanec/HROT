# Technical Debt & Deferred Issues Tracker — Runner

Tracks P2/P3 issues, known risks, and design decisions deferred from Runner batch reviews.  
**P1 issues are never deferred** — they become Corrective Task 0 in the next batch.

Update this file when an item is resolved. Do not delete resolved rows — mark them ?.

---

## How to Use

- **Dev lead:** during each review, add any new P2/P3 items here before writing the next batch.  
- **Developer:** check this file during onboarding. If your batch touches a file mentioned here, fix the relevant item even if it wasn't explicitly assigned.
- **Priority:** P2 = fix within the next 1–2 batches; P3 = fix before Phase complete or whenever the area is touched.

---

## Open Items

| ID | Sev | Source | Description | Target | Status |
|---|---|---|---|---|---|
| RUNNER-DEBT-001 | P3 | RUNNER-BATCH-01 | `AsyncRecorder` has `Dispose()` but doesn't implement `IDisposable` — violates .NET conventions, prevents polymorphic usage. | FDP.Kernel.FlightRecorder | Open |
| RUNNER-DEBT-002 | P3 | RUNNER-BATCH-01 | `ComponentTypeRegistry` fully static — makes test isolation hard, requires `Clear()` in every fixture, breaks parallel test runners. Propose `AsyncLocal<ComponentTypeRegistry>` instance pattern. | FDP.Kernel | Open |
| RUNNER-DEBT-003 | P3 | RUNNER-BATCH-01 | `PlaybackController.LoadMetadata()` swallows all exceptions, silently treats corrupted `.meta.json` as old recording. Add structured `MetadataLoadException` with file path. | FDP.Kernel.FlightRecorder | Open |
| RUNNER-DEBT-004 | P3 | RUNNER-BATCH-01 | No binary format version negotiation — `FdpConfig.FORMAT_VERSION` not checked at playback. Add `RecordingMetadata.FormatVersion` validation. | FDP.Kernel.FlightRecorder | Open |
| RUNNER-DEBT-005 | P3 | RUNNER-BATCH-01 | `ComponentTypeRegistry.GetOrRegister<T>()` is `internal` — third-party plugins cannot register custom components. Add public `Register<T>()` API with registration-phase lock. | FDP.Kernel | Open |

---

## Resolved Items (archive)

| ID | Sev | Description | Resolved In |
|---|---|---|---|
| | | | |

---

## Future Considerations (Not Debt)

**Roslyn Analyzer for Missing `[ComponentId]` Attributes**  
*Priority:* P4 (nice-to-have) | *Effort:* 3-5 days | *Source:* RUNNER-BATCH-01

Emit a warning when a struct used in `world.AddComponent<T>()` lacks `[ComponentId]` attribute. Prevents developers from forgetting to annotate new components when `EnforceExplicitComponentIds = false` in test mode.

---

## Metrics

| Metric | Value |
|--------|-------|
| Total Open | 5 |
| P2 | 0 |
| P3 | 5 |
| Total Effort | 3.85 days |

---

## Notes

All debt from RUNNER-BATCH-01 is **P3** and **non-blocking**. Smart architectural decisions avoided the need for immediate fixes.
