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
| IOS-DEBT-028 | P3 | TASK-TRACKER.md | TASK-TRACKER.md is missing IOS Phase P5 (Project Setup) which is defined in TASK-DETAILS-IOS.md. Need to synchronize task lists. | IOS-BATCH-01 | ✅ Resolved |
| IOS-DEBT-029 | P2 | IOS-BATCH-01-REPORT | IDerEntity.GetDescriptor<T>() returns default(T) for value types without TryGet interface, leading to risky HasDescriptor omissions. | SHARED-P8 | Open |
| IOS-DEBT-030 | P2 | IOS-BATCH-01-REPORT | TargetEntityId type mismatch: MissionControlRequest uses long while IDerRepo.GetEntity uses int. | SHARED/IOS | Open |
| IOS-DEBT-031 | P3 | IOS-BATCH-01-REPORT | MissionEditorService lacks ingress path (DDS reader) for the MissionControlAck topic. | IOS Phase 9 | ✅ Resolved |
| IOS-DEBT-032 | P3 | IOS-BATCH-01-REPORT | MissionEditorService lacks IDisposable implementation, leaving pending TaskCompletionSources orphaned correctly upon teardown. | IOS Phase 9 | ✅ Resolved |
| IOS-DEBT-033 | P3 | IOS-BATCH-02-REPORT | OrbatPanel.FindChildren scans all entities per node (O(n²)); replace with a CommanderId→children dictionary for repos with large entity counts. | IOS Phase 9 | ✅ Resolved |
| IOS-DEBT-034 | P3 | IOS-BATCH-02-REPORT | InteractionPanel.AddLog is not thread-safe; DDS ingress callbacks may fire on a non-main thread. Needs ConcurrentQueue drain model in Phase 9 app shell. | IOS Phase 9 | ✅ Resolved |
| IOS-DEBT-035 | P3 | INTS-BATCH-01-REPORT | MiniIosPanelState.SubmitViaGateway discards CreateEntityAsync task without propagating spawn failure back to UI. | Phase 3 | Open |
| IOS-DEBT-036 | P3 | INTS-BATCH-01-REPORT | DdsWriterAdapterTests require ddsc.dll/libddsc.so on PATH. Needs [Trait("Category","RequiresDds")] or similar to prevent clean-room CI failures. | Phase 3 | Open |

---

## Resolved Items (archive)

| ID | Sev | Description | Resolved In |
|---|---|---|---|
| IOS-DEBT-028 | P3 | TASK-TRACKER.md was missing IOS Phase P5 (Project Setup). P5 section added to TASK-TRACKER.md. | IOS-BATCH-01 |
| IOS-DEBT-031 | P3 | MissionEditorService ACK ingress path added utilizing IEventQueue. | IOS-BATCH-04 |
| IOS-DEBT-032 | P3 | MissionEditorService IDisposable resolving orphaned Tasks cleanly. | IOS-BATCH-04 |
| IOS-DEBT-033 | P3 | OrbatPanel O(n^2) traversal refactored to O(n) utilizing local CommanderId lookup map. | IOS-BATCH-04 |
| IOS-DEBT-034 | P3 | InteractionPanel.AddLog made thread-safe utilizing ConcurrentQueue in IosMock / InteractionPanel event draining. | IOS-BATCH-03 |

---

## Notes
- Initialized for SimHost development.
