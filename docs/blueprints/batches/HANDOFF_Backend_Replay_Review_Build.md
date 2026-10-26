<!--STATUS
state: LIVE
updated: 2026-10-26
current-answer: §1 what to build, in order · §2 the rails · §4 the gate report
stale-below: nothing
related-designs:
  - ../../DESIGN_Replay_Time_Axis_And_Review.md — OWNS everything this handoff dispatches (the sim axis, During Action Review, §12 the zombie cleanup on the wire); READY-TO-BUILD
  - ../../DESIGN_Checkpoint_And_Dry_Run_Restore.md — OWNS the checkpoint machinery the review's return reuses (WorldKeyframes, AfterRestore, ResumeAfterRestore, B4, the R1 oracle)
  - ../../designs/replay-and-modules/DESIGN.md — OWNS the replay gates; its §2.2 / §2.3 and §3.10.4 are SUPERSEDED (see its known-rot)
  - ../../DESIGN_Director_Timeline_Editor.md — ui's; OWNS the one timeline panel (P4 touches it)
-->
# HANDOFF — backend: build the replay's sim axis and During Action Review (CE-3329, CE-3331)

**RELEARN** — read `docs/blueprints/RULINGS.md` first, then this file.

| | |
|---|---|
| **Dispatched at** | `cfcb539e5` (branch `backend`) |
| **Scope** | ⛔ **FROZEN at `cfcb539e5`.** Documents that change after it are FYI only. A later document that invalidates an item ⇒ STOP that item and report it; do not adapt, do not revert. Every other item continues (R-106) |
| **Owning design** | [`docs/DESIGN_Replay_Time_Axis_And_Review.md`](../../DESIGN_Replay_Time_Axis_And_Review.md) — `build-state: READY-TO-BUILD`. Its UML (§3a states, §3b classes, §3c the review's sequence, §3d modules, §12a the wire) IS the design — build against it and report matches / deviations (obligation ③); fold every deviation back into it before the batch closes (obligation ⑤) |
| **Rulings that bind** | R-3056 (`atSeconds` stays wall) · R-3057 (R1–R12) · R-3058 (the axis switches at runtime) · R-3059 (no state change on a left click; every control from any host; per-host lanes) · R-3060 (the existing time-control toolbar grows two icons; ■ = back to live in a review; the Replay Browser keeps its own controls) · R-3061 (the mode icon is an indicator — a click opens a menu) · ⭐ **R-3062** (the review is ON the wire; a dispose for every potential zombie) · ⭐ **R-3063** (the full checkpoint; D-B…D-H) |
| **Tracker** | CE-3329 (this programme), CE-3331 (today's zombie hole), CE-3000 ④ (`WaitForAcks` in CycloneDds.NET — not this lane's repo) |
| **Ids** | backend block: next free **`CE-3332`**, **`R-3064`** |

## 0. Start

1. `git fetch origin backend ui behaviors coordinator`; work on `backend`; merge what you build on and name it in the started marker.
2. ⭐ Push `chore: started replay-review build at <sha>` BEFORE writing code (rule 1b).
3. `scripts/cbm.sh list_projects` in the BACKGROUND (CLAUDE.md); every existence claim — graph + grep.
4. Read the design's §11f and §12 FIRST, then §3, §7, §8. §8's 45 numbered rows are the work list; §11's F-numbers explain each row.

## 1. What to build, in order *(design §7; §8 row numbers)*

| step | §8 rows | what | accepted when |
|---|---|---|---|
| **PW** ⭐ first | 35, 36, 38, 39 | ⭐ **the wire (§12)**: `CycloneNetworkCleanupSystem` becomes a mark-and-sweep by network id (an id it published and ABSENT from the world ⇒ `DisposeAll`; present as a ghost ⇒ forget, no dispose; subsumes its lightweight step 1b) · the egress HOLD from a node's world swap to the master's commit (disposes pass) · `AfterRestore.Run` gains `SmartEgressUtil.ForceMarkAllDirty` · a STATE discard beside the events' `IDiscardableIngress`, used at a review's exit | §2's PW rails; ⭐ **CE-3331's rail red before, green after** (an ordinary replay's backward seek leaves an entity created after the target on the wire today) |
| ⏸ row 37 | 37 | `WaitForAcks` before "ready" (§12 W3) | ⛔ **NOT in this batch** — the binding lacks it (CE-3000 ④, needs the CycloneDds.NET repo). Leave a named seam, not a fake call |
| **P0** | 1–6 | v7 frame header (`SimTime`, `Flags`: operator-halt + changed); v6 read by decoding; `/replay/status` `simSeconds`; ⭐ **measure M2** (is `Changed` set on every paused frame?) on the CE-4053 recording and record it in §11c F21 | a stepped live run's index shows the OPERATOR pauses only (D-H) without decoding |
| **P1** | 7–13, 31, 32, 34, 45 | `ReplayTimeMap`, each node's map in `ReplayPrepareResult`, the master's intersection; `atSimSeconds`; the axis (`SetReplayAxisIntent`); `ReplayClockProcessManager` with a NON-pausing jump over an operator pause (R4 as amended — ⛔ never `SnapTo` there); `ReplaySessionTopic` (+ `ReviewId`); the Replay Browser's S / W icon | a sim seek lands every node on the same sim frame; a sim play crosses an operator pause without a kernel reset |
| **P2** | 14 | `ReviewGate` + `[RunsDuringReplay]` | every module outside the toggle groups declares; nothing undeclared ticks in a replay |
| **P3** | 15–20, 40–44 | the review states (`LoadingReview` / `OperatingReview` / `UnloadingReview`, both enum copies + the DDS mirror), `ReferenceReviewHandler` (§3c exactly: barrier pause → quiesce → forced `review-<n>` → FULL checkpoint with `CaptureOptions.Recording` → gates before the module → swap; exit: drain seeks → remove the module → apply → `ResumeAfterRestore` → wall + rate restored → recorder resumed with a keyframe and a cleared destruction log); the guard (D-E); `ClusterStates.IsReplaying`; `PlaybackController` `FileShare.ReadWrite`; `/review` routes; ⭐ **measure M3** (capture + apply time of a `Recording` keyframe on `uc-urban-battle`) | live → review → live identical (§2); a failure during a review leaves a finalised recording and a restorable `review-<n>` |
| **P4** | 21–30, 33 | the UI (§9): the mode indicator + axis icon in the EXISTING time-control section, ■ → ⏏ in a review, the timeline's right-click menu, the session layer, `LiveTimelineSource` | ⚠ **cross-lane — ui owns the editor**: first `git diff origin/ui` for CE-3330 (the timeline zoom / pan defects ui is fixing) and the panel's current shape; build only if the user says so, else send ui a NOTE with §9 + rows 21–30. Say which in the report |

⭐ **Stop rule:** a premise that fails mid-build stops THAT item, never the batch (R-106). An item whose design is wrong ⇒ report with the measured evidence; do not invent a different design silently.

## 2. The rails *(T-1 first: run the feature's own suite before writing a new one; add INTO it)*

| step | the feature's own suite — run FIRST | the new rails (into those suites) |
|---|---|---|
| PW | `Hrot.SimHost.Tests/CycloneNetworkCleanupSystemTests` · `Hrot.ClusterRunner.Integration.Tests/ZombieEntityMapTests` · `EntityDestroyIntegrationTests` · `CheckpointClusterTests` | a backward seek disposes the entity created after the target (CE-3331, red-proved) · a hand-over is forgotten, never disposed · a checkpoint restore leaves no entity of the abandoned future on the wire and republishes the survivors · the exit's state discard: no review-era sample reaches the restored world |
| P0 | `Fdp.Core.Tests/{FlightRecorderTests, PlaybackControllerTests, AsyncRecorderTests, MetadataTests}` | v7 round trip; v6 read by decoding gives the same index; `SteppingHeld` gaps are not stretches |
| P1 | `Hrot.Orchestrator.Tests/{ClusterMasterSeekTests, ReplaySeekProcessManagerTests}` · `Fdp.Toolkits.Tests/ReplayBrowser/Federation/FederatedReplayManagerTests` | the map's intersection; a sim seek → one wall frame on every node; the jump does not bump the timeline epoch |
| P2 | `Fdp.ModuleHost.Tests` (kernel) | every module outside the groups declares (a census rail) |
| P3 | `Hrot.Orchestrator.Tests/ClusterMasterCheckpointTests` · `Fdp.Toolkits.Tests/Replay/{WorldKeyframesTests, RecordingModuleTests, ReplayModuleTests}` · `Fdp.Toolkits.Tests/Orchestration/ReferenceHandlerTests` · `Hrot.Editor.Tests/Director/DirectorRunnerTests` (the R1 oracle, B5) · `Hrot.SimHost.Integration.Tests/RecordReplayIntegrationTests` | ⭐ live → review → live: world + singletons + the next N steps' trajectory = the straight run (R1's oracle reused) · the reader opens the live file (a test that runs on Windows — say so if it cannot run here) · world-changing ops answer 409 in a review · a node lost during a review ⇒ finalised recording + `review-<n>` restorable |

⛔ **The ClusterRunner integration suite is the system-level gate for PW and P3** (row 8 of the gate contract): name it and report running it, in isolation (`--filter`) if the full suite is flaky.

## 3. Traps that cost hours — read before the first build

| trap | do |
|---|---|
| build the PRODUCTION project, then `--no-build` test the TEST project ⇒ a stale DLL prints green | build the TEST project (`scripts/quick-check.sh <tests.csproj>`) |
| `--no-restore` after a merge that added a package ⇒ every scenario red the same way | after merging a lane, `dotnet restore <tests.csproj>` once |
| a full-solution build inside the fix loop (115 s vs 8 s) | build the affected project; the solution once, at the end |
| a foreground build / test blocks the user | `run_in_background: true`, keep working, never poll |
| `SnapTo` = `SnapAndPause` bumps the timeline epoch ⇒ the kernel resets every slot and drops in-flight async results (`ModuleHostKernel.cs:570-592`) | never on the review's entry or a sim-axis jump; the exit uses the checkpoint path (`ResumeAfterRestore`, CE-3316) |
| `CaptureOptions.NodeDryRun` drops singletons and `NoPreview` types (`WorldKeyframes.cs:55`) | the review captures with `CaptureOptions.Recording` |
| ingress during a replay discards EVENTS but leaves STATE in the reader (`CycloneNetworkIngressSystem.cs:92-104`, the join rule R-4007) | keep the join rule; the review's exit adds the state discard |
| replay-and-modules §2.2 / §2.3 say our IG does not replay | ⛔ superseded — EVERY recording node replays (R-4010) |

## 4. The gate report *(the reviewing lane does NOT re-run gates — the report substitutes)*

Per gate row: the verbatim command · pass / fail / skip · the delta vs baseline · a `--no-build` column · every red confirmed PRE-EXISTING against `cfcb539e5` · the tree clean after each run · quarantine counts · `tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · `mermaid-check.mjs` on the design · ⭐ the integration suite row (row 8) · every id allocated. Then: the design's diagrams vs what was built (obligation ③), and the design sections updated for every deviation (obligation ⑤). Tick CE-3331 and CE-3329's built steps in the tracker; update `RESUME_Backend_Lane.md`'s `current-answer`.

## 5. What is NOT in this batch

- §12 W3 / row 37 (`WaitForAcks`) — CE-3000 ④, the CycloneDds.NET repo.
- CE-3330 (the timeline's zoom / pan defects) — ui's.
- CE-4054's `inPreview` flag, CE-4050 — unrelated open backend rows.
