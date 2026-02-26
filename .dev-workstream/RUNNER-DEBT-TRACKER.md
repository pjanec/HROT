# Runner Technical Debt Tracker

| ID | Title | Priority | Origin Batch | Description/Issue | Resolution Notes |
|---|---|---|---|---|---|
| RUN-DEBT-001 | `DdsParticipant.DomainId` narrowing | P3 | RUNNER-BATCH-02 | The property returns `uint` but all Runner models/config use `int`, requiring silent narrowing casts everywhere. Needs `DomainIdInt` property or codebase-wide switch to `uint`. | |
| RUN-DEBT-002 | `SubsystemOrchestrator` `IDisposable` | P3 | RUNNER-BATCH-02 | Unmanaged Raylib/ImGui context creation doesn't cleanly enforce disposal, breaking inversion-of-control if containers assume `IDisposable`. | |
| RUN-DEBT-003 | Subsystem Config Compile-time safety | P3 | RUNNER-BATCH-02 | `ISubsystem.Initialize(object config)` lacks compile-time safety and requires blind casting in subsystem implementations. An abstract generic base class or pattern improvement is requested. | |
| RUN-DEBT-004 | Wait-for Hint UX | P4 | RUNNER-BATCH-02 | Configuration wait strings reject bad spellings with correct set lists, but don't say *which* word failed. E.g. "Did you mean 'simhost'?" | |
| RUN-DEBT-005 | `WaitingRoomCoordinator` DDS polling thread | P3 | RUNNER-BATCH-02 | Using `Thread.Sleep(100)` occupies a thread for up to 30 seconds. Replace with DDS listener callback + `ManualResetEventSlim` for instant wake-up. | |
| RUN-DEBT-006 | Headless Fixed Update Burn | P4 | RUNNER-BATCH-02 | `SubsystemOrchestrator.Run()` spins hard in headless mode using Raylib's target FPS logic without graphics sleep context. | |
| RUN-DEBT-007 | NodeId Collision from TickCount | P3 | RUNNER-BATCH-02 | Using `Environment.TickCount` for NodeId causes collisions and overwritten announcements if two runners start <15ms apart. | |
