# Replay Browser Frankenstein — ONBOARDING

Welcome. Read [DEV-GUIDE.md](../../DEV-GUIDE.md) first — it defines how to work in this repository (commit style, testing, batches, debt handling).

---

## What we are building

We are extending the existing **Replay Browser** so an operator can open the per-node `.fdp` recordings from one distributed exercise (Brain / Muscle / IG / …) together, align them on a wall-clock tick, dial per-node offsets, and see a **single mathematically correct merged ECS snapshot** synthesised from all loaded contexts.

The merged view is "Frankenstein": for every operator action it tears down and re-builds a fresh `EntityRepository` by extracting authority-filtered component slices from every node via `ScenarioSerializer` and remapping all relational entity handles through a custom `IGuidResolver`. Severe scrub stutter is the accepted cost; correctness is the goal.

The feature is **offline only** (post-mortem). It has no 60 Hz or zero-allocation budget.

There is **no fast inspector-level federation tier** — the architecture has only the transient-master path. This was a deliberate user decision.

---

## Where to read

In order, before writing code:

1. [merged-view.md](./merged-view.md) — the source design talk that originated this work.
2. [DESIGN.md](./DESIGN.md) — the binding architecture and rationale. All success conditions (SC-1…SC-6) are defined here.
3. [TASK-DETAILS.md](./TASK-DETAILS.md) — per-task scope, DESIGN cross-references, and binary success conditions (typically unit-test names).
4. [TASK-TRACKER.md](./TASK-TRACKER.md) — phase/task checklist; tick boxes as you complete tasks.
5. [DEBT-TRACKER.md](./DEBT-TRACKER.md) — every deferred non-critical issue lives here; do not silently leave debt out of the tracker.

---

## Codebase tour

### Components we are refactoring / extending

| What | Where | Reason |
|---|---|---|
| `RecordingMetadata` | [FDP/Engine/Fdp.Core/FlightRecorder/Metadata/RecordingMetadata.cs](../../FDP/Engine/Fdp.Core/FlightRecorder/Metadata/RecordingMetadata.cs) | Add `ExerciseId`, `NodeId`. |
| `RecordingConfiguration` | [FDP/Toolkits/Fdp.Toolkits/Replay/RecordingConfiguration.cs](../../FDP/Toolkits/Fdp.Toolkits/Replay/RecordingConfiguration.cs) | Add required `NodeId`. |
| `RecordingModule` | [FDP/Toolkits/Fdp.Toolkits/Replay/RecordingModule.cs](../../FDP/Toolkits/Fdp.Toolkits/Replay/RecordingModule.cs) | Bridge — build `RecordingMetadata { ExerciseId, NodeId }` and pass it into `new AsyncRecorder(path, metadata)`. `AsyncRecorder` itself is unchanged. |
| `ReplayBrowserContext` | [FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/ReplayBrowserContext.cs](../../FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/ReplayBrowserContext.cs) | Still used per-file. Extract its private component-priming flow to a shared helper. |
| `ScenarioSerializer` | [FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs](../../FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs) | Add a `DeserializeWith(IGuidResolver)` overload that does NOT throw on missing GUIDs. |
| `ReplayTimelinePanel` | [FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplayTimelinePanel.cs](../../FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplayTimelinePanel.cs) | Multi-file open. |
| `ReplayBrowserSubsystem` | [Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs](../../Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs) | Replace single context with the federation manager; swap active repo on mode/time change. |

### Components we depend on (don't modify their behaviour)

| What | Where | Why we need it |
|---|---|---|
| `PlaybackController.SeekToWallClockTicks` | [FDP/Engine/Fdp.Core/FlightRecorder/PlaybackController.cs](../../FDP/Engine/Fdp.Core/FlightRecorder/PlaybackController.cs#L245) | Binary-search seek; thread-safe per the existing `replay-time` design. |
| `IGuidResolver` | [FDP/Toolkits/Fdp.Toolkits/Scenario/IGuidResolver.cs](../../FDP/Toolkits/Fdp.Toolkits/Scenario/IGuidResolver.cs) | Our `FederatedGuidResolver` implements it. |
| `NetworkIdentity` | [FDP/Toolkits/Fdp.Toolkits/Replication/Components/NetworkIdentity.cs](../../FDP/Toolkits/Fdp.Toolkits/Replication/Components/NetworkIdentity.cs) | Global key (long → Guid via `NetworkIdGuid`). |
| `NetworkAuthority` | [FDP/Toolkits/Fdp.Toolkits/Replication/Components/NetworkAuthority.cs](../../FDP/Toolkits/Fdp.Toolkits/Replication/Components/NetworkAuthority.cs) | `PrimaryOwnerId` orders nodes for consensus extraction. |
| `EntityMetadataCold.AuthorityMask` | [FDP/Engine/Fdp.Core/EntityMetadataCold.cs](../../FDP/Engine/Fdp.Core/EntityMetadataCold.cs#L17) | 512-bit per-entity local-authority mask; recorded into every `.fdp`. |
| `DescriptorOwnership` | [FDP/Toolkits/Fdp.Toolkits/Replication/Components/DescriptorOwnership.cs](../../FDP/Toolkits/Fdp.Toolkits/Replication/Components/DescriptorOwnership.cs) | Component-level ownership map (DDS descriptor → owner). The `.fdp` already captures the resolved `AuthorityMask`, so synthesis does not need to walk this map at replay time. |
| `RepositoryAdapter` | [FDP/Engine/Fdp.Presentation/ImGui/Adapters/RepositoryAdapter.cs](../../FDP/Engine/Fdp.Presentation/ImGui/Adapters/RepositoryAdapter.cs) | The UI's `IInspectableSession`. We rebind it when the active repo changes. |

### New code lands here

- `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/` — `FederatedReplayManager`, `FederatedGuidResolver`, `TransientMasterBuilder`, `NetworkIdGuid`.
- `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/FederationPanel.cs` — the new operator-facing panel.

### Tests

- Headless logic (manager, resolver, builder, serializer overload) → `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Federation/`.
- UI behaviour (panel callbacks, repo rebind, paradox flagging) → `FDP/Engine/Fdp.Presentation.Tests/ImGui/ReplayBrowser/`.
- Recorder/metadata round-trips → existing `FDP/Engine/Fdp.Core.Tests/` neighbourhood.

---

## How to build and run

```powershell
# Restore + build the whole solution (top-level .sln or directly the projects you touched):
dotnet build

# Run all replay-browser-related tests:
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --filter "FullyQualifiedName~ReplayBrowser"
dotnet test FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj      --filter "FullyQualifiedName~Recording"
dotnet test FDP/Engine/Fdp.Presentation.Tests/Fdp.Presentation.Tests.csproj --filter "FullyQualifiedName~ReplayBrowser"

# Launch the replay browser subsystem interactively:
#   (top-level Hrot ClusterRunner; -m selects the subsystem)
dotnet run --project Hrot/Runner/Hrot.ClusterRunner -- -m replaybrowser
```

You will need a set of `.fdp` recordings from the same exercise to exercise the merged view by hand. Recordings produced **before** Phase P1 has shipped will lack `ExerciseId`/`NodeId` and will be rejected by the group loader — that is intended.

---

## Operator-visible quirks to keep in mind

These are designed-in behaviours; surface them to operators rather than try to "fix" them.

- **No Play in Merged View.** The Play / Pause button is disabled while Merged View is active. Allowing continuous play would trigger a full JSON-round-trip rebuild every frame and freeze the application. Operators step frame-by-frame, drag the slider, or edit the base wall-tick to navigate. Switching back to Single-Node View re-enables Play. (DESIGN §6.2.1.)
- **No Search in Merged View.** `IRecordingSearchService` searches by spinning up an isolated `PlaybackController` against an on-disk `.fdp` file; it cannot search the synthesised transient master. In Merged View the search panel shows: *"Search is disabled in Merged View. Switch to Single-Node View to search a specific recording."*. (DESIGN §6.2.2.)
- **Component Diff works in Merged View, but "Seek Prev/Next Change" arrows are disabled.** Passive frame-to-frame diff is fully supported — when the selection or time changes, the subsystem rebuilds the transient master TWICE (once for "before", once for "after") and feeds both JSON DOMs into `ComponentDiffService.ComputeTreeDiff`. Each step is heavy, but acceptable for one-frame-at-a-time analysis. The "Seek to Previous/Next Change" transport arrows next to the diff panel are greyed out in Merged View because their algorithm would require thousands of rebuilds. (DESIGN §6.2.3.)
- **Local-only entities follow the "Local-Entities Provider" node.** Entities without `NetworkIdentity` (local visual effects, UI markers, camera anchors) cannot be cross-correlated. The Merged View injects them from a single designated provider node (defaults to the lowest-numbered loaded NodeId — usually the Brain/CGF). The `FederationPanel` exposes a dropdown to change the provider. Operators may see local markers from non-provider nodes "disappear" in Merged View — that is by design. (DESIGN §7.8.)
- **`Entity.Null` fields in Merged View are flagged.** A field showing `Entity.Null` in Merged View is rendered in a warning colour with a tooltip explaining the two possible causes: a manual time offset OR a recorded cluster desync (packet loss in the original live run). The flag fires regardless of whether offsets are currently non-zero. (DESIGN §8.3.)
- **Merged View scrub stutter is normal.** Each scrub / step / offset / provider change rebuilds the whole transient `EntityRepository` via JSON round-trip. Hundreds of milliseconds to seconds per rebuild is expected. (DESIGN §9, SC-6.)

## Out of scope (do not touch)

- **Universal Breakpoint integration.** The merged-view spec mentions extending breakpoint-hit panels to expose `GlobalTime.TotalWallTicks`. We decided this is delivered separately. The operator pastes / types the wall-tick value into the federation panel. Do not add coupling between this code and the breakpoint code.
- **Live cluster rewind.**
- **Corrective DDS publishing.**
- **Tier-1 (cheap) inspector federation.** The architecture deliberately omits it.
- **Performance optimisation of the merged view.** Stutter is expected; SC-6 forbids stopwatch-based regression gates.

---

## Working norms

- Read [DEV-GUIDE.md](../../DEV-GUIDE.md) — it defines batch sizes, commit conventions, review/report cadence, and the debt-tracker discipline.
- Never silently leave issues unfinished. Either fix in-batch (P1 priority items must) or record them in [DEBT-TRACKER.md](./DEBT-TRACKER.md) with an explicit target batch.
- Verify each task's success conditions (named unit tests) before checking it off in the tracker.
