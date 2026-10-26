<!--STATUS
state: LIVE — ui → coordinator: the replay-UI-state handoff is done (all three items)
updated: 2026-10-26
current-answer: the whole note
stale-below: nothing
related-designs:
  - docs/UX/UX_Feature_Selection.md — §2.7.19, the as-built of ① and ② (class + sequence)
  - docs/DESIGN_Entity_Brain_View.md — §12e's audit table, the row ③ adds
  - docs/DESIGN_Replay_Census_H0.md — the census whose §2 findings these close
-->

# NOTE — ui → coordinator: [HANDOFF_Coordinator_To_Ui_Replay_Ui_State](HANDOFF_Coordinator_To_Ui_Replay_Ui_State.md) is done (`CE-4049`, `CE-4047`)

Started at `72930e0b2` (behaviors, backend and coordinator merged first). Built on `ui`: `270aeec02` (① ②), `3090f0167` (③),
`c1530b9dc` (as-built). No item was invalidated by a later document.

| # | what was built | as-built |
|---|---|---|
| **①** | `SelectionStore` — a `Transient` managed world singleton keyed by network id (component id 465, ui's block) — is the truth; `SelectionState` stays as its per-entity copy for its ~14 readers, now `NoScenario \| NoReplay`, rewritten each frame by `EcsSelectionState.Sync` (first thing in `SelectionRequestSystem`, which every map host registers). `EcsSelectionState` — the only writer — writes both. The IG's `ContextMenuState` is `Transient`. `GET /replay/selection` was already withdrawn by backend (R-3052…R-3055) — no route or catalog change here | [UX Selection §2.7.19](../../UX/UX_Feature_Selection.md) |
| **②** | 📐 **measured first** (the new rail, red before the change): select, centre, rename, tool, shortcut and world-reset were all RECORDED and came back from a player given an event bus. In production none re-fired yet — the cluster replay passes no bus (`ReplayModule`), the Replay Browser injects into its context's own `SandboxBus`, not the world's, and a dry-run exit restores no events — but every recording carried them. All six are `NoReplay` now | same |
| **③** | `SquadDrillState` (`NoScenario`, recorded; component id 466) written beside the transient `SquadDrillTrace` by its one `Put`, registered in `CognitiveComponentRegistry`; the Brain tab's Squad line reads it. ⚠ **cross-lane** in behaviors' `Fdp.Toolkits/Squad` and SimHost's registry. The trace's orders / history stay brain-local — `/entities/{id}/squad`'s `maneuver` is still live-only | [Brain View §12e](../../DESIGN_Entity_Brain_View.md) audit row |

## Gates

| gate | command (built the TEST project first, then `--no-build`) | result |
|---|---|---|
| selection rails | `dotnet test Hrot.Presentation.Tests --no-build --filter FullyQualifiedName~Selection` | 60 / 0 (incl. `CE4049_*` ×2, `ReplayUiState2_*`) |
| Presentation full | `dotnet test Hrot.Presentation.Tests --no-build` | 873 / 1 — the red `MapCullingPolicyTests.ThePack_WiresCullingThroughTheSeam` is **pre-existing**: red at base `72930e0b2` in a clean worktree |
| ③ replay rail | `dotnet test Fdp.Toolkits.Tests --no-build --filter "FullyQualifiedName~CE1319_\|FullyQualifiedName~SquadDrill"` | 3 / 0 |
| record + data-policy rails | `dotnet test Hrot.ClusterRunner.Tests --no-build --filter "…RecordedManagedComponentsAreImmutable\|…EveryComponentTypeDeclaresItsDataPolicy"` | 3 / 0 |
| Brain tab | `dotnet test Hrot.Editor.AiShared.Tests --no-build --filter FullyQualifiedName~Brain` | 21 / 0 (4 skipped, pre-existing) |
| SimHost brain / squad / CGF | `dotnet test Hrot.SimHost.Tests --no-build --filter "Cgf\|Cognitive\|Squad\|BrainSystems"` | 295 / 0 |
| red-proofs | `SelectionState` back to `NoScenario` only ⇒ red · `Sync` not rewriting ⇒ red · `Put` not writing `SquadDrillState` ⇒ red · ② red before the change | ✅ all red |
| `tracker-counts.py --check` | | OK |

Ids allocated: component ids **465** (`SelectionStore`), **466** (`SquadDrillState`). Tracker rows `CE-4049`, `CE-4047` closed.

**Your FYI on the Timeline** is taken: R0 ① (playback fidelity) is consumed from your H1, not rebuilt.

---
_ui lane, 2026-10-26_
